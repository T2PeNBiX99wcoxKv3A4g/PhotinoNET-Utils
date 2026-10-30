using JetBrains.Annotations;

namespace PhotinoNetUtils;

/// <summary>
///     The per-user folders a Photino app owns, so every app does not have to spell out the same
///     <see cref="Environment.SpecialFolder" /> combinations.
/// </summary>
/// <remarks>
///     The split follows what the platform expects: settings and anything else the user would miss
///     live under <c>%APPDATA%</c>, while caches and extracted files that can be recreated live
///     under <c>%LOCALAPPDATA%</c> and never roam.
/// </remarks>
[PublicAPI]
public static class AppPaths
{
    /// <summary>
    ///     Where the app's settings and other user data live: <c>%APPDATA%/{appName}</c> (XDG's config
    ///     home on Linux), created on demand. Keep <c>settings.json</c>, scripts, history and similar
    ///     files here.
    /// </summary>
    public static string SettingsDirectory(string appName) =>
        Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), appName));

    /// <summary>
    ///     A local folder for disposable files: <c>%LOCALAPPDATA%/{appName}</c> (XDG's data home on
    ///     Linux), created on demand. It is the natural content root for
    ///     <see cref="Hosting.StaticServer" />, and the place for the extracted icon, the web view
    ///     cache and anything else that can be recreated, so it never roams.
    /// </summary>
    public static string DataDirectory(string appName) =>
        Ensure(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appName));

    /// <summary>
    ///     Where <see cref="Logging" /> writes its rolling files: <c>%APPDATA%/{appName}/logs</c>
    ///     (XDG's config home on Linux), created on demand.
    /// </summary>
    public static string LogDirectory(string appName) =>
        Ensure(Path.Combine(SettingsDirectory(appName), "logs"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
