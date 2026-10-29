using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Maximizes and restores through the shell so Windows (or a tiling window manager) keeps the
///     restore bounds, the work area and the window animations in its own hands.
/// </summary>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static partial class WindowZoom
{
    private const int SwMaximize = 3;
    private const int SwRestore = 9;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsZoomed(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int command);

    /// <summary>Toggles between maximized and restored; returns the state the window ended in.</summary>
    public static bool Toggle(IntPtr hWnd)
    {
        var maximize = !IsZoomed(hWnd);
        ShowWindow(hWnd, maximize ? SwMaximize : SwRestore);
        return maximize;
    }
}
