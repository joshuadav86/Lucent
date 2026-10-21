using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Writes a picture as a PNG file: 8-bit RGB, each row filtered against the one above it ("Up", which suits video
/// frames well), then compressed. Written in C#, so it's the same on Windows and the Mac.
/// </summary>
public static class PngWriter
{
    private static readonly uint[] CrcTable = MakeCrcTable();

    /// <summary>Writes a 32-bit BGRA picture (the alpha byte is ignored) to <paramref name="target"/>.</summary>
    public static unsafe void Write(Stream target, nint bgra, int width, int height, int stride,
        CompressionLevel level = CompressionLevel.Fastest)
    {
        target.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(target, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, level, leaveOpen: true))
        {
            var row = new byte[1 + width * 3];
            var above = new byte[width * 3];
            var current = new byte[width * 3];
            for (var y = 0; y < height; y++)
            {
                var pixels = new ReadOnlySpan<byte>((byte*)bgra + (long)y * stride, width * 4);
                for (var x = 0; x < width; x++)
                {
                    current[x * 3] = pixels[x * 4 + 2];
                    current[x * 3 + 1] = pixels[x * 4 + 1];
                    current[x * 3 + 2] = pixels[x * 4];
                }

                row[0] = 2;
                for (var i = 0; i < current.Length; i++)
                {
                    row[1 + i] = (byte)(current[i] - above[i]);
                }

                zlib.Write(row);
                (above, current) = (current, above);
            }
        }

        WriteChunk(target, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(target, "IEND", ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream target, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> four = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(four, data.Length);
        target.Write(four);
        var name = Encoding.ASCII.GetBytes(type);
        target.Write(name);
        target.Write(data);
        var crc = Crc(Crc(0xFFFFFFFF, name), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(four, crc);
        target.Write(four);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
