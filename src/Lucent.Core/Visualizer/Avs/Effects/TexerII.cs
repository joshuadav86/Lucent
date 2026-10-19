using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Render / Texer II (the "Acko.net: Texer II" plug-in, e_texer2.cpp in the later open-source AVS): code places n copies of a
/// small picture (particles), each optionally resized, mirrored, and tinted, put on with the line blend mode.
/// </summary>
[AvsEffect("Acko.net: Texer II")]
public sealed class TexerII : AvsEffect
{
    private const int NameLength = 260;
    private const int OldVersion = 0;
    private const int CurrentVersion = 1;

    // The plug-in's own picture: a soft white dot, 21 pixels across (greys, 0..255).
    private static readonly byte[] DefaultImage =
    {
        0, 0, 0, 0, 0, 0, 3, 8, 13, 16, 18, 16, 13, 8, 3, 1, 0, 0, 0, 0, 0,
        0, 0, 0, 0, 3, 11, 22, 33, 42, 47, 50, 47, 42, 33, 22, 11, 3, 0, 0, 0, 0,
        0, 0, 0, 5, 18, 34, 50, 65, 77, 84, 87, 84, 77, 65, 50, 34, 18, 5, 0, 0, 0,
        0, 0, 5, 20, 40, 62, 83, 101, 116, 125, 128, 125, 116, 102, 83, 62, 40, 20, 5, 0, 0,
        0, 3, 17, 40, 66, 93, 119, 141, 156, 165, 168, 165, 156, 141, 119, 93, 66, 40, 17, 3, 0,
        0, 10, 33, 62, 93, 125, 154, 175, 192, 202, 205, 202, 192, 175, 154, 125, 93, 62, 33, 10, 0,
        3, 21, 49, 83, 118, 153, 182, 206, 222, 232, 235, 232, 222, 205, 182, 153, 118, 83, 49, 21, 3,
        7, 31, 64, 101, 140, 175, 205, 229, 242, 248, 250, 248, 242, 229, 205, 175, 140, 101, 64, 31, 7,
        12, 40, 76, 115, 155, 191, 221, 242, 250, 253, 254, 253, 250, 242, 221, 191, 155, 115, 76, 40, 11,
        15, 46, 83, 124, 164, 200, 231, 248, 253, 255, 255, 255, 253, 248, 231, 200, 164, 124, 83, 46, 15,
        16, 48, 85, 126, 166, 203, 233, 249, 254, 255, 255, 255, 254, 249, 233, 203, 166, 126, 85, 48, 16,
        15, 45, 82, 123, 163, 199, 230, 247, 253, 254, 255, 254, 253, 247, 230, 199, 163, 123, 82, 45, 15,
        11, 40, 74, 113, 154, 189, 220, 241, 250, 253, 254, 253, 250, 241, 220, 189, 154, 113, 74, 40, 11,
        6, 31, 63, 99, 138, 173, 203, 227, 241, 247, 249, 247, 241, 227, 203, 173, 138, 99, 63, 31, 6,
        3, 20, 48, 80, 116, 150, 179, 202, 219, 229, 232, 229, 219, 203, 179, 150, 116, 80, 48, 20, 2,
        0, 9, 32, 60, 90, 122, 150, 172, 188, 198, 201, 198, 188, 172, 150, 122, 90, 60, 32, 9, 0,
        0, 2, 16, 38, 63, 90, 115, 137, 153, 161, 164, 161, 153, 137, 115, 90, 64, 38, 16, 2, 0,
        0, 0, 4, 18, 38, 59, 80, 98, 112, 121, 124, 121, 112, 98, 80, 59, 38, 18, 4, 0, 0,
        0, 0, 0, 4, 15, 31, 47, 62, 73, 80, 83, 80, 73, 62, 47, 31, 15, 4, 0, 0, 0,
        0, 0, 0, 0, 2, 9, 19, 30, 38, 44, 46, 44, 38, 29, 19, 9, 2, 0, 0, 0, 0,
        0, 0, 0, 0, 0, 0, 2, 6, 10, 14, 15, 14, 10, 6, 3, 0, 0, 0, 0, 0, 0,
    };

