using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;
using ShellLink;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Keeps the Start Menu shortcut Photino creates for a window in step with the running executable.
/// </summary>
/// <remarks>
///     <para>
///         Photino writes that shortcut when a window registers for notifications — Windows needs one to
///         attribute toasts to — and it only ever writes it once. A build that later moves, from a
///         folder to an installer's location or to a new folder after an upgrade, therefore leaves a
///         shortcut that keeps starting the old copy.
///     </para>
///     <para>
///         <see cref="Refresh" /> compares the shortcut's target with the running executable, rewrites
///         it when they differ, and creates it when it is missing. Nothing is written while it is
///         already correct.
///     </para>
/// </remarks>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static class StartMenuShortcut
{
    /// <summary>Points the shortcut named after <paramref name="windowTitle" /> at this executable.</summary>
    public static void Refresh(string windowTitle) => Refresh(windowTitle, Environment.ProcessPath);

    /// <summary>Points the shortcut named after <paramref name="windowTitle" /> at <paramref name="executablePath" />.</summary>
    public static void Refresh(string windowTitle, string? executablePath)
    {
        if (string.IsNullOrEmpty(windowTitle) || string.IsNullOrEmpty(executablePath))
            return;

        // Called while the window is being created, so a failure here must never take the app down.
        Try.Run(() =>
        {
            var link = ShortcutPath(windowTitle);

            var current = File.Exists(link)
                ? Try.Run(() => TargetOf(link)).GetOrNull()
                : null;

            if (current is not null && current.Equals(executablePath, StringComparison.OrdinalIgnoreCase))
                return;

            Log.Information("Repointing the Start Menu shortcut from {Old} to {New}",
                current ?? "(missing)", executablePath);

            WindowsShortcut.Create(link, executablePath, "", Path.GetDirectoryName(executablePath) ?? "",
                windowTitle);
        }).OnFailure(ex => Log.Warning(ex, "Failed to refresh the Start Menu shortcut for {Title}", windowTitle));
    }

    /// <summary>
    ///     Renames the shortcut Photino writes for notifications, which it names after the executable,
    ///     to <paramref name="windowTitle" /> so the Start Menu shows the name people know. When a
    ///     shortcut with that name already exists, the copy named after the executable is removed
    ///     instead, because two entries for one app confuse the user.
    /// </summary>
    public static void Adopt(string windowTitle, string executableName)
    {
        if (string.IsNullOrEmpty(windowTitle) || string.IsNullOrEmpty(executableName))
            return;

        Try.Run(() =>
        {
            var source = ShortcutPath(executableName);
            if (!File.Exists(source))
                return;

            var target = ShortcutPath(windowTitle);

            if (!File.Exists(target))
            {
                Log.Information("Renaming the Start Menu shortcut {Old} to {New}", source, target);
                File.Move(source, target);
                return;
            }

            // Both exist, so the titled one already opens the app; the second entry only makes the
            // Start Menu show one program twice.
            Log.Information("Removing the duplicate Start Menu shortcut {Path}", source);
            File.Delete(source);
        }).OnFailure(ex => Log.Warning(ex, "Failed to rename the Start Menu shortcut for {Title}", windowTitle));
    }

    /// <summary>The file Photino names after the window, in the per-user Start Menu's Programs folder.</summary>
    private static string ShortcutPath(string windowTitle) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), $"{windowTitle}.lnk");

    /// <summary>The target stored in a shortcut, or null when it cannot be read.</summary>
    private static string? TargetOf(string link) => Shortcut.ReadFromFile(link).LinkTargetIDList?.Path;
}