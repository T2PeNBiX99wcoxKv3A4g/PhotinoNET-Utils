using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Hosting;

/// <summary>Keeps the web view's data folder in step with the build that fills it.</summary>
[PublicAPI]
public static class WebViewCache
{
    /// <summary>
    ///     Returns the folder to pass to <c>SetTemporaryFilesPath</c>. When the marker there was written
    ///     by a different <paramref name="buildToken" />, the folder is deleted first, so an upgrade
    ///     never keeps serving the previous interface from the web view's own cache.
    /// </summary>
    public static string Prepare(string dataDirectory, string buildToken)
    {
        var cache = Path.Combine(dataDirectory, "webview");
        var marker = Path.Combine(dataDirectory, "webview.build");

        Try.Run(() =>
        {
            Directory.CreateDirectory(dataDirectory);

            if (File.Exists(marker) && File.ReadAllText(marker) != buildToken)
            {
                Log.Information("This is a different build; clearing the web view's cache");

                if (Directory.Exists(cache))
                    Directory.Delete(cache, true);
            }

            File.WriteAllText(marker, buildToken);
        }).OnFailure(ex => Log.Warning(ex, "Failed to prepare the web view's data folder"));

        return cache;
    }
}
