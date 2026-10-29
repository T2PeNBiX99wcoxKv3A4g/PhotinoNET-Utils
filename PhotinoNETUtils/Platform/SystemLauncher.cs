using System.Diagnostics;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Hands a URL or a folder to the system: the shell on Windows, <c>xdg-open</c> elsewhere. This is
///     the same path a click on a link takes, so whatever the user has registered as the default
///     handler decides — which may be the app itself.
/// </summary>
[PublicAPI]
public static class SystemLauncher
{
    /// <summary>Opens <paramref name="target" /> with the system's default handler.</summary>
    public static void Open(string target) =>
        Process.Start(new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? target : "xdg-open",
            Arguments = OperatingSystem.IsWindows() ? "" : $"\"{target}\"",
            // ShellExecute is what makes Windows ask the shell instead of starting the string itself.
            UseShellExecute = OperatingSystem.IsWindows()
        });
}