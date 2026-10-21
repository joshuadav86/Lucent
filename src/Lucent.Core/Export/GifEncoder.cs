using System.IO;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Makes an animated GIF from pictures added one at a time, written in C# so it's the same on Windows and the Mac.
/// </summary>
/// <remarks>
/// <para>
/// A GIF has at most 256 colours, so the pictures are kept (as 24-bit RGB) until <see cref="Write"/>, which picks
/// the 256 that suit the whole clip best (median cut over every picture's colours), so the colours don't flicker
/// from frame to frame. Each picture is then mapped to them, with Floyd-Steinberg dithering when asked (smoother
/// gradients, a bigger file).
/// </para>
/// <para>
/// Only the part of each frame that changed from the one before is stored, and a frame that doesn't change at all
/// just makes the one before last longer, which keeps a GIF of a video much smaller.
/// </para>
/// </remarks>
public sealed class GifEncoder
{
    private const int ColourBits = 5;
    private const int Bins = 1 << (ColourBits * 3);

    private readonly List<byte[]> _frames = new();
    private readonly List<int> _delays = new();
    private readonly long[] _histogram = new long[Bins];

    /// <param name="width">The GIF's width, in pixels: every picture added is this size.</param>
    /// <param name="height">The GIF's height.</param>
    public GifEncoder(int width, int height)
    {
        if (width is < 1 or > 65535 || height is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "A GIF is 1 to 65535 pixels each way.");
        }

        Width = width;
        Height = height;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>How many pictures have been added.</summary>
    public int Count => _frames.Count;

    /// <summary>The memory the pictures take until they're written, in bytes.</summary>
    public long Bytes => (long)_frames.Count * Width * Height * 3;

    /// <summary>
    /// Adds a 32-bit BGRA picture (top row first, alpha ignored) that shows for <paramref name="centiseconds"/>
    /// (GIF counts in hundredths of a second).
    /// </summary>
    public unsafe void Add(nint bgra, int stride, int centiseconds)
    {
        var rgb = new byte[Width * Height * 3];
        for (var y = 0; y < Height; y++)
        {
            var row = new ReadOnlySpan<byte>((byte*)bgra + (long)y * stride, Width * 4);
            var at = y * Width * 3;
            for (var x = 0; x < Width; x++)
            {
                var r = row[x * 4 + 2];
                var g = row[x * 4 + 1];
                var b = row[x * 4];
                rgb[at++] = r;
                rgb[at++] = g;
                rgb[at++] = b;
                _histogram[Bin(r, g, b)]++;
            }
        }

        _frames.Add(rgb);
        _delays.Add(Math.Max(2, centiseconds));
    }

    /// <summary>Writes the GIF: once round, or looping forever.</summary>
    public void Write(Stream target, bool loop, bool dither, IProgress<double>? progress = null, CancellationToken cancellation = default)
    {
        if (_frames.Count == 0)
        {
            throw new InvalidOperationException("A GIF needs at least one picture.");
        }

        var palette = MedianCut(_histogram, 256);
        var nearest = NearestForEachBin(palette);
        using var output = new BinaryWriter(target, System.Text.Encoding.ASCII, leaveOpen: true);
        output.Write("GIF89a"u8);
        output.Write((ushort)Width);
        output.Write((ushort)Height);
        // A global colour table of 256 colours.
        output.Write((byte)0xF7);
        output.Write((byte)0);
        output.Write((byte)0);
        for (var i = 0; i < 256; i++)
        {
            (int R, int G, int B) colour = i < palette.Count ? palette[i] : (0, 0, 0);
            output.Write((byte)colour.R);
            output.Write((byte)colour.G);
            output.Write((byte)colour.B);
        }

        if (loop)
        {
            // NETSCAPE2.0: play forever.
            output.Write(new byte[] { 0x21, 0xFF, 0x0B });
            output.Write("NETSCAPE2.0"u8);
            output.Write(new byte[] { 0x03, 0x01, 0x00, 0x00, 0x00 });
        }

        byte[]? shown = null;
        (int Left, int Top, int Width, int Height, byte[] Pixels)? pending = null;
        var pendingDelay = 0;
        for (var f = 0; f < _frames.Count; f++)
        {
            cancellation.ThrowIfCancellationRequested();
            var indexed = Map(_frames[f], palette, nearest, dither);
            var changed = shown is null ? (0, 0, Width, Height) : ChangedArea(shown, indexed);
            if (changed is null)
            {
                // Nothing changed: the frame before just shows longer.
                pendingDelay += _delays[f];
            }
            else
            {
                if (pending is { } before)
                {
                    WriteFrame(output, before, pendingDelay);
                }

                var (left, top, w, h) = changed.Value;
                pending = (left, top, w, h, Crop(indexed, left, top, w, h));
                pendingDelay = _delays[f];
                shown = indexed;
            }

            progress?.Report((f + 1.0) / _frames.Count);
        }

        if (pending is { } last)
        {
            WriteFrame(output, last, pendingDelay);
        }

        output.Write((byte)0x3B);
    }

