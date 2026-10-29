using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Interop;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Removes the native title bar while keeping an ordinary resizable window frame. A popup
///     "chromeless" window is invisible to the shell and to tiling window managers (GlazeWM never
///     manages one), so the frame stays and only the non-client area is dropped: the interface then
///     draws its own title bar inside the client area.
/// </summary>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static partial class WindowFrame
{
    private const int GwlWndProc = -4;
    private const uint WmNcCalcSize = 0x0083;
    private const uint SwpFrameChanged = 0x0020;

    private static NativeMethods.WndProcDelegate? _windowProc;
    private static IntPtr _previous;

    public static void RemoveTitleBar(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return;

        _windowProc = WindowProc;
        _previous = NativeMethods.SetWindowLongPtr(hWnd, GwlWndProc,
            Marshal.GetFunctionPointerForDelegate(_windowProc));

        // The new non-client calculation only takes effect once the frame is recalculated.
        NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, 0, 0, 0, 0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder
            | NativeMethods.SwpNoActivate | SwpFrameChanged);
    }

    private static IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg != WmNcCalcSize || wParam == IntPtr.Zero)
            return CallWindowProc(_previous, hWnd, msg, wParam, lParam);

        // The whole window becomes client area. A maximized window is inflated by the resize frame,
        // so clamp that back to the work area instead of running the interface off the screen.
        if (IsZoomed(hWnd) && TryGetWorkArea(hWnd, out var workArea))
        {
            Marshal.WriteInt32(lParam, 0, workArea.Left);
            Marshal.WriteInt32(lParam, 4, workArea.Top);
            Marshal.WriteInt32(lParam, 8, workArea.Right);
            Marshal.WriteInt32(lParam, 12, workArea.Bottom);
        }

        return IntPtr.Zero;
    }

    private static bool TryGetWorkArea(IntPtr hWnd, out NativeMethods.Rect workArea)
    {
        workArea = default;
        var monitor = MonitorFromWindow(hWnd, NativeMethods.MonitorDefaultToNearest);
        var info = new NativeMethods.MonitorInfo
        {
            cbSize = Marshal.SizeOf<NativeMethods.MonitorInfo>()
        };

        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
            return false;

        workArea = info.rcWork;
        return true;
    }

    [LibraryImport("user32.dll", EntryPoint = "CallWindowProcW")]
    private static partial IntPtr CallWindowProc(IntPtr previous, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hWnd);
}