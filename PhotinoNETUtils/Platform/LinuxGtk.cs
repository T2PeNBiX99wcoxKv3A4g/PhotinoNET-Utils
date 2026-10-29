using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Utils;
using Serilog;
using Serilog.Events;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Direct GTK access for the few things Photino cannot do on Linux: finding a window to hide or
///     re-present it, making it RGBA-transparent, and setting the program name that becomes the Wayland
///     app id.
/// </summary>
/// <remarks>
///     Photino exposes no GTK widget handle and no hide call. The toplevel is therefore looked up by
///     its unique, app-controlled title. Every call is best-effort and never throws.
/// </remarks>
[SupportedOSPlatform("linux")]
public static partial class LinuxGtk
{
    private const string GtkLibrary = "libgtk-3.so.0";
    private const string GlibLibrary = "libglib-2.0.so.0";
    private const string GObjectLibrary = "libgobject-2.0.so.0";
    private const string CairoLibrary = "libcairo.so.2";
    private const string WebKitLibrary = "libwebkit2gtk-4.1.so.0";

    // G_LOG_LEVEL_ERROR = 1 << 2, CRITICAL = 1 << 3, WARNING = 1 << 4.
    private const int LogLevelError = 1 << 2;
    private const int LogLevelCritical = 1 << 3;
    private const int LogLevelWarning = 1 << 4;

    /// <summary>GDK_BUTTON1_MASK | GDK_BUTTON2_MASK | GDK_BUTTON3_MASK.</summary>
    private const uint PointerButtonMask = 1u << 8 | 1u << 9 | 1u << 10;

    private const int CairoOperatorClear = 0;

    private static readonly List<LogHandler> LogHandlers = [];

    private static Action<string>? _clipboardSink;
    private static IntPtr _clipboard;
    private static bool _clipboardConnected;

    private static readonly ClipboardOwnerChangedCallback ClipboardOwnerChanged = OnClipboardOwnerChanged;
    private static readonly ClipboardTextReceivedCallback ClipboardTextReceived = OnClipboardTextReceived;

    private static readonly IntPtr ClipboardTextReceivedPointer =
        Marshal.GetFunctionPointerForDelegate(ClipboardTextReceived);

    private static readonly DrawCallback DrawHandler = OnDraw;

    /// <summary>
    ///     Whether the screen supports RGBA visuals at all. This can be answered without a window, which
    ///     matters because the decision has to be taken before the popup is created.
    /// </summary>
    public static bool CanBeTransparent =>
        Try.Run(() => gdk_screen_get_rgba_visual(gdk_screen_get_default()) != IntPtr.Zero)
            .GetOrElse(ex =>
            {
                Log.Debug(ex, "Could not query the screen's RGBA visual");
                return false;
            });

    /// <summary>
    ///     Hides a toplevel by title. Returns false when it could not be found. Unmapping is what takes
    ///     a window out of the taskbar here: iconifying it only minimises it, which leaves it there.
    /// </summary>
    public static bool Hide(string title) => WithWindow(title, gtk_widget_hide);

    /// <summary>Un-hides and raises a toplevel by title. Returns false when it could not be found.</summary>
    public static bool Present(string title) => WithWindow(title, window =>
    {
        gtk_widget_show(window);
        gtk_window_present(window);
    });

    /// <summary>
    ///     Shows a toplevel kept above the others without asking for focus, for overlays like the
    ///     floating selection button that must not disturb whatever the user is typing into.
    /// </summary>
    public static bool ShowOnTop(string title) => WithWindow(title, window =>
    {
        gtk_widget_show(window);
        gtk_window_set_keep_above(window, true);
    });

    /// <summary>
    ///     Asks the window manager to leave the toplevel out of the taskbar, so its close button can never
    ///     be reached. Best-effort: Wayland has no taskbar hint and simply ignores it.
    /// </summary>
    public static bool SkipTaskbar(string title) =>
        WithWindow(title, window => gtk_window_set_skip_taskbar_hint(window, true));

    /// <summary>
    ///     Whether a toplevel with this title still exists. Hidden toplevels are still listed, so this
    ///     reports false only once the window is really gone.
    /// </summary>
    public static bool Exists(string title) =>
        Try.Run(() => FindByTitle(title) != IntPtr.Zero).GetOrElse(_ => false);

    /// <summary>Sets the program name, which GTK reports as the Wayland app id.</summary>
    public static void SetProgramName(string name) =>
        Try.Run(() => g_set_prgname(name)).OnFailure(ex => Log.Debug(ex, "Failed to set the program name"));

