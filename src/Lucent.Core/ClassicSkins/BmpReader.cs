using System.Buffers.Binary;

namespace Lucent.ClassicSkins;

/// <summary>
/// Reads Windows .bmp files, which is what every classic Winamp skin is drawn in. Both apps need the same
/// pictures from the same files, so this is done here rather than by WPF or Avalonia. It reads every kind
/// skins were saved as: 1, 4, 8, 16, 24 and 32 bits a pixel, run-length encoded or not, bottom-up or top-down,
/// and the old OS/2 header some early editors wrote.
/// </summary>
public static class BmpReader
{
    private const uint Opaque = 0xFF000000;

    /// <summary>The picture in <paramref name="data"/>, or null when it isn't a .bmp Lucent can read.</summary>
    public static SkinImage? Read(ReadOnlySpan<byte> data)
    {
        try
        {
            return Decode(data);
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    private static SkinImage? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || data[0] != 'B' || data[1] != 'M')
        {
            return null;
        }

        var pixelStart = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[10..]);
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
        int width, height, bits, compression = 0, paletteEntrySize, paletteCount = 0;
        uint redMask = 0, greenMask = 0, blueMask = 0;
        if (headerSize == 12)
        {
            // OS/2: 16-bit sizes and three-byte palette entries.
            width = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(data[20..]);
            bits = BinaryPrimitives.ReadUInt16LittleEndian(data[24..]);
            paletteEntrySize = 3;
        }
        else if (headerSize >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
            bits = BinaryPrimitives.ReadUInt16LittleEndian(data[28..]);
            compression = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[30..]);
            paletteCount = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[46..]);
            paletteEntrySize = 4;
            if (compression == 3)
            {
                // Bit fields: the masks follow a 40-byte header, or are part of a longer one.
                redMask = BinaryPrimitives.ReadUInt32LittleEndian(data[54..]);
                greenMask = BinaryPrimitives.ReadUInt32LittleEndian(data[58..]);
                blueMask = BinaryPrimitives.ReadUInt32LittleEndian(data[62..]);
            }
        }
        else
        {
            return null;
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
        {
            return null;
        }

        uint[] palette = Array.Empty<uint>();
        if (bits <= 8)
        {
            if (paletteCount <= 0 || paletteCount > 1 << bits)
            {
                paletteCount = 1 << bits;
            }

            palette = new uint[1 << bits];
            var paletteStart = 14 + headerSize + (compression == 3 && headerSize == 40 ? 12 : 0);
            for (var i = 0; i < paletteCount && paletteStart + i * paletteEntrySize + 2 < data.Length; i++)
            {
                var entry = data.Slice(paletteStart + i * paletteEntrySize, 3);
                palette[i] = Opaque | (uint)entry[2] << 16 | (uint)entry[1] << 8 | entry[0];
            }
        }

        var pixels = new uint[width * height];
        var source = data[Math.Min(pixelStart, data.Length)..];
        switch (compression)
        {
            case 0 or 3 when bits is 1 or 4 or 8 or 16 or 24 or 32:
                ReadRows(source, pixels, width, height, bits, palette, compression == 3 ? (redMask, greenMask, blueMask) : null);
                break;
            case 1 when bits == 8:
            case 2 when bits == 4:
                ReadRunLength(source, pixels, width, height, bits, palette);
                break;
            default:
                return null;
        }

        if (!topDown)
        {
            // Stored bottom row first; turn it the right way up.
            var row = new uint[width];
            for (var y = 0; y < height / 2; y++)
            {
                var top = pixels.AsSpan(y * width, width);
                var bottom = pixels.AsSpan((height - 1 - y) * width, width);
                top.CopyTo(row);
                bottom.CopyTo(top);
                row.CopyTo(bottom);
            }
        }

        return new SkinImage(width, height, pixels);
    }

    /// <summary>Uncompressed rows, each padded to four bytes. Rows the file is too short for stay black.</summary>
    private static void ReadRows(ReadOnlySpan<byte> source, uint[] pixels, int width, int height, int bits,
        uint[] palette, (uint Red, uint Green, uint Blue)? masks)
    {
        var stride = (width * bits + 31) / 32 * 4;
        var fields = masks ?? (bits == 16 ? (0x7C00u, 0x03E0u, 0x001Fu) : (0x00FF0000u, 0x0000FF00u, 0x000000FFu));
        for (var y = 0; y < height; y++)
        {
            var rowStart = y * stride;
            var output = pixels.AsSpan(y * width, width);
            if (rowStart + stride > source.Length)
            {
                output.Fill(Opaque);
                continue;
            }

            var row = source.Slice(rowStart, stride);
            for (var x = 0; x < width; x++)
            {
                output[x] = bits switch
                {
                    1 => palette[(row[x >> 3] >> (7 - (x & 7))) & 1],
                    4 => palette[(row[x >> 1] >> ((x & 1) == 0 ? 4 : 0)) & 0xF],
                    8 => palette[row[x]],
                    16 => FromFields(BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..]), fields),
                    24 => Opaque | (uint)row[x * 3 + 2] << 16 | (uint)row[x * 3 + 1] << 8 | row[x * 3],
                    _ => masks is null
                        ? Opaque | (BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]) & 0xFFFFFF)
                        : FromFields(BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]), fields),
                };
            }
        }
    }

    /// <summary>RLE8 and RLE4: runs of one colour, literal stretches, and jumps. Skipped pixels are black.</summary>
    private static void ReadRunLength(ReadOnlySpan<byte> source, uint[] pixels, int width, int height, int bits, uint[] palette)
    {
        Array.Fill(pixels, Opaque | palette[0]);
        int x = 0, y = 0, at = 0;
        void Put(int index)
        {
            if (x < width && y < height)
            {
                pixels[y * width + x] = palette[index];
            }

            x++;
        }

        while (at + 1 < source.Length && y < height)
        {
            int count = source[at++], value = source[at++];
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    Put(bits == 8 ? value : (i & 1) == 0 ? value >> 4 : value & 0xF);
                }

                continue;
            }

            switch (value)
            {
                case 0:
                    x = 0;
                    y++;
                    break;
                case 1:
                    return;
                case 2:
                    if (at + 1 >= source.Length)
                    {
                        return;
                    }

                    x += source[at++];
                    y += source[at++];
                    break;
                default:
                    // A literal stretch of 'value' pixels, padded to a whole number of 16-bit words.
                    var bytes = bits == 8 ? value : (value + 1) / 2;
                    for (var i = 0; i < value && at + (bits == 8 ? i : i / 2) < source.Length; i++)
                    {
                        var b = source[at + (bits == 8 ? i : i / 2)];
                        Put(bits == 8 ? b : (i & 1) == 0 ? b >> 4 : b & 0xF);
                    }

                    at += (bytes + 1) & ~1;
                    break;
            }
        }
    }

    private static uint FromFields(uint value, (uint Red, uint Green, uint Blue) masks) =>
        Opaque | (uint)Channel(value, masks.Red) << 16 | (uint)Channel(value, masks.Green) << 8 | Channel(value, masks.Blue);

    /// <summary>One colour channel picked out by its mask and stretched to 0–255.</summary>
    private static byte Channel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        return (byte)(((value & mask) >> shift) * 255 / max);
    }
}
