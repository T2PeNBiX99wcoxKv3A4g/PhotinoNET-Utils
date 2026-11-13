using System.Diagnostics;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Per-user desktop entries and <c>xdg-mime</c>: the Linux counterpart of the Windows registry
///     keys the association services write. Everything lives under the user's own data folder, so
///     nothing needs root rights, and deleting the file undoes the change again.
/// </summary>
[SupportedOSPlatform("linux")]
[PublicAPI]
public static class LinuxDesktop
{
    private const string SystemDirectory = "/usr/share/applications";

    public static string UserDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");

    public static string UserPath(string name) => Path.Combine(UserDirectory, name);

    /// <summary>Writes (or replaces) a per-user desktop entry.</summary>
    public static void Write(string name, string content)
    {
        Directory.CreateDirectory(UserDirectory);
        File.WriteAllText(UserPath(name), content);
    }

    public static void Remove(string name) => Try.Run(() => File.Delete(UserPath(name)));

    /// <summary>The executable a desktop entry starts, or null when the entry cannot be read.</summary>
    public static string? ExecutableOf(string name)
    {
        var path = File.Exists(UserPath(name)) ? UserPath(name)
            : File.Exists(Path.Combine(SystemDirectory, name)) ? Path.Combine(SystemDirectory, name)
            : null;

        var line = path is null
            ? null
            : Try.Run(() => File.ReadLines(path)
                    .FirstOrDefault(entry => entry.StartsWith("Exec=", StringComparison.Ordinal)))
                .GetOrNull();

        return line is null ? null : FirstArgument(line["Exec=".Length..]);
    }

    /// <summary>The handler <c>xdg-mime</c> stored for a MIME type, or an empty string.</summary>
    public static string Query(string mimeType) => Run("xdg-mime", ["query", "default", mimeType]).Trim();

    public static void SetDefault(string desktop, string mimeType) =>
        _ = Run("xdg-mime", ["default", desktop, mimeType]);

    /// <summary>Runs a helper and keeps its standard output; a failure is simply no output.</summary>
    public static string Run(string command, string[] arguments) =>
        Try.Run(() => Process.RunAndCaptureText(command, arguments, TimeSpan.FromSeconds(3)).StandardOutput)
            .GetOrElse(_ => "");

    /// <summary>The first token of an Exec line, with its quotes removed.</summary>
    private static string? FirstArgument(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
            return null;

        if (text[0] != '"')
        {
            var space = text.IndexOf(' ');
            return space > 0 ? text[..space] : text;
        }

        var end = text.IndexOf('"', 1);
        return end > 1 ? text[1..end] : null;
    }
}