    /// <summary>
    ///     Connects a GObject signal. The caller has to keep the callback alive. Generic so the
    ///     AOT-safe <see cref="Marshal.GetFunctionPointerForDelegate{TDelegate}" /> overload is used.
    /// </summary>
    public static void Connect<TDelegate>(IntPtr instance, string signal, TDelegate callback, IntPtr data)
        where TDelegate : Delegate =>
        g_signal_connect_data(instance, signal, Marshal.GetFunctionPointerForDelegate(callback), data,
            IntPtr.Zero, 0);

    /// <summary>
    ///     Reroutes a GLib log domain to Serilog at a fixed level. Used for libraries that announce their
    ///     own deprecation on every start and would otherwise keep printing it to stderr.
    /// </summary>
    public static void RouteLogDomain(string domain, LogEventLevel level) =>
        g_log_set_handler(domain, LogLevelWarning | LogLevelCritical | LogLevelError,
            Marshal.GetFunctionPointerForDelegate(Root((_, _, message, _) => Write(level, IntPtr.Zero, message))),
            IntPtr.Zero);

    /// <summary>
    ///     Reroutes every GLib domain that has no handler of its own — which is how GTK, WebKit and the
    ///     other native libraries report problems — so their warnings reach the log file, not only stderr.
    /// </summary>
    public static void RouteAllLogDomains() =>
        g_log_set_default_handler(
            Marshal.GetFunctionPointerForDelegate(
                Root((domain, level, message, _) => Write(ToSerilogLevel(level), domain, message))),
            IntPtr.Zero);

    /// <summary>Keeps a GLib callback alive: the library holds it for the life of the process.</summary>
    private static LogHandler Root(LogHandler handler)
    {
        LogHandlers.Add(handler);
        return handler;
    }

    private static void Write(LogEventLevel level, IntPtr domain, IntPtr message)
    {
        var name = domain == IntPtr.Zero ? "glib" : Marshal.PtrToStringUTF8(domain) ?? "glib";
        var text = (Marshal.PtrToStringUTF8(message) ?? string.Empty).TrimEnd();

        if (text.Length > 0)
            Log.Write(level, "[{Domain}] {Line}", name, text);
    }

    private static LogEventLevel ToSerilogLevel(int level) => level switch
    {
        LogLevelError or LogLevelCritical => LogEventLevel.Error,
        LogLevelWarning => LogEventLevel.Warning,
        _ => LogEventLevel.Debug
    };

    /// <summary>
    ///     Makes a toplevel RGBA-transparent, so its CSS rounded corners are not filled in. GTK needs an
    ///     RGBA visual plus a draw handler that clears the background, and the visual only takes effect
    ///     on a widget that is not realised yet — Photino shows the window before it calls back, so an
    ///     already realised widget is unrealised and realised again.
    /// </summary>
    public static bool MakeTransparent(string title) =>
        Try.Run(() =>
        {
            var window = FindByTitle(title);
            if (window == IntPtr.Zero)
            {
                Log.Warning("Could not find the {Title} window to make transparent", title);
                return false;
            }

            var visual = gdk_screen_get_rgba_visual(gtk_widget_get_screen(window));
            if (visual == IntPtr.Zero)
            {
                Log.Warning("The screen has no RGBA visual; {Title} stays opaque", title);
                return false;
            }

            var realized = gtk_widget_get_realized(window);
            if (realized)
                gtk_widget_unrealize(window);

            gtk_widget_set_visual(window, visual);
            gtk_widget_set_app_paintable(window, true);
            Connect(window, "draw", DrawHandler, IntPtr.Zero);

            if (realized)
            {
                gtk_widget_realize(window);
                gtk_widget_show(window);
            }

            Log.Information("{Title} is transparent (re-realised={ReRealised})", title, realized);
            return true;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to make {Title} transparent", title);
            return false;
        });

    /// <summary>
    ///     Clears the web view's own background. Without this the GTK window can be transparent and the
    ///     page can have transparent corners, and WebKit still paints opaque white behind them.
    /// </summary>
    public static bool MakeWebViewTransparent(string title) =>
        Try.Run(() =>
        {
            var webView = FindWebView(FindByTitle(title));
            if (webView == IntPtr.Zero)
            {
                Log.Warning("Could not find the web view inside {Title}", title);
                return false;
            }

            var clear = new GdkRgba
            {
                Alpha = 0
            };
            webkit_web_view_set_background_color(webView, ref clear);

            Log.Information("Web view background cleared");
            return true;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Failed to clear the web view background");
            return false;
        });