    private void WriteFrame(BinaryWriter output, (int Left, int Top, int Width, int Height, byte[] Pixels) frame, int delay)
    {
        // Graphic control: leave the frame in place (the next draws over it), and how long it shows.
        output.Write(new byte[] { 0x21, 0xF9, 0x04, 0x04 });
        output.Write((ushort)Math.Min(65535, delay));
        output.Write((byte)0);
        output.Write((byte)0);
        output.Write((byte)0x2C);
        output.Write((ushort)frame.Left);
        output.Write((ushort)frame.Top);
        output.Write((ushort)frame.Width);
        output.Write((ushort)frame.Height);
        output.Write((byte)0);
        output.Write((byte)8);
        var data = Lzw(frame.Pixels);
        for (var at = 0; at < data.Length; at += 255)
        {
            var length = Math.Min(255, data.Length - at);
            output.Write((byte)length);
            output.Write(data, at, length);
        }

        output.Write((byte)0);
    }

    private static int Bin(int r, int g, int b) =>
        ((r >> (8 - ColourBits)) << (ColourBits * 2)) | ((g >> (8 - ColourBits)) << ColourBits) | (b >> (8 - ColourBits));

    private static (int R, int G, int B) BinColour(int bin)
    {
        const int mask = (1 << ColourBits) - 1;
        const int shift = 8 - ColourBits;
        var half = 1 << (shift - 1);
        return ((((bin >> (ColourBits * 2)) & mask) << shift) + half, (((bin >> ColourBits) & mask) << shift) + half, ((bin & mask) << shift) + half);
    }

    // Median cut: the colour space is split, box by box, through the middle of the pixels in the box with the widest
    // spread, until there are enough boxes; each box's colour is its pixels' average.
    private static List<(int R, int G, int B)> MedianCut(long[] histogram, int colours)
    {
        var used = Enumerable.Range(0, Bins).Where(bin => histogram[bin] > 0).ToArray();
        var boxes = new List<int[]> { used };
        while (boxes.Count < colours)
        {
            var best = -1;
            var bestScore = 0L;
            var bestChannel = 0;
            for (var i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Length < 2)
                {
                    continue;
                }

                var (channel, range) = WidestChannel(boxes[i]);
                // Big, spread-out boxes are split first.
                var weight = boxes[i].Sum(bin => histogram[bin]);
                var score = range * (long)Math.Sqrt(weight);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                    bestChannel = channel;
                }
            }

            if (best < 0)
            {
                break;
            }

            var box = boxes[best].OrderBy(bin => Channel(bin, bestChannel)).ToArray();
            var total = box.Sum(bin => histogram[bin]);
            var half = 0L;
            var split = 1;
            for (var i = 0; i < box.Length - 1; i++)
            {
                half += histogram[box[i]];
                split = i + 1;
                if (half * 2 >= total)
                {
                    break;
                }
            }

