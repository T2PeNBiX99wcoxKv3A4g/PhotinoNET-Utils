using System.Reflection;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

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
        string? linuxResource = null)
    {
        var (resource, fileName) = OperatingSystem.IsWindows()
            ? (windowsResource ?? $"{appName}.Resources.icon.ico", "icon.ico")
            : (linuxResource ?? $"{appName}.Resources.icon.png", "icon.png");

        return Extract(assembly, appName, resource, fileName);
    }

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

            var path = Path.Combine(AppPaths.DataDirectory(appName), fileName);
            FileUtil.WriteAllBytesIfChanged(path, icon);

            return path;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to extract the app icon");
            return null;
        });
}