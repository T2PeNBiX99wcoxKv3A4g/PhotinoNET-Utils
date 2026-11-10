using System.Reflection;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;
using ShellLink;

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

    /// <summary>
    ///     Whether the entry starts the running executable. A shortcut left by a copy in another
    ///     folder is not this app starting, so the settings page shows the toggle as off.
    /// </summary>
    public bool IsCurrent => IsEnabled && SamePath(TargetPath, Environment.ProcessPath);

    /// <summary>The executable the entry starts, or null when the file cannot be read.</summary>
    public string? TargetPath => !IsEnabled
        ? null
        : OperatingSystem.IsWindows() ? WindowsTarget(EntryPath) : LinuxTarget(EntryPath);

    /// <summary>Compares two executable paths the way the platform compares file names.</summary>
    public static bool SamePath(string? first, string? second)
    {
        if (first is not { Length: > 0 } || second is not { Length: > 0 })
            return false;

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        return string.Equals(first.TrimEnd('\\', '/'), second.TrimEnd('\\', '/'), comparison);
    }

    [SupportedOSPlatform("windows")]
    private static string? WindowsTarget(string linkPath)
    {
        var shortcut = Try.Run(() => Shortcut.ReadFromFile(linkPath)).GetOrNull();
        if (shortcut is null)
            return null;

        if (shortcut.LinkTargetIDList?.Path is { Length: > 0 } target)
            return target;

        if (shortcut.LinkInfo is not { } info)
            return null;

        var basePath = info.LocalBasePathUnicode is { Length: > 0 } unicode ? unicode : info.LocalBasePath;
        var suffix = info.CommonPathSuffixUnicode is { Length: > 0 } suffixUnicode
            ? suffixUnicode
            : info.CommonPathSuffix;

        return $"{basePath}{suffix}" is { Length: > 0 } path ? path : null;
    }

    private static string? LinuxTarget(string entryPath)
    {
        var exec = Try.Run(() => File.ReadAllLines(entryPath))
            .GetOrNull()?
            .FirstOrDefault(line => line.StartsWith("Exec=", StringComparison.Ordinal));

        if (exec is null)
            return null;

        var value = exec["Exec=".Length..].Trim();
        if (!value.StartsWith('"'))
            return value.Split(' ')[0];

        var end = value.IndexOf('"', 1);
        return end > 1 ? value[1..end] : null;
    }

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