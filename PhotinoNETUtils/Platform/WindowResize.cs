using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Platform;

/// <summary>Starts a native window resize from one of the web-drawn edge handles.</summary>
[PublicAPI]
[SupportedOSPlatform("windows")]
public static partial class WindowResize
{
    private const uint WmNcLButtonDown = 0x00A1;

    private static readonly Dictionary<string, IntPtr> Directions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["left"] = 10,
        ["right"] = 11,
        ["top"] = 12,
        ["topleft"] = 13,
        ["topright"] = 14,
        ["bottom"] = 15,
        ["bottomleft"] = 16,
        ["bottomright"] = 17
    };

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReleaseCapture();

    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    private static partial IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static void Begin(IntPtr hWnd, string direction)
    {
        if (hWnd == IntPtr.Zero || !Directions.TryGetValue(direction, out var hitTest))
            return;

        ReleaseCapture();
        SendMessage(hWnd, WmNcLButtonDown, hitTest, IntPtr.Zero);
    }
}