using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;
using Serilog;
using Serilog.Events;

namespace PhotinoNetUtils.Interop;

/// <summary>
///     Tray icon on Linux through libayatana-appindicator, i.e. the StatusNotifierItem protocol that KDE
///     Plasma and the GNOME extension speak. The older XEmbed <c>GtkStatusIcon</c> is not an option:
///     Plasma only bridges XEmbed through <c>xembedsniproxy</c>, which is X11-only.
/// </summary>
/// <remarks>
///     Photino has no tray support at all, so this talks to GTK directly. Everything is best-effort — a
///     missing library or a failing call leaves the app running without a tray icon rather than crashing.
///     Every call must happen on the GTK thread, which is the one that created the first window.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed partial class LinuxTrayIcon(
    string appId,
    string tooltip,
    string? iconPath,
    Action onOpen,
    IReadOnlyList<TrayMenuItem> menu)
    : ITrayIcon
{
    private const string IndicatorLibrary = "libayatana-appindicator3.so.1";
    private const string GtkLibrary = "libgtk-3.so.0";

    private const int StatusPassive = 0;
    private const int StatusActive = 1;
    private const int CategoryApplicationStatus = 0;

    /// <summary>Roots the signal callbacks: a delegate handed to GTK must never be collected.</summary>
    private readonly List<Delegate> _callbacks = [];

    private IntPtr _indicator = IntPtr.Zero;

    static LinuxTrayIcon()
    {
        NativeLibrary.SetDllImportResolver(typeof(LinuxTrayIcon).Assembly, ResolveLibrary);

        // The library announces its own deprecation on every start. Its -glib replacement is a different,
        // GTK-free implementation with a different (GMenu) API, and is not packaged for Fedora, so the
        // message is kept in our log at debug level instead of being printed to stderr.
        LinuxGtk.RouteLogDomain("libayatana-appindicator", LogEventLevel.Debug);
    }

    /// <summary>
    ///     True once GTK has a display connection. libayatana-appindicator does not fail gracefully
    ///     without one — it aborts the process — so every call is guarded by this.
    /// </summary>
    public static bool IsToolkitReady =>
        Try.Run(() => gdk_display_get_default() != IntPtr.Zero)
            .GetOrElse(ex =>
            {
                Log.Debug(ex, "GTK is not available");
                return false;
            });

    public bool Start()
    {
        if (!IsToolkitReady)
        {
            Log.Warning("GTK has no display connection yet; skipping the tray icon");
            return false;
        }

        return Try.Run(() =>
        {
            _indicator = app_indicator_new(appId, iconPath ?? "file-manager", CategoryApplicationStatus);
            if (_indicator == IntPtr.Zero)
            {
                Log.Warning("AppIndicator could not be created; continuing without a tray icon");
                return false;
            }

            app_indicator_set_title(_indicator, tooltip);

            if (iconPath is { Length: > 0 })
                app_indicator_set_icon_full(_indicator, iconPath, tooltip);

            app_indicator_set_menu(_indicator, BuildMenu());
            app_indicator_set_status(_indicator, StatusActive);

            // A primary click (left button) only arrives as "activate" while a handler is connected;
            // with none, libayatana-appindicator falls back to popping the menu. The menu stays on the
            // right button either way.
            IndicatorActivateCallback activated = (_, _, _, _) => Dispatch(onOpen);
            _callbacks.Add(activated);
            LinuxGtk.Connect(_indicator, "activate", activated, IntPtr.Zero);

            Log.Information("Tray icon created via {Library}", LinuxDiagnostics.AppIndicatorLibrary);
            return true;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to create the tray icon");
            _indicator = IntPtr.Zero;
            return false;
        });
    }

    public void Dispose()
    {
        if (_indicator == IntPtr.Zero)
            return;

        // Only withdraw it: the process is on its way out and the OS reclaims the rest.
        Try.Run(() => app_indicator_set_status(_indicator, StatusPassive))
            .OnFailure(ex => Log.Debug(ex, "Failed to withdraw the tray icon"));

        _indicator = IntPtr.Zero;
    }

    /// <summary>
    ///     Builds the indicator's menu, shown on a right click (and on hosts that only support the menu).
    ///     A left click opens the window instead, through the indicator's "activate" signal.
    /// </summary>
    private IntPtr BuildMenu()
    {
        var handle = gtk_menu_new();

        foreach (var item in menu)
        {
            if (item.SeparatorBefore)
                gtk_menu_shell_append(handle, gtk_separator_menu_item_new());

            AddItem(handle, item.Label, item.Callback);
        }

        gtk_widget_show_all(handle);
        return handle;
    }

    private void AddItem(IntPtr menuHandle, string label, Action action)
    {
        var item = gtk_menu_item_new_with_label(label);

        ActivateCallback callback = (_, _) => Dispatch(action);
        _callbacks.Add(callback);

        LinuxGtk.Connect(item, "activate", callback, IntPtr.Zero);

        gtk_menu_shell_append(menuHandle, item);
    }

    /// <summary>
    ///     Runs the action off the GTK thread: these callbacks are raised from inside the GTK main loop,
    ///     and Photino's <c>Invoke</c> must not be called from the loop it is waiting on.
    /// </summary>
    private static void Dispatch(Action action) =>
        _ = Task.Run(() => Try.Run(action).OnFailure(ex => Log.Warning(ex, "Tray callback failed")));

    /// <summary>Resolves the indicator library, accepting either the Ayatana or the original soname.</summary>
    private static IntPtr ResolveLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != IndicatorLibrary)
            return IntPtr.Zero;

        var available = LinuxDiagnostics.AppIndicatorLibrary ?? libraryName;
        return NativeLibrary.TryLoad(available, out var handle) ? handle : IntPtr.Zero;
    }

    // ---- libayatana-appindicator3 ----

    [LibraryImport(IndicatorLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr app_indicator_new(string id, string iconName, int category);

    [LibraryImport(IndicatorLibrary)]
    private static partial void app_indicator_set_status(IntPtr indicator, int status);

    [LibraryImport(IndicatorLibrary)]
    private static partial void app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    [LibraryImport(IndicatorLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void app_indicator_set_icon_full(IntPtr indicator, string iconName, string iconDescription);

    [LibraryImport(IndicatorLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void app_indicator_set_title(IntPtr indicator, string title);

    // ---- libgtk-3 ----

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_display_get_default();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_menu_new();

    [LibraryImport(GtkLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr gtk_menu_item_new_with_label(string label);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_separator_menu_item_new();

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_menu_shell_append(IntPtr menuShell, IntPtr child);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_show_all(IntPtr widget);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActivateCallback(IntPtr widget, IntPtr data);

    /// <summary>AppIndicator::activate — a primary click, carrying the coordinates it happened at.</summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void IndicatorActivateCallback(IntPtr indicator, int x, int y, IntPtr data);
}