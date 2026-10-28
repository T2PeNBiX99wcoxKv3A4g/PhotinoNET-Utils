using System.Reflection;
using JetBrains.Annotations;
using Serilog;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils;

/// <summary>
///     The application icon. Both an <c>.ico</c> (Windows) and a <c>.png</c> (Linux, where GTK cannot
///     read ICO) are embedded, but Photino's <c>SetIconFile</c>, Win32's <c>LoadImage</c> and
///     libayatana-appindicator all want a real path, so the suitable one is written to disk on startup.
/// </summary>
[PublicAPI]
public static class AppIcon
{
    /// <summary>
    ///     Extracts the icon this platform can read (rewriting it only when it changed) and returns its
    ///     path, or <c>null</c> when the resource is unavailable. Resource names default to
    ///     <c>{appName}.Resources.icon.ico</c> / <c>.png</c>.
    /// </summary>
    public static string? EnsureExtracted(
        Assembly assembly,
        string appName,
        string? windowsResource = null,
        string? linuxResource = null) =>
        OperatingSystem.IsWindows()
            ? Extract(assembly, appName, windowsResource ?? $"{appName}.Resources.icon.ico", "icon.ico")
            : Extract(assembly, appName, linuxResource ?? $"{appName}.Resources.icon.png", "icon.png");

    private static string? Extract(Assembly assembly, string appName, string resourceName, string fileName) =>
        Try.Run<string?>(() =>
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream is null)
            {
                Log.Warning("Embedded icon {Resource} was not found", resourceName);
                return null;
            }

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var icon = buffer.ToArray();

            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName);
            Directory.CreateDirectory(directory);

            var path = Path.Combine(directory, fileName);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(icon))
                File.WriteAllBytes(path, icon);

            return path;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to extract the app icon");
            return null;
        });
}
