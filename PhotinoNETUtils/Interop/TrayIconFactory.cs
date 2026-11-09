using PhotinoNetUtils.Platform;

namespace PhotinoNetUtils.Interop;

/// <summary>Picks the tray implementation for the current platform.</summary>
public static class TrayIconFactory
{
    /// <summary>
    ///     True when the Linux toolkit is ready to create a tray icon. Always true elsewhere.
    /// </summary>
    public static bool IsToolkitReady => !OperatingSystem.IsLinux() || LinuxTrayIcon.IsToolkitReady;

    /// <summary>
    ///     Creates the tray icon. On Linux the returned icon can only be started once GTK has a
    ///     display connection; check <see cref="IsToolkitReady" /> first there.
    /// </summary>
    /// <param name="appId">Stable application id, used for the Linux indicator.</param>
    /// <param name="tooltip">Hover text and indicator title.</param>
    /// <param name="iconPath">A real <c>.ico</c> (Windows) or <c>.png</c> (Linux) path.</param>
    /// <param name="onOpen">Primary click: show the main window.</param>
    /// <param name="menu">Context menu entries, in display order.</param>
    public static ITrayIcon Create(
        string appId,
        string tooltip,
        string? iconPath,
        Action onOpen,
        IReadOnlyList<TrayMenuItem> menu) =>
        OperatingSystem.IsWindows()
            ? new WindowsTrayIcon(tooltip, iconPath, onOpen, menu)
            : OperatingSystem.IsLinux() && LinuxDiagnostics.AppIndicatorLibrary is not null
                ? new LinuxTrayIcon(appId, tooltip, iconPath, onOpen, menu)
                : new NullTrayIcon();

    /// <summary>Creates the standard Open / Settings / Exit tray menu, optionally with a reload action.</summary>
    public static ITrayIcon CreateStandard(
        string appId,
        string tooltip,
        string? iconPath,
        string openLabel,
        string settingsLabel,
        string exitLabel,
        Action onOpen,
        Action onSettings,
        Action onExit,
        Action? onReload = null,
        string reloadLabel = "Reload interface")
    {
        var menu = new List<TrayMenuItem>
        {
            new(openLabel, onOpen)
        };
        if (onReload is not null) menu.Add(new(reloadLabel, onReload));
        menu.Add(new(settingsLabel, onSettings));
        menu.Add(new(exitLabel, onExit, true));

        return Create(appId, tooltip, iconPath, onOpen, menu);
    }

    /// <summary>Starts a tray icon on GTK's UI thread once its display is ready.</summary>
    public static void StartWhenReady(ITrayIcon tray, Action<Action> runOnMainWindow, Action<bool> onStarted)
    {
        if (!OperatingSystem.IsLinux())
        {
            onStarted(tray.Start());
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 100 && !IsToolkitReady; attempt++)
                await Task.Delay(100);

            runOnMainWindow(() => onStarted(tray.Start()));
        });
    }
}