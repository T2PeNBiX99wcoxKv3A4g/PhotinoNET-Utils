using System.Reflection;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>Creates the per-user Windows or XDG autostart entry for a desktop application.</summary>
[PublicAPI]
public sealed class StartupRegistration
{
    private readonly Assembly _assembly;
    private readonly string _appName;
    private readonly string _comment;
    private readonly string _fallbackIcon;
    private readonly string? _linuxWindowClass;

    public StartupRegistration(Assembly assembly, string appName, string fallbackIcon, string? comment = null,
        string? linuxWindowClass = null)
    {
        _assembly = assembly;
        _appName = appName;
        _comment = comment ?? appName;
        _fallbackIcon = fallbackIcon;
        _linuxWindowClass = linuxWindowClass;
        EntryPath = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{appName}.lnk")
            : Path.Combine(ConfigHome(), "autostart", $"{appName}.desktop");
    }

    /// <summary>True on platforms with a per-user autostart location.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Path to the shortcut or desktop entry.</summary>
    public string EntryPath { get; }

    /// <summary>Whether the autostart entry currently exists.</summary>
    public bool IsEnabled => IsSupported && File.Exists(EntryPath);

    /// <summary>Rewrites an existing entry so it points at the current executable and icon.</summary>
    public bool Refresh()
    {
        return !IsEnabled || Create();
    }

    /// <summary>Creates or removes the autostart entry. Returns false when the executable is unknown.</summary>
    public bool SetEnabled(bool enabled)
    {
        if (!IsSupported)
            return true;

        if (enabled)
            return Create();

        if (File.Exists(EntryPath))
            File.Delete(EntryPath);

        return true;
    }

    private bool Create()
    {
        if (Environment.ProcessPath is not { Length: > 0 } executable)
        {
            Log.Warning("Cannot create the autostart entry: the executable path is unknown");
            return false;
        }

        if (OperatingSystem.IsLinux())
            WriteDesktopEntry(executable);
        else if (OperatingSystem.IsWindows())
            WindowsShortcut.Create(EntryPath, executable, "", Path.GetDirectoryName(executable) ?? "", _appName);

        return true;
    }

    [SupportedOSPlatform("linux")]
    private void WriteDesktopEntry(string executable)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);

        var icon = AppIcon.EnsureExtracted(_assembly, _appName) ?? _fallbackIcon;
        var lines = new List<string>
        {
            "[Desktop Entry]",
            "Type=Application",
            $"Name={_appName}",
            $"Comment={_comment}",
            $"Exec=\"{executable}\"",
            $"Icon={icon}"
        };

        if (_linuxWindowClass is { Length: > 0 } windowClass)
            lines.Add($"StartupWMClass={windowClass}");

        lines.AddRange(["Terminal=false", "X-GNOME-Autostart-enabled=true", ""]);
        File.WriteAllText(EntryPath, string.Join("\n", lines));
        File.SetUnixFileMode(EntryPath, File.GetUnixFileMode(EntryPath) | UnixFileMode.UserExecute);
    }

    private static string ConfigHome() => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } configured
        ? configured
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
}
