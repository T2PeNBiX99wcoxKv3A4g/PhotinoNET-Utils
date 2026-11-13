using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using JetBrains.Annotations;

namespace PhotinoNetUtils.Utils;

/// <summary>
///     A minimal PNG writer for the shell icons: 8-bit RGBA, one filter byte per row, deflate via
///     <see cref="ZLibStream" />. Kept here instead of a drawing library so the icon path does not
///     pull a native graphics stack into a trimmed, self-contained build.
/// </summary>
[PublicAPI]
public static class Png
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();

        output.Write(Signature);
        WriteChunk(output, "IHDR", Header(width, height));
        WriteChunk(output, "IDAT", Deflate(width, height, rgba));
        WriteChunk(output, "IEND", []);

        return output.ToArray();
    }

    private static byte[] Header(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8; // bit depth
        header[9] = 6; // colour type: truecolour with alpha
        return header;
    }

    private static byte[] Deflate(int width, int height, byte[] rgba)
    {
        using var raw = new MemoryStream();
        var stride = width * 4;
        var prior = new byte[stride];

        // Each scanline carries the filter that compresses best. "None" is enough for a flat icon,
        // while a photo needs the predictor filters to stay small — the tile data is held in memory.
        var work = new byte[stride];
        var best = new byte[stride];

        for (var row = 0; row < height; row++)
        {
            var line = rgba.AsSpan(row * stride, stride);
            var bestScore = long.MaxValue;
            var bestFilter = 0;

            for (var filter = 0; filter < 5; filter++)
            {
                long score = 0;

                for (var index = 0; index < stride; index++)
                {
                    var left = index >= 4 ? line[index - 4] : 0;
                    var up = prior[index];
                    var upLeft = index >= 4 ? prior[index - 4] : 0;

                    var predicted = filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        _ => Paeth(left, up, upLeft)
                    };

                    var value = (byte)(line[index] - predicted);
                    work[index] = value;
                    score += value < 128 ? value : 256 - value;
                }

                if (score >= bestScore) continue;

                bestScore = score;
                bestFilter = filter;
                work.CopyTo(best, 0);
            }

            raw.WriteByte((byte)bestFilter);
            raw.Write(best);
            line.CopyTo(prior);
        }

        raw.Position = 0;

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, true))
            raw.CopyTo(zlib);

        return compressed.ToArray();
    }

    private static int Paeth(int left, int up, int upLeft)
    {
        var estimate = left + up - upLeft;
        var distanceLeft = Math.Abs(estimate - left);
        var distanceUp = Math.Abs(estimate - up);
        var distanceUpLeft = Math.Abs(estimate - upLeft);

        return distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft
            ? left
            : distanceUp <= distanceUpLeft
                ? up
                : upLeft;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);

        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data));
        output.Write(crc);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = type.Aggregate(0xFFFFFFFFu, (current, value) => CrcTable[(current ^ value) & 0xFF] ^ current >> 8);
        crc = data.Aggregate(crc, (current, value) => CrcTable[(current ^ value) & 0xFF] ^ current >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];

        for (var index = 0u; index < table.Length; index++)
        {
            var value = index;

            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320u ^ value >> 1 : value >> 1;

            table[index] = value;
        }

        return table;
    }
}