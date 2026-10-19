using System.Buffers.Binary;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// A picture an effect shows, as AVS loaded them with Windows' LoadImage: a .bmp file next to the preset,
/// read here without Windows. Pixels are 0x00RRGGBB, row by row from the top-left.
/// </summary>
internal sealed class AvsBitmap
{
    private AvsBitmap(int width, int height, int[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public int[] Pixels { get; }

    /// <summary>
    /// The file a preset names, looked for in <paramref name="folder"/> (the preset's own) as AVS did, but without
    /// minding upper and lower case, as Windows didn't. Null if there's no folder or no such file.
    /// </summary>
    public static string? Find(string? folder, string name)
    {
        if (string.IsNullOrEmpty(folder) || string.IsNullOrWhiteSpace(name) || !Directory.Exists(folder))
        {
            return null;
        }

        var relative = name.Replace('\\', '/').TrimStart('/');
        try
        {
            var direct = Path.Combine(folder, relative);
            if (File.Exists(direct))
            {
                return direct;
            }

            // Walk down the folders it names, matching each part's name in any case.
            var current = folder;
            var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < parts.Length && current is not null; i++)
            {
                var last = i == parts.Length - 1;
                var entries = last ? Directory.EnumerateFiles(current) : Directory.EnumerateDirectories(current);
                current = entries.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), parts[i], StringComparison.OrdinalIgnoreCase));
                if (last)
                {
                    return current;
                }
            }