    private readonly string[] _code = { string.Empty, string.Empty, string.Empty, string.Empty };
    private int _version = CurrentVersion;
    private bool _resize;
    private bool _wrap;
    private bool _colorize = true;
    private Sprite _sprite = new(Array.Empty<int[]>(), 0, 0);
    private Sprite? _scaled;
    private float _scaledFor;
    private Compiled? _compiled;

    public TexerII(AvsShared shared)
        : base(shared)
    {
        // Until a preset says otherwise: the plug-in's first example, a coloured oscilloscope.
        _code[0] = "n=300;";
        _code[3] = "x=(i*2-1)*2;y=v;\r\nred=1-y*2;green=abs(y)*2;blue=y*2-1;";
        _colorize = true;
        LoadDefaultImage();
    }

    public override void Load(AvsConfigReader config)
    {
        _version = config.ReadInt(CurrentVersion);
        if (_version is < OldVersion or > CurrentVersion)
        {
            _version = OldVersion;
        }

        var name = config.Slice(config.Position, NameLength).ReadTerminatedString();
        config.Position = Math.Min(config.Length, 4 + NameLength);
        _resize = config.ReadInt() != 0;
        _wrap = config.ReadInt() != 0;
        _colorize = config.ReadInt() != 0;
        config.ReadInt();
        for (var i = 0; i < 4; i++)
        {
            _code[i] = config.ReadString();
        }

        _compiled = null;
        if (name.Length == 0)
        {
            LoadDefaultImage();
            return;
        }

        var image = AvsBitmap.Load(Shared.Folder, name, out var problem);
        if (image is null)
        {
            Warn($"{problem?.Replace(", so it's left out.", string.Empty)}; Texer II shows its own dot instead.");
            LoadDefaultImage();
            return;
        }

        SetImage(image.Pixels, image.Width, image.Height);
    }

    public override void Restart()
    {
        if (_compiled is not null)
        {
            _compiled.Initialised = false;
        }
    }

    private void LoadDefaultImage()
    {
        var pixels = new int[DefaultImage.Length];
        for (var i = 0; i < pixels.Length; i++)
        {
            int c = DefaultImage[i];
            pixels[i] = c | (c << 8) | (c << 16);
        }

        SetImage(pixels, 21, 21);
    }

    private void SetImage(int[] normal, int width, int height)
    {
        var mirrored = new int[normal.Length];
        var flipped = new int[normal.Length];
        var turned = new int[normal.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = normal[y * width + x];
                flipped[(height - y - 1) * width + x] = value;
                mirrored[(y + 1) * width - x - 1] = value;
                turned[(height - y) * width - x - 1] = value;
            }
        }

        _sprite = new Sprite(new[] { normal, mirrored, flipped, turned }, width, height);
        _scaled = null;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var code = _compiled ??= new Compiled(this);
        int w = context.Width, h = context.Height;
        var v = code.Values;
        v[code.I] = 0.0;
        v[code.X] = 0.0;
        v[code.Y] = 0.0;
        v[code.V] = 0.0;
        v[code.W] = w;
        v[code.H] = h;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;
        v[code.Iw] = _sprite.Width;
        v[code.Ih] = _sprite.Height;
        v[code.SizeX] = 1.0;
        v[code.SizeY] = 1.0;
        v[code.Red] = 1.0;
        v[code.Green] = 1.0;
        v[code.Blue] = 1.0;
        v[code.Skip] = 0.0;
        if (!code.Initialised)
        {
            v[code.N] = 0.0;
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        var n = Math.Clamp(RoundToInt(code.Values[code.N]), 0, 65536);
        if (n <= 0)
        {
            return 0;
        }

        // Lucent draws bigger than AVS did, so particles grow by the dot scale: resized ones through their size,
        // the others drawn from a copy of the picture made that much bigger.
        var scale = context.DotScale;
        var sprite = _sprite;
        if (!_resize && scale != 1f)
        {
            if (_scaled is null || _scaledFor != scale)
            {
                _scaled = sprite.Scaled(scale);
                _scaledFor = scale;
            }

            sprite = _scaled;
        }

        var blend = Shared.LineBlendMode;
        var audio = context.Audio;
        var step = 1.0 / (n - 1);
        var i = 0.0;
        for (var j = 0; j < n; j++)
        {
            v = code.Values;
            v[code.I] = i;
            v[code.Skip] = 0.0;
            var sample = j * 575 / n;
            v[code.V] = (audio.Waveform(0, sample) + audio.Waveform(1, sample)) / 256.0;
            i += step;
            code.Point.Run();
            v = code.Values;
            if (v[code.Skip] != 0.0)
            {
                continue;
            }

            var sizeX = v[code.SizeX];
            var sizeY = v[code.SizeY];
            var szx = Math.Abs(sizeX);
            var szy = Math.Abs(sizeY);
            if (szx <= .01 || szy <= .01)
            {
                continue;
            }

            // Without resizing the sizes only mirror and flip (and say how far a wrapped particle reaches).
            if (_resize)
            {
                szx *= scale;
                szy *= scale;
            }

            var colour = 0xffffff;
            if (_colorize)
            {
                colour = Math.Clamp(RoundToInt(255.0f * v[code.Blue]), 0, 255)
                    | (Math.Clamp(RoundToInt(255.0f * v[code.Green]), 0, 255) << 8)
                    | (Math.Clamp(RoundToInt(255.0f * v[code.Red]), 0, 255) << 16);
            }

            var texture = sprite.Variants[sizeX < 0 && sizeY > 0 ? 1 : sizeX > 0 && sizeY < 0 ? 2 : sizeX < 0 && sizeY < 0 ? 3 : 0];
            var x = v[code.X];
            var y = v[code.Y];
            if (_wrap)
            {
                bool overlapsX, overlapsY;
                if (_version == OldVersion)
                {
                    // The 2.81d plug-in wrapped once only (past 1, not past 3 and so on).
                    x -= x > 1.0 ? 2.0 : x < -1.0 ? -2.0 : 0.0;
                    y -= y > 1.0 ? 2.0 : y < -1.0 ? -2.0 : 0.0;
                    overlapsX = OverlapsEdge(v[code.X], szx, sprite.Width, w);
                    overlapsY = OverlapsEdge(v[code.Y], szy, sprite.Height, h);
                }
                else
                {
                    x -= Math.Round(x / 2.0, MidpointRounding.AwayFromZero) * 2.0;
                    y -= Math.Round(y / 2.0, MidpointRounding.AwayFromZero) * 2.0;
                    overlapsX = OverlapsEdge(x, szx, sprite.Width, w);
                    overlapsY = OverlapsEdge(y, szy, sprite.Height, h);
                }

                var signX2 = x > 0 ? 2.0 : -2.0;
                var signY2 = y > 0 ? 2.0 : -2.0;
                if (overlapsX)
                {
                    DrawParticle(framebuffer, texture, sprite.Width, sprite.Height, w, h, x - signX2, y, szx, szy, colour, blend);
                }

                if (overlapsY)
                {
                    DrawParticle(framebuffer, texture, sprite.Width, sprite.Height, w, h, x, y - signY2, szx, szy, colour, blend);
                }

                if (overlapsX && overlapsY)
                {
                    DrawParticle(framebuffer, texture, sprite.Width, sprite.Height, w, h, x - signX2, y - signY2, szx, szy, colour, blend);
                }
            }

            DrawParticle(framebuffer, texture, sprite.Width, sprite.Height, w, h, x, y, szx, szy, colour, blend);
        }

        return 0;
    }

    private static bool OverlapsEdge(double wrapped, double size, int imageSize, int screenSize)
    {
        var distance = Math.Abs(wrapped);
        var halfSize = size * (imageSize - 1) / screenSize;
        return distance + halfSize > 1.0 && distance - halfSize < 1.0;
    }

    /// <summary>cvtsd2si: to the nearest whole number (halves to even), or int.MinValue when it doesn't fit, as x86 gave.</summary>
    private static int RoundToInt(double value)
    {
        var rounded = Math.Round(value, MidpointRounding.ToEven);
        return rounded is >= int.MinValue and <= int.MaxValue ? (int)rounded : int.MinValue;
    }

    private static double Fractional(double value) => value - (int)Math.Clamp(value, int.MinValue, int.MaxValue);

    /// <summary>
    /// Draws one particle centred on (x, y) (-1..1 across the picture): at its own size pixel for pixel, or, resizing,
    /// <paramref name="sizeX"/> by <paramref name="sizeY"/> times as big with bilinear filtering. Each texel is
    /// tinted by <paramref name="colour"/> (over 256) and put on with the line blend mode.
    /// </summary>
    private void DrawParticle(int[] framebuffer, int[] texture, int textureWidth, int textureHeight, int width, int height,
        double x, double y, double sizeX, double sizeY, int colour, int blend)
    {
        // As the plug-in, sizes from here on count from 0 (one less than the size).
        var w = width - 1;
        var h = height - 1;
        var stride = textureWidth;
        var iw = textureWidth - 1;
        var ih = textureHeight - 1;
        int cr = (colour >> 16) & 0xff, cg = (colour >> 8) & 0xff, cb = colour & 0xff;
        var mode = blend & 0xff;
        var amount = (blend >> 8) & 0xff;
        double x0 = 0.0, y0 = 0.0;
        if (_resize)
        {
            var left = -iw * .5 * sizeX + 0.5 + (x * .5 + .5) * w;
            var top = -ih * .5 * sizeY + 0.5 + (y * .5 + .5) * h;
            var right = (iw - 1) * .5 * sizeX + 0.5 + (x * .5 + .5) * w;
            var bottom = (ih - 1) * .5 * sizeY + 0.5 + (y * .5 + .5) * h;
            if (!double.IsFinite(left + top + right + bottom) || right == left || bottom == top)
            {
                return;
            }

            int l2 = RoundToInt(left), t2 = RoundToInt(top), r2 = RoundToInt(right), b2 = RoundToInt(bottom);
            if (r2 < 0 || l2 > w || b2 < 0 || t2 > h)
            {
                return;
            }

            // Where the first pixel falls on the picture, to the sub-pixel.
            x0 = (0.5 - Fractional(left + 0.5)) / (right - left);
            y0 = (0.5 - Fractional(top + 0.5)) / (bottom - top);
            if (left < 0.0)
            {
                x0 = -left / (right - left);
                l2 = 0;
            }

            if (top < 0.0)
            {
                y0 = -top / (bottom - top);
                t2 = 0;
            }

            if (right > w)
            {
                r2 = w;
            }

            if (bottom > h)
            {
                b2 = h;
            }

            var fx0 = x0 * iw;
            var fy0 = y0 * ih;
            var cx0 = RoundToInt(fx0);
            var cy0 = RoundToInt(fy0);
            var dx = 65535 - (int)((.5f - (fx0 - cx0)) * 65536.0);
            var dy = 65535 - (int)((.5f - (fy0 - cy0)) * 65536.0);

            // Texels a pixel, in 16.16 fixed point.
            var sdx = (int)((iw - 1) / (right - left + 1) * 65536.0);
            var sdy = (int)((ih - 1) / (bottom - top + 1) * 65536.0);
            cx0 = (cx0 << 16) + dx;
            cy0 = (cy0 << 16) + dy;
            if (cx0 < 0)
            {
                cx0 += sdx;
                ++l2;
            }

            if (cy0 < 0)
            {
                cy0 += sdy;
                ++t2;
            }

            if (r2 <= l2 || b2 <= t2)
            {
                return;
            }

            var lastColumn = textureWidth - 1;
            var lastRow = textureHeight - 1;
            for (var py = t2; py <= b2; py++, cy0 += sdy)
            {
                var fy = (cy0 >> 8) & 0xff;
                var row0 = Math.Min((int)((uint)cy0 >> 16), lastRow);
                var row1 = Math.Min(row0 + 1, lastRow) * stride;
                row0 *= stride;
                var at = py * width + l2;
                var cx = cx0;
                for (var px = l2; px <= r2; px++, at++, cx += sdx)
                {
                    var fx = (cx >> 8) & 0xff;
                    var column0 = Math.Min((int)((uint)cx >> 16), lastColumn);
                    var column1 = Math.Min(column0 + 1, lastColumn);
                    int a = texture[row0 + column0], b = texture[row0 + column1], c = texture[row1 + column0], d = texture[row1 + column1];
                    var sb = Filter(a, b, c, d, 0, fx, fy) * cb >> 8;
                    var sg = Filter(a, b, c, d, 8, fx, fy) * cg >> 8;
                    var sr = Filter(a, b, c, d, 16, fx, fy) * cr >> 8;
                    framebuffer[at] = Blend(framebuffer[at], sr, sg, sb, mode, amount);
                }
            }

            return;
        }

        // The picture at its own size.
        var rl = RoundToInt((x * .5f + .5f) * w) - iw / 2;
        var rt = RoundToInt((y * .5f + .5f) * h) - ih / 2;
        if (rl == int.MinValue || rt == int.MinValue)
        {
            return;
        }

        var rr = rl + iw - 1;
        var rb = rt + ih - 1;
        int cl = rl, ct = rt, cr2 = rr, cb2 = rb;
        if (cr2 < 0 || cl > w || cb2 < 0 || ct > h)
        {
            return;
        }

        if (rl < 0)
        {
            x0 = rr != rl ? -(double)rl / (rr - rl) : 0.0;
            cl = 0;
        }

        if (rt < 0)
        {
            y0 = rb != rt ? -(double)rt / (rb - rt) : 0.0;
            ct = 0;
        }

        if (rr > w)
        {
            cr2 = w;
        }

        if (rb > h)
        {
            cb2 = h;
        }

        if (cr2 <= cl || cb2 <= ct)
        {
            return;
        }

        var tx0 = RoundToInt(x0 * iw);
        var ty = RoundToInt(y0 * ih);
        for (var py = ct; py <= cb2; py++, ty++)
        {
            var row = Math.Min(ty, textureHeight - 1) * stride;
            var at = py * width + cl;
            for (var px = cl; px <= cr2; px++, at++)
            {
                var t = texture[row + Math.Min(tx0 + px - cl, textureWidth - 1)];
                int sb = t & 0xff, sg = (t >> 8) & 0xff, sr = (t >> 16) & 0xff;
                if (_colorize)
                {
                    sb = sb * cb >> 8;
                    sg = sg * cg >> 8;
                    sr = sr * cr >> 8;
                }

                framebuffer[at] = Blend(framebuffer[at], sr, sg, sb, mode, amount);
            }
        }
    }

    /// <summary>One channel of the bilinear mix of four texels, in the plug-in's 8-bit steps.</summary>
    private static int Filter(int a, int b, int c, int d, int shift, int fx, int fy)
    {
        int ca = (a >> shift) & 0xff, cb = (b >> shift) & 0xff, cc = (c >> shift) & 0xff, cd = (d >> shift) & 0xff;
        var top = (cb * fx >> 8) + (ca * (255 - fx) >> 8);
        var bottom = (cc * (255 - fx) >> 8) + (cd * fx >> 8);
        return (bottom * fy >> 8) + (top * (255 - fy) >> 8);
    }

    /// <summary>Puts a particle's pixel (channels 0..255) onto <paramref name="under"/> with the line blend mode, as the plug-in's MMX did.</summary>
    private static int Blend(int under, int r, int g, int b, int mode, int amount)
    {
        int ur = (under >> 16) & 0xff, ug = (under >> 8) & 0xff, ub = under & 0xff;
        switch (mode)
        {
            case 1:
                r = Math.Min(r + ur, 255);
                g = Math.Min(g + ug, 255);
                b = Math.Min(b + ub, 255);
                break;
            case 2:
                r = Math.Max(r, ur);
                g = Math.Max(g, ug);
                b = Math.Max(b, ub);
                break;
            case 3:
                r = (r + ur) >> 1;
                g = (g + ug) >> 1;
                b = (b + ub) >> 1;
                break;
            case 4:
                r = Math.Max(ur - r, 0);
                g = Math.Max(ug - g, 0);
                b = Math.Max(ub - b, 0);
                break;
            case 5:
                r = Math.Max(r - ur, 0);
                g = Math.Max(g - ug, 0);
                b = Math.Max(b - ub, 0);
                break;
            case 6:
                r = r * ur >> 8;
                g = g * ug >> 8;
                b = b * ub >> 8;
                break;
            case 7:
                r = Math.Min((ur * (255 - amount) >> 8) + (r * amount >> 8), 255);
                g = Math.Min((ug * (255 - amount) >> 8) + (g * amount >> 8), 255);
                b = Math.Min((ub * (255 - amount) >> 8) + (b * amount >> 8), 255);
                break;
            case 8:
                r ^= ur;
                g ^= ug;
                b ^= ub;
                break;
            case 9:
                r = Math.Min(r, ur);
                g = Math.Min(g, ug);
                b = Math.Min(b, ub);
                break;
        }

        return b | (g << 8) | (r << 16);
    }

    /// <summary>The picture, and it mirrored, flipped, and both (for negative sizes), all the same size.</summary>
    private sealed record Sprite(int[][] Variants, int Width, int Height)
    {
        /// <summary>All four made <paramref name="scale"/> times bigger, smoothly.</summary>
        public Sprite Scaled(float scale)
        {
            var width = Math.Max(1, (int)MathF.Round(Width * scale));
            var height = Math.Max(1, (int)MathF.Round(Height * scale));
            var variants = Variants.Select(pixels => TexerSprites.ScaleSmooth(pixels, Width, Height, width, height)).ToArray();
            return new Sprite(variants, width, height);
        }
    }

    /// <summary>The four pieces of code, compiled the first time the effect draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(TexerII texer)
        {
            _context = texer.NewContext();
            N = _context.Index("n");
            I = _context.Index("i");
            X = _context.Index("x");
            Y = _context.Index("y");
            V = _context.Index("v");
            B = _context.Index("b");
            W = _context.Index("w");
            H = _context.Index("h");
            Iw = _context.Index("iw");
            Ih = _context.Index("ih");
            SizeX = _context.Index("sizex");
            SizeY = _context.Index("sizey");
            Red = _context.Index("red");
            Green = _context.Index("green");
            Blue = _context.Index("blue");
            Skip = _context.Index("skip");
            Init = texer.Compile(_context, texer._code[0], "Texer II's init code");
            Frame = texer.Compile(_context, texer._code[1], "Texer II's per frame code");
            Beat = texer.Compile(_context, texer._code[2], "Texer II's on beat code");
            Point = texer.Compile(_context, texer._code[3], "Texer II's per particle code");
        }

        public int N { get; }

        public int I { get; }

        public int X { get; }

        public int Y { get; }

        public int V { get; }

        public int B { get; }

        public int W { get; }

        public int H { get; }

        public int Iw { get; }

        public int Ih { get; }

        public int SizeX { get; }

        public int SizeY { get; }

        public int Red { get; }

        public int Green { get; }

        public int Blue { get; }

        public int Skip { get; }

        public EelCode Init { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Point { get; }

        public bool Initialised { get; set; }

        public double[] Values => _context.Values;
    }
}
