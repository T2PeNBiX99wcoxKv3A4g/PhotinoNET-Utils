using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JetBrains.Annotations;
using PhotinoNetUtils.Utils;

namespace PhotinoNetUtils.Platform;

/// <summary>
///     Turns a GDI bitmap (an icon or a shell thumbnail) into a PNG data URL the interface can show.
/// </summary>
[SupportedOSPlatform("windows")]
[PublicAPI]
public static partial class BitmapImage
{

    private const int DibRgbColors = 0;
    private const int BiRgb = 0;
    private const int MaxSize = 1024;

    /// <summary>Turns a GDI bitmap into PNG bytes, without the data URL wrapper.</summary>
    public static unsafe byte[]? ToPng(IntPtr bitmap)
    {
        if (bitmap == IntPtr.Zero)
            return null;

        if (GetObject(bitmap, Marshal.SizeOf<Bitmap>(), out var info) == 0)
            return null;

        var width = info.Width;
        var height = Math.Abs(info.Height);

        if (width is <= 0 or > MaxSize || height is <= 0 or > MaxSize)
            return null;

        var header = new BitmapInfoHeader
        {
            Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // top-down rows
            Planes = 1,
            BitCount = 32,
            Compression = BiRgb
        };

        var pixels = new byte[width * height * 4];
        var dc = GetDC(IntPtr.Zero);

        if (dc == IntPtr.Zero)
            return null;

        try
        {
            fixed (byte* buffer = pixels)
                if (GetDIBits(dc, bitmap, 0, (uint)height, buffer, ref header, DibRgbColors) == 0)
                    return null;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, dc);
        }

        // GDI hands the pixels over as BGRA; a bitmap without an alpha channel comes back transparent.
        var hasAlpha = false;
        for (var index = 3; index < pixels.Length; index += 4)
            if (pixels[index] != 0)
            {
                hasAlpha = true;
                break;
            }

        for (var index = 0; index < pixels.Length; index += 4)
        {
            (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);
            if (!hasAlpha)
                pixels[index + 3] = 0xFF;
        }

        return Png.Encode(width, height, pixels);
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(IntPtr handle, int size, out Bitmap bitmap);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr dc);

    // The pixels are passed as a fixed buffer, whose length the generator cannot infer from a byte[].
    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte* bits,
        ref BitmapInfoHeader header, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }
}