            boxes[best] = box[..split];
            boxes.Add(box[split..]);
        }

        var palette = new List<(int R, int G, int B)>();
        foreach (var box in boxes.Where(box => box.Length > 0))
        {
            double r = 0, g = 0, b = 0, n = 0;
            foreach (var bin in box)
            {
                var (cr, cg, cb) = BinColour(bin);
                var count = histogram[bin];
                r += cr * count;
                g += cg * count;
                b += cb * count;
                n += count;
            }

            palette.Add(((int)Math.Round(r / n), (int)Math.Round(g / n), (int)Math.Round(b / n)));
        }

        if (palette.Count == 0)
        {
            palette.Add((0, 0, 0));
        }

        return palette;
    }

    private static int Channel(int bin, int channel) => (bin >> (ColourBits * (2 - channel))) & ((1 << ColourBits) - 1);

    private static (int Channel, int Range) WidestChannel(int[] box)
    {
        var best = (Channel: 0, Range: -1);
        for (var channel = 0; channel < 3; channel++)
        {
            var min = int.MaxValue;
            var max = int.MinValue;
            foreach (var bin in box)
            {
                var value = Channel(bin, channel);
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            // Green counts most to the eye, then red, then blue.
            var range = (max - min) * (channel switch { 0 => 3, 1 => 4, _ => 2 });
            if (range > best.Range)
            {
                best = (channel, range);
            }
        }

        return best;
    }

    private static byte[] NearestForEachBin(List<(int R, int G, int B)> palette)
    {
        var nearest = new byte[Bins];
        for (var bin = 0; bin < Bins; bin++)
        {
            var (r, g, b) = BinColour(bin);
            nearest[bin] = Nearest(palette, r, g, b);
        }

        return nearest;
    }

    private static byte Nearest(List<(int R, int G, int B)> palette, int r, int g, int b)
    {
        var best = 0;
        var bestDistance = int.MaxValue;
        for (var i = 0; i < palette.Count; i++)
        {
            var (pr, pg, pb) = palette[i];
            var distance = 3 * (pr - r) * (pr - r) + 4 * (pg - g) * (pg - g) + 2 * (pb - b) * (pb - b);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return (byte)best;
    }

    private byte[] Map(byte[] rgb, List<(int R, int G, int B)> palette, byte[] nearest, bool dither)
    {
        var indexed = new byte[Width * Height];
        if (!dither)
        {
            for (var i = 0; i < indexed.Length; i++)
            {
                indexed[i] = nearest[Bin(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2])];
            }

            return indexed;
        }

        // Floyd-Steinberg: each pixel's error goes on to the ones right and below it.
        var errors = new int[2, (Width + 2) * 3];
        for (var y = 0; y < Height; y++)
        {
            var row = y & 1;
            var next = row ^ 1;
            for (var i = 0; i < (Width + 2) * 3; i++)
            {
                errors[next, i] = 0;
            }

            for (var x = 0; x < Width; x++)
            {
                var at = (y * Width + x) * 3;
                var e = (x + 1) * 3;
                var r = Math.Clamp(rgb[at] + errors[row, e] / 16, 0, 255);
                var g = Math.Clamp(rgb[at + 1] + errors[row, e + 1] / 16, 0, 255);
                var b = Math.Clamp(rgb[at + 2] + errors[row, e + 2] / 16, 0, 255);
                var index = nearest[Bin(r, g, b)];
                indexed[y * Width + x] = index;
                var (pr, pg, pb) = palette[index];
                Spread(errors, row, next, e, r - pr, 0);
                Spread(errors, row, next, e, g - pg, 1);
                Spread(errors, row, next, e, b - pb, 2);
            }
        }

        return indexed;
    }

    private static void Spread(int[,] errors, int row, int next, int e, int error, int channel)
    {
        errors[row, e + 3 + channel] += error * 7;
        errors[next, e - 3 + channel] += error * 3;
        errors[next, e + channel] += error * 5;
        errors[next, e + 3 + channel] += error;
    }

    private (int Left, int Top, int Width, int Height)? ChangedArea(byte[] before, byte[] now)
    {
        int left = Width, right = -1, top = Height, bottom = -1;
        for (var y = 0; y < Height; y++)
        {
            var row = y * Width;
            for (var x = 0; x < Width; x++)
            {
                if (before[row + x] != now[row + x])
                {
                    left = Math.Min(left, x);
                    right = Math.Max(right, x);
                    top = Math.Min(top, y);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < 0 ? null : (left, top, right - left + 1, bottom - top + 1);
    }

    private byte[] Crop(byte[] indexed, int left, int top, int width, int height)
    {
        if (left == 0 && top == 0 && width == Width && height == Height)
        {
            return indexed;
        }

        var cropped = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(indexed, (top + y) * Width + left, cropped, y * width, width);
        }

        return cropped;
    }

    // GIF's LZW, for 8-bit pixels: codes start at 9 bits and grow to 12, then the table starts again.
    private static byte[] Lzw(byte[] pixels)
    {
        const int clear = 256;
        const int end = 257;
        var output = new MemoryStream();
        var bits = 0;
        var buffered = 0;
        var size = 9;
        var nextCode = 258;
        var table = new Dictionary<int, int>(4096);

        void Emit(int code)
        {
            buffered |= code << bits;
            bits += size;
            while (bits >= 8)
            {
                output.WriteByte((byte)buffered);
                buffered >>= 8;
                bits -= 8;
            }
        }

        Emit(clear);
        var prefix = (int)pixels[0];
        for (var i = 1; i < pixels.Length; i++)
        {
            var pixel = pixels[i];
            var key = (prefix << 8) | pixel;
            if (table.TryGetValue(key, out var code))
            {
                prefix = code;
                continue;
            }

            Emit(prefix);
            if (nextCode < 4096)
            {
                table[key] = nextCode++;
                if (nextCode > (1 << size) && size < 12)
                {
                    size++;
                }
            }
            else
            {
                Emit(clear);
                table.Clear();
                nextCode = 258;
                size = 9;
            }

            prefix = pixel;
        }

        Emit(prefix);
        Emit(end);
        if (bits > 0)
        {
            output.WriteByte((byte)buffered);
        }

        return output.ToArray();
    }
}
