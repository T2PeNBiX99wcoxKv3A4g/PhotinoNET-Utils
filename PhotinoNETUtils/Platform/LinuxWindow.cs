using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Extensions;
using PhotinoNetUtils.Platform;
using PhotinoNetUtils.Utils;
using Serilog;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Window chrome on GTK. Photino exposes no widget handle on Linux, so the toplevel is looked up
///     by its (app-controlled) title and asked to start a compositor-driven move, resize or zoom.
/// </summary>
/// <remarks>
///     The compositor does the actual work, which is also what makes this work on Wayland: GDK turns
///     the request into an xdg_toplevel move/resize and ignores the coordinates, so the values below
///     only matter on X11.
/// </remarks>
[PublicAPI]
[SupportedOSPlatform("linux")]
public static partial class LinuxWindow
{
    private const string GtkLibrary = "libgtk-3.so.0";
    private const int LeftButton = 1;

    // GdkWindowEdge: north-west, north, north-east, west, east, south-west, south, south-east.
    private static readonly Dictionary<string, int> Edges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["topleft"] = 0,
        ["top"] = 1,
        ["topright"] = 2,
        ["left"] = 3,
        ["right"] = 4,
        ["bottomleft"] = 5,
        ["bottom"] = 6,
        ["bottomright"] = 7
    };

    public static bool BeginDrag(string title) =>
        WithWindow(title, window =>
        {
            var (x, y) = Pointer();
            gtk_window_begin_move_drag(window, LeftButton, x, y, EventTime());
        });

    public static bool BeginResize(string title, string direction) =>
        Edges.TryGetValue(direction, out var edge) &&
        WithWindow(title, window =>
        {
            var (x, y) = Pointer();
            gtk_window_begin_resize_drag(window, edge, LeftButton, x, y, EventTime());
        });

    /// <summary>Toggles the zoomed state and reports the state the window ended up in.</summary>
    public static bool ToggleMaximize(string title) =>
        Try.Run(() =>
        {
            var window = FindByTitle(title);
            if (window == IntPtr.Zero)
                return false;

            if (gtk_window_is_maximized(window))
                gtk_window_unmaximize(window);
            else
                gtk_window_maximize(window);

            return gtk_window_is_maximized(window);
        }).GetOrElse(ex =>
        {
            Log.Debug(ex, "GTK window lookup for {Title} failed", title);
            return false;
        });

    private static (int X, int Y) Pointer() =>
        LinuxGtk.TryGetPointerPosition(out var x, out var y) ? (x, y) : (0, 0);

    private static uint EventTime() =>
        Try.Run(gtk_get_current_event_time).GetOrElse(_ => 0u);

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
                if (window == IntPtr.Zero)
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
    private static partial void gtk_window_begin_move_drag(IntPtr window, int button, int rootX, int rootY,
        uint timestamp);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_begin_resize_drag(IntPtr window, int edge, int button, int rootX,
        int rootY, uint timestamp);

    [LibraryImport(GtkLibrary)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool gtk_window_is_maximized(IntPtr window);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_maximize(IntPtr window);

    [LibraryImport(GtkLibrary)]
    private static partial void gtk_window_unmaximize(IntPtr window);

    [LibraryImport(GtkLibrary)]
    private static partial uint gtk_get_current_event_time();

    [LibraryImport("libglib-2.0.so.0")]
    private static partial void g_list_free(IntPtr list);
}