    /// <summary>
    ///     The pointer position in screen coordinates. Wayland deliberately hides the global pointer, so
    ///     this reports false there and the caller falls back to letting the compositor place the window.
    /// </summary>
    public static bool TryGetPointerPosition(out int x, out int y)
    {
        x = 0;
        y = 0;

        try
        {
            var display = gdk_display_get_default();
            if (display == IntPtr.Zero)
                return false;

            var seat = gdk_display_get_default_seat(display);
            if (seat == IntPtr.Zero)
                return false;

            var pointer = gdk_seat_get_pointer(seat);
            return pointer != IntPtr.Zero && gdk_device_get_position(pointer, IntPtr.Zero, out x, out y);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not read the pointer position");
            return false;
        }
    }

    /// <summary>
    ///     Whether a mouse button is down. GDK insists on a window to ask about, so the query goes
    ///     through the root window; without one it is skipped rather than passed a null, which would
    ///     trip a GDK assertion on every poll. Wayland only reports input aimed at this client's own
    ///     surfaces, so this is effectively X11-only; elsewhere it simply stays false.
    /// </summary>
    public static bool IsPointerButtonPressed() =>
        Try.Run(() =>
        {
            var root = gdk_get_default_root_window();
            if (root == IntPtr.Zero)
                return false;

            var display = gdk_display_get_default();
            if (display == IntPtr.Zero)
                return false;

            var seat = gdk_display_get_default_seat(display);
            if (seat == IntPtr.Zero)
                return false;

            var pointer = gdk_seat_get_pointer(seat);
            if (pointer == IntPtr.Zero)
                return false;

            gdk_device_get_state(pointer, root, IntPtr.Zero, out var mask);
            return (mask & PointerButtonMask) != 0;
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "Could not read the pointer state");
            return false;
        });

    /// <summary>
    ///     Watches the clipboard and reports every new text selection. The text is requested
    ///     asynchronously, so a busy or frozen clipboard owner can never block this process.
    /// </summary>
    public static bool WatchClipboard(Action<string> onText) =>
        Try.Run(() =>
        {
            _clipboardSink = onText;
            if (_clipboardConnected)
                return true;

            _clipboard = gtk_clipboard_get(gdk_atom_intern_static_string("CLIPBOARD"));
            if (_clipboard == IntPtr.Zero)
            {
                Log.Warning("The CLIPBOARD selection is unavailable");
                return false;
            }

            Connect(_clipboard, "owner-change", ClipboardOwnerChanged, IntPtr.Zero);
            _clipboardConnected = true;
            Log.Information("Watching the clipboard for copied URLs");
            return true;
        }).GetOrElse(ex =>
        {
            Log.Warning(ex, "Could not watch the clipboard");
            return false;
        });

    /// <summary>Stops asking for the clipboard's text. The signal stays connected, it just does nothing.</summary>
    public static void StopWatchingClipboard() => _clipboardSink = null;

    private static void OnClipboardOwnerChanged(IntPtr clipboard, IntPtr @event, IntPtr data)
    {
        if (_clipboardSink is null)
            return;

        gtk_clipboard_request_text(clipboard, ClipboardTextReceivedPointer, IntPtr.Zero);
    }

    private static void OnClipboardTextReceived(IntPtr clipboard, IntPtr text, IntPtr data)
    {
        if (text == IntPtr.Zero)
            return;

        if (Marshal.PtrToStringUTF8(text) is { Length: > 0 } value)
            _clipboardSink?.Invoke(value);
    }

    /// <summary>Depth-first search for the WebKitWebView inside a widget tree.</summary>
    private static IntPtr FindWebView(IntPtr widget)
    {
        if (widget == IntPtr.Zero)
            return IntPtr.Zero;

        if (g_type_check_instance_is_a(widget, webkit_web_view_get_type()) != 0)
            return widget;

        if (g_type_check_instance_is_a(widget, gtk_container_get_type()) == 0)
            return IntPtr.Zero;

        var children = gtk_container_get_children(widget);
        if (children == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            for (var node = children; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var found = FindWebView(Marshal.ReadIntPtr(node));
                if (found != IntPtr.Zero)
                    return found;
            }
        }
        finally
        {
            g_list_free(children);
        }

        return IntPtr.Zero;
    }

    /// <summary>Clears the window to fully transparent; the web view paints everything on top of it.</summary>
    private static int OnDraw(IntPtr widget, IntPtr cairo, IntPtr data)
    {
        cairo_set_operator(cairo, CairoOperatorClear);
        cairo_paint(cairo);

        // Let the default handler run too, so children still get drawn.
        return 0;
    }

    private static bool WithWindow(string title, Action<IntPtr> action) =>
        Try.Run(() =>
        {
            var window = FindByTitle(title);
            if (window == IntPtr.Zero)
                return false;

            action(window);
            return true;
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "GTK window lookup for {Title} failed", title);
            return false;
        });

    private static IntPtr FindByTitle(string title)
    {
        var list = gtk_window_list_toplevels();
        if (list == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            // GList is { gpointer data; GList* next; GList* prev; }.
            for (var node = list; node != IntPtr.Zero; node = Marshal.ReadIntPtr(node, IntPtr.Size))
            {
                var window = Marshal.ReadIntPtr(node);

                // A toplevel that is being torn down can still sit in this list for a moment, and
                // asking it for its title would trip a GTK assertion.
                if (window == IntPtr.Zero || g_type_check_instance_is_a(window, gtk_window_get_type()) == 0)
                    continue;

                var current = gtk_window_get_title(window);

                if (current != IntPtr.Zero && Marshal.PtrToStringUTF8(current) == title)
                    return window;
            }
        }
        finally
        {
            g_list_free(list);
        }

        return IntPtr.Zero;
    }

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_window_list_toplevels();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_window_get_title(IntPtr window);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_hide(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_show(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_unrealize(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_realize(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_present(IntPtr window);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_set_keep_above(IntPtr window, [MarshalAs(UnmanagedType.Bool)] bool setting);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_set_skip_taskbar_hint(IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool setting);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_get_default_root_window();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_clipboard_get(IntPtr selection);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_clipboard_request_text(IntPtr clipboard, IntPtr callback, IntPtr userData);

    [LibraryImport(GtkLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr gdk_atom_intern_static_string(string atomName);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_display_get_default();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_display_get_default_seat(IntPtr display);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_seat_get_pointer(IntPtr seat);

    [LibraryImport(GtkLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gdk_device_get_position(IntPtr device, IntPtr screen, out int x, out int y);

    [LibraryImport(GtkLibrary)]
    private static partial void gdk_device_get_state(IntPtr device, IntPtr window, IntPtr axes, out uint mask);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_widget_get_screen(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_screen_get_default();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gdk_screen_get_rgba_visual(IntPtr screen);

    [LibraryImport(GtkLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gtk_widget_get_realized(IntPtr widget);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_set_visual(IntPtr widget, IntPtr visual);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_widget_set_app_paintable(IntPtr widget,
        [MarshalAs(UnmanagedType.Bool)] bool paintable);

    [LibraryImport(GlibLibrary)]
    private static partial void g_list_free(IntPtr list);

    [LibraryImport(GlibLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial void g_set_prgname(string name);

    [LibraryImport(GlibLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial uint g_log_set_handler(string logDomain, int logLevels, IntPtr logFunc, IntPtr userData);

    [LibraryImport(GlibLibrary)]
    private static partial IntPtr g_log_set_default_handler(IntPtr logFunc, IntPtr userData);

    // g_signal_connect_data lives in GObject, not GLib.
    [LibraryImport(GObjectLibrary, StringMarshalling = StringMarshalling.Utf8)]
    private static partial ulong g_signal_connect_data(IntPtr instance, string detailedSignal, IntPtr handler,
        IntPtr data, IntPtr destroyData, int flags);

    [LibraryImport(CairoLibrary)]
    private static partial void cairo_set_operator(IntPtr cairo, int op);

    [LibraryImport(CairoLibrary)]
    private static partial void cairo_paint(IntPtr cairo);

    // ---- libgobject / libgtk (widget tree) ----

    [LibraryImport(GObjectLibrary)]
    private static partial int g_type_check_instance_is_a(IntPtr instance, IntPtr type);

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_container_get_type();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_window_get_type();

    [LibraryImport(GtkLibrary)]
    private static partial IntPtr gtk_container_get_children(IntPtr container);

    // ---- libwebkit2gtk ----

    [LibraryImport(WebKitLibrary)]
    private static partial IntPtr webkit_web_view_get_type();

    [LibraryImport(WebKitLibrary)]
    private static partial void webkit_web_view_set_background_color(IntPtr webView, ref GdkRgba color);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LogHandler(IntPtr domain, int level, IntPtr message, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClipboardOwnerChangedCallback(IntPtr clipboard, IntPtr @event, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClipboardTextReceivedCallback(IntPtr clipboard, IntPtr text, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct GdkRgba
    {
        public double Red;
        public double Green;
        public double Blue;
        public double Alpha;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DrawCallback(IntPtr widget, IntPtr cairo, IntPtr data);
}