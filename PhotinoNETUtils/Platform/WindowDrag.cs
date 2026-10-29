using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>Starts a native window move from the web-drawn title bar.</summary>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static partial class WindowDrag
{
    private const uint WmNcLButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = 2;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static void Begin(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return;

        ReleaseCapture();
        SendMessage(hWnd, WmNcLButtonDown, HtCaption, IntPtr.Zero);
    }
}
