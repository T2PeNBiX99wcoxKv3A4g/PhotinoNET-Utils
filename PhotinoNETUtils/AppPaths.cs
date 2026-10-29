using JetBrains.Annotations;

namespace PhotinoNetUtils;

/// <summary>
///     The per-user folders a Photino app owns, so every app does not have to spell out the same
///     <see cref="Environment.SpecialFolder" /> combinations.
/// </summary>
[PublicAPI]
public static class AppPaths
{
    /// <summary>
    ///     The app's own folder under <c>%LOCALAPPDATA%</c> (XDG's data home on Linux), created on
    ///     demand. It is the natural content root for <see cref="Hosting.StaticServer" />, which would
    ///     otherwise create its <c>wwwroot</c> next to the executable.
    /// </summary>
    public static string DataDirectory(string appName) =>
        Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName));

    /// <summary>
    ///     Where <see cref="Logging" /> writes its rolling files: <c>%APPDATA%/{appName}/logs</c>
    ///     (XDG's config home on Linux), created on demand.
    /// </summary>
    public static string LogDirectory(string appName) =>
        Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName, "logs"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}