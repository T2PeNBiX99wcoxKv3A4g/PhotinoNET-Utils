using System.Reflection;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>Creates the per-user Windows or XDG autostart entry for a desktop application.</summary>
[PublicAPI]
public sealed class StartupRegistration
{
    private readonly string _appName;
    private readonly Assembly _assembly;
    private readonly string _comment;
    private readonly string _entryName;
    private readonly string _fallbackIcon;
    private readonly string? _linuxWindowClass;

    /// <param name="entryName">
    ///     The name the shortcut or desktop entry carries, when it should read differently from the
    ///     identifier used for the icon and the data folder.
    /// </param>
    public StartupRegistration(Assembly assembly, string appName, string fallbackIcon, string? comment = null,
        string? linuxWindowClass = null, string? entryName = null)
    {
        _assembly = assembly;
        _appName = appName;
        _entryName = string.IsNullOrWhiteSpace(entryName) ? appName : entryName;
        _comment = comment ?? _entryName;
        _fallbackIcon = fallbackIcon;
        _linuxWindowClass = linuxWindowClass;
        EntryPath = EntryAt(_entryName);
    }

    /// <summary>
    ///     The entry a build before <c>entryName</c> used, when the name changed; it is cleaned up so
    ///     the user is left with one entry instead of two.
    /// </summary>
    private string? LegacyEntryPath => _entryName == _appName ? null : EntryAt(_appName);

    private static string EntryAt(string name) => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), $"{name}.lnk")
        : Path.Combine(ConfigHome(), "autostart", $"{name}.desktop");

    /// <summary>True on platforms with a per-user autostart location.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux();

    /// <summary>Path to the shortcut or desktop entry.</summary>
    public string EntryPath { get; }

    /// <summary>Whether the autostart entry currently exists.</summary>
    public bool IsEnabled => IsSupported && File.Exists(EntryPath);

    /// <summary>Rewrites an existing entry so it points at the current executable and icon.</summary>
    public bool Refresh() => !IsEnabled || Create();

    /// <summary>Creates or removes the autostart entry. Returns false when the executable is unknown.</summary>
    public bool SetEnabled(bool enabled)
    {
        if (!IsSupported)
            return true;

        if (enabled)
            return Create();

        if (File.Exists(EntryPath))
            File.Delete(EntryPath);

        // An entry an earlier build wrote under the identifier is removed with it.
        if (LegacyEntryPath is { } legacy && File.Exists(legacy))
            File.Delete(legacy);

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
            WindowsShortcut.Create(EntryPath, executable, "", Path.GetDirectoryName(executable) ?? "", _entryName);

        // A login entry written by an earlier build carried the identifier; keeping it would start
        // the app twice.
        if (LegacyEntryPath is { } legacy && File.Exists(legacy))
            Try.Run(() => File.Delete(legacy));

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
            $"Name={_entryName}",
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

    private static string ConfigHome() => Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is
        { Length: > 0 } configured
        ? configured
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
}