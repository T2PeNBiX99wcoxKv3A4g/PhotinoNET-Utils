using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Logs the Linux facts that decide which integrations can work: the session type (several window
///     behaviours are Wayland limitations, not bugs) and whether a tray library is installed.
/// </summary>
[SupportedOSPlatform("linux")]
public static class LinuxDiagnostics
{
    /// <summary>Soname of the AppIndicator library that is present, or null when there is none.</summary>
    public static string? AppIndicatorLibrary { get; } = ProbeAppIndicator();

    public static void Write()
    {
        Log.Information(
            "Linux session: type={SessionType} desktop={Desktop} wayland={Wayland} display={Display}",
            Variable("XDG_SESSION_TYPE"), Variable("XDG_CURRENT_DESKTOP"),
            Variable("WAYLAND_DISPLAY"), Variable("DISPLAY"));

        Log.Information("Linux paths: config={Config} local={Local}",
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        Log.Information("Tray support: {Support}", AppIndicatorLibrary is { } library
            ? $"{library} is available"
            : "no appindicator library found; the tray icon cannot be shown");

        // Native libraries report through GLib's logging; mirror it into the log file as well.
        LinuxGtk.RouteAllLogDomains();
    }

    private static string Variable(string name) =>
        Environment.GetEnvironmentVariable(name) ?? "<unset>";

    private static string? ProbeAppIndicator()
    {
        // The Ayatana fork is the maintained one; distributions may still ship the original.
        foreach (var name in new[]
                 {
                     "libayatana-appindicator3.so.1", "libappindicator3.so.1"
                 })
        {
            var found = Try.Run(() => NativeLibrary.TryLoad(name, out _) ? name : null)
                .GetOrElse(ex =>
                {
                    Log.Debug(ex, "Failed to probe {Library}", name);
                    return null;
                });

            if (found is not null)
                return found;
        }

        return null;
    }
}