            // Failing that, a file of that name right next to the preset.
            var fileName = Path.GetFileName(relative);
            return Directory.EnumerateFiles(folder)
                .FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), fileName, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Finds and reads the picture a preset names. Gives null, with the reason in <paramref name="problem"/>
    /// (for the error log), when it's missing or isn't a .bmp file this can read.
    /// </summary>
    public static AvsBitmap? Load(string? folder, string name, out string? problem)
    {
        var path = Find(folder, name);
        if (path is null)
        {
            problem = folder is null
                ? $"The picture \"{name}\" couldn't be looked for, as the preset isn't in a folder, so it's left out."
                : $"The picture \"{name}\" isn't next to the preset, so it's left out.";
            return null;
        }

        try
        {
            var bitmap = Decode(File.ReadAllBytes(path));
            problem = bitmap is null ? $"The picture \"{name}\" isn't a kind of .bmp file Lucent can read, so it's left out." : null;
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            problem = $"The picture \"{name}\" couldn't be read ({ex.Message}), so it's left out.";
            return null;
        }
    }

    /// <summary>
    /// Reads a Windows bitmap: 1, 4, 8 (with a palette, plain or run-length packed), 16, 24, or 32 bits a pixel,
    /// stored bottom-up or top-down. Null if it isn't one.
    /// </summary>
    public static AvsBitmap? Decode(byte[] file)
    {
        try
        {
            return DecodeUnchecked(file);
        }
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentException or OverflowException or OutOfMemoryException)
        {
            return null;
        }
    }

    private static AvsBitmap? DecodeUnchecked(byte[] file)
    {
        if (file.Length < 26 || file[0] != 'B' || file[1] != 'M')
        {
            return null;
        }

        var data = file.AsSpan();
        var pixelStart = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        int width, height, bits, compression = 0, coloursUsed = 0, paletteEntry = 4;
        uint redMask = 0, greenMask = 0, blueMask = 0;
        if (headerSize == 12)
        {
            // OS/2's BITMAPCOREHEADER.
            width = BinaryPrimitives.ReadInt16LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(data[20..]);
            bits = BinaryPrimitives.ReadInt16LittleEndian(data[24..]);
            paletteEntry = 3;
        }
        else if (headerSize >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
            bits = BinaryPrimitives.ReadInt16LittleEndian(data[28..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(data[30..]);
            coloursUsed = BinaryPrimitives.ReadInt32LittleEndian(data[46..]);
            if (compression is 3 or 6)
            {
                // The colour masks follow the 40-byte header (inside it in later versions).
                var masks = data[(14 + 40)..];
                redMask = BinaryPrimitives.ReadUInt32LittleEndian(masks);
                greenMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[4..]);
                blueMask = BinaryPrimitives.ReadUInt32LittleEndian(masks[8..]);
            }
        }
        else
        {
            return null;
        }

        var topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0 || (long)width * height > 64L * 1024 * 1024)
        {
            return null;
        }

        int[]? palette = null;
        if (bits <= 8)
        {
            var count = coloursUsed > 0 && coloursUsed <= 256 ? coloursUsed : 1 << bits;
            palette = new int[256];
            var at = 14 + headerSize + (compression is 3 or 6 && headerSize == 40 ? 12 : 0);
            for (var i = 0; i < count && at + 2 < file.Length; i++, at += paletteEntry)
            {
                palette[i] = file[at] | (file[at + 1] << 8) | (file[at + 2] << 16);
            }
        }

        var pixels = new int[width * height];
        if (compression is 1 or 2 && palette is not null)
        {
            DecodeRunLength(file, pixelStart, width, height, compression == 2, palette, pixels);
        }
        else if (compression is 0 or 3 or 6)
        {
            if (!DecodePlain(file, pixelStart, width, height, bits, palette, redMask, greenMask, blueMask, pixels))
            {
                return null;
            }
        }
        else
        {
            // JPEG or PNG inside a bitmap, which LoadImage didn't take either.
            return null;
        }

        if (!topDown)
        {
            // Stored from the bottom row up.
            for (int top = 0, bottom = height - 1; top < bottom; top++, bottom--)
            {
                var upper = pixels.AsSpan(top * width, width);
                var lower = pixels.AsSpan(bottom * width, width);
                for (var x = 0; x < width; x++)
                {
                    (upper[x], lower[x]) = (lower[x], upper[x]);
                }
            }
        }

        return new AvsBitmap(width, height, pixels);
    }

    private static bool DecodePlain(byte[] file, int start, int width, int height, int bits, int[]? palette,
        uint redMask, uint greenMask, uint blueMask, int[] pixels)
    {
        if (bits is not (1 or 4 or 8 or 16 or 24 or 32))
        {
            return false;
        }

        if (redMask == 0 && greenMask == 0 && blueMask == 0)
        {
            (redMask, greenMask, blueMask) = bits == 16 ? (0x7c00u, 0x03e0u, 0x001fu) : (0xff0000u, 0x00ff00u, 0x0000ffu);
        }

        var stride = (int)(((long)width * bits + 31) / 32 * 4);
        for (var y = 0; y < height; y++)
        {
            var row = start + y * stride;
            var output = y * width;
            for (var x = 0; x < width; x++)
            {
                int colour;
                switch (bits)
                {
                    case 1:
                        colour = palette![(Byte(file, row + (x >> 3)) >> (7 - (x & 7))) & 1];
                        break;
                    case 4:
                        colour = palette![(Byte(file, row + (x >> 1)) >> ((x & 1) == 0 ? 4 : 0)) & 15];
                        break;
                    case 8:
                        colour = palette![Byte(file, row + x)];
                        break;
                    case 24:
                        var at = row + x * 3;
                        colour = Byte(file, at) | (Byte(file, at + 1) << 8) | (Byte(file, at + 2) << 16);
                        break;
                    default:
                        var size = bits / 8;
                        var p = row + x * size;
                        uint value = 0;
                        for (var i = 0; i < size; i++)
                        {
                            value |= (uint)Byte(file, p + i) << (8 * i);
                        }

                        colour = Channel(value, blueMask) | (Channel(value, greenMask) << 8) | (Channel(value, redMask) << 16);
                        break;
                }

                pixels[output + x] = colour;
            }
        }

        return true;
    }

    /// <summary>BI_RLE8 and BI_RLE4: runs of one palette colour, or of literal ones, with jumps.</summary>
    private static void DecodeRunLength(byte[] file, int start, int width, int height, bool four, int[] palette, int[] pixels)
    {
        int x = 0, y = 0, at = start;
        void Put(int index)
        {
            if (x < width && y < height)
            {
                pixels[y * width + x] = palette[index & 0xff];
            }

            x++;
        }

        while (at + 1 < file.Length && y < height)
        {
            int count = file[at], value = file[at + 1];
            at += 2;
            if (count > 0)
            {
                for (var i = 0; i < count; i++)
                {
                    Put(four ? (i & 1) == 0 ? value >> 4 : value & 15 : value);
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
                    x += Byte(file, at);
                    y += Byte(file, at + 1);
                    at += 2;
                    break;
                default:
                    for (var i = 0; i < value; i++)
                    {
                        Put(four ? (i & 1) == 0 ? Byte(file, at + i / 2) >> 4 : Byte(file, at + i / 2) & 15 : Byte(file, at + i));
                    }

                    // Literal runs are padded to a whole number of 16-bit words.
                    var bytes = four ? (value + 1) / 2 : value;
                    at += (bytes + 1) & ~1;
                    break;
            }
        }
    }

    private static int Byte(byte[] file, int at) => at >= 0 && at < file.Length ? file[at] : 0;

    /// <summary>One colour channel of a 16- or 32-bit pixel, by its mask, scaled to 0..255.</summary>
    private static int Channel(uint value, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = System.Numerics.BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        var v = (value & mask) >> shift;
        return max == 255 ? (int)v : (int)(v * 255 / max);
    }
}
