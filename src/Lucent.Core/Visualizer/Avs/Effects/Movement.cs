namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Movement (r_trans.cpp): moves every pixel to a new place (a swirl, a tunnel, a zoom, or what
/// the preset's own formula says), worked out once for the picture's size and looked up each frame.
/// </summary>
[AvsEffect(15)]
public sealed class Movement : AvsEffect
{
    /// <summary>The effect number AVS saves for "user defined" (code of the preset's own).</summary>
    private const int Custom = 32767;

    private const int FirstRadial = 3;
    private const int LastEffect = 23;

    // The built-in movements AVS ran through its expression code (18 to 23), and whether each uses x and y rather than d and r.
    private static readonly string[] BuiltInCode =
    {
        "d = d * (1 - (sin((r-$PI*0.5) * 7) * .03));\r\nr = r + (cos(d * 12) * .03);",
        "d = d * (1 - (sin((r - $PI*0.5) * 12) * .05));\r\nr = r + (cos(d * 18) * .05);\r\nd = d * (1-((d - .4) * .03));\r\nr = r + ((d - .4) * .13)",
        "x = x + (cos(y * 18) * .02);\r\ny = y + (sin(x * 14) * .03);",
        "x = x + (cos(abs(y-.5) * 8) * .02);\r\ny = y + (sin(abs(x-.5) * 8) * .05);\r\nx = x * .95;\r\ny = y * .95;",
        "y = y * ( 1 + (sin(r + $PI/2) * .3) );\r\nx = x * ( 1 + (cos(r + $PI/2) * .3) );\r\nx = x * .995;\r\ny = y * .995;",
        "y = (r*6)/($PI); x = d;",
    };

    private static readonly bool[] BuiltInRectangular = { false, false, true, true, true, true };

    private int _effect = 1;
    private int _blend;
    private int _sourceMapped;
    private int _loadedSourceMapped;
    private int _rectangular;
    private int _subpixel = 1;
    private int _wrap;
    private string _expression = string.Empty;

    // The lookup table: where each pixel comes from and, when filtering, how far across and down (0..248) between pixels.
    private int[] _offsets = Array.Empty<int>();
    private byte[] _xFraction = Array.Empty<byte>();
    private byte[] _yFraction = Array.Empty<byte>();
    private int _tableWidth;
    private int _tableHeight;
    private bool _tableSubpixel;

    public Movement(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _effect = config.ReadInt(_effect);
        if (_effect == Custom)
        {
            var at = config.Position;
            if (config[at] == '!' && config[at + 1] == 'r' && config[at + 2] == 'e' && config[at + 3] == 'c' && config[at + 4] == 't' && config[at + 5] == ' ')
            {
                config.Position += 6;
                _rectangular = 1;
            }

            if (config[config.Position] == 1)
            {
                config.Position++;
                _expression = config.ReadString();
            }
            else if (config.Remaining >= 256)
            {
                _expression = config.ReadFixedString(256 - (_rectangular != 0 ? 6 : 0));
            }
        }

        _blend = config.ReadInt(_blend);
        _sourceMapped = config.ReadInt(_sourceMapped);
        _rectangular = config.ReadInt(_rectangular);
        _subpixel = config.ReadInt(0);
        _wrap = config.ReadInt(0);

        // Effects past 15 are saved as 0 up front (old versions of AVS choked on them) and for real at the end.
        if (_effect == 0)
        {
            _effect = config.ReadInt(0);
        }

        if ((_effect != Custom && _effect > LastEffect) || _effect < 0)
        {
            _effect = 0;
        }

        _loadedSourceMapped = _sourceMapped;
        _tableWidth = 0;
    }

    public override void Restart()
    {
        _sourceMapped = _loadedSourceMapped;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_effect == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_tableWidth != w || _tableHeight != h || _offsets.Length != w * h)
        {
            BuildTable(context, w, h);
        }

        if ((_sourceMapped & 2) != 0 && context.IsBeat)
        {
            _sourceMapped ^= 1;
        }

        var offsets = _offsets;
        var xFraction = _xFraction;
        var yFraction = _yFraction;
        var blend = _blend != 0;
        if ((_sourceMapped & 1) != 0)
        {
            // Source mapping pushes each pixel to where the table says, keeping the brighter of what lands there.
            if (blend)
            {
                Array.Copy(framebuffer, fbout, w * h);
            }
            else
            {
                Array.Clear(fbout, 0, w * h);
            }

            for (var i = 0; i < w * h; i++)
            {
                var to = offsets[i];
                fbout[to] = AvsBlend.Max(framebuffer[i], fbout[to]);
            }

            if (blend)
            {
                context.ForEachRow(y =>
                {
                    for (int at = y * w, end = at + w; at < end; at++)
                    {
                        fbout[at] = AvsBlend.Average(fbout[at], framebuffer[at]);
                    }
                });
            }

            return Swapped;
        }

        var subpixel = _tableSubpixel;
        context.ForEachRow(y =>
        {
            var at = y * w;
            var end = at + w;
            if (subpixel && blend)
            {
                for (; at < end; at++)
                {
                    fbout[at] = AvsBlend.Average(framebuffer[at], MoveHelpers.Bilinear(framebuffer, offsets[at], w, xFraction[at], yFraction[at]));
                }
            }
            else if (subpixel)
            {
                for (; at < end; at++)
                {
                    fbout[at] = MoveHelpers.Bilinear(framebuffer, offsets[at], w, xFraction[at], yFraction[at]);
                }
            }
            else if (blend)
            {
                for (; at < end; at++)
                {
                    fbout[at] = AvsBlend.Average(framebuffer[at], framebuffer[offsets[at]]);
                }
            }
            else
            {
                for (; at < end; at++)
                {
                    fbout[at] = framebuffer[offsets[at]];
                }
            }
        });
        return Swapped;
    }

    private static bool UsesCode(int effect) => effect >= 18 && effect <= LastEffect;

    private void BuildTable(AvsRenderContext context, int w, int h)
    {
        var count = w * h;
        if (_offsets.Length != count)
        {
            _offsets = new int[count];
            _xFraction = new byte[count];
            _yFraction = new byte[count];
        }

        _tableWidth = w;
        _tableHeight = h;
        _tableSubpixel = _subpixel != 0 && w >= 2 && h >= 2 &&
                         ((_effect >= FirstRadial && _effect <= LastEffect && _effect != 7) || _effect == Custom);
        var offsets = _offsets;
        if (_effect == 1)
        {
            // Slight fuzzify: each pixel from a random neighbour, chosen once.
            for (var p = 0; p < count; p++)
            {
                var r = p + Shared.Random.Next(3) - 1 + (Shared.Random.Next(3) - 1) * w;
                offsets[p] = Math.Min(count - 1, Math.Max(r, 0));
            }
        }
        else if (_effect == 2)
        {
            // Shift rotate left.
            for (var y = 0; y < h; y++)
            {
                var lp = w / 64;
                for (var x = 0; x < w; x++)
                {
                    offsets[y * w + x] = y * w + lp++;
                    if (lp >= w)
                    {
                        lp -= w;
                    }
                }
            }
        }
        else if (_effect == 7)
        {
            // Blocky partial out.
            for (var y = 0; y < h; y++)
            {
                for (var x = 0; x < w; x++)
                {
                    if ((x & 2) != 0 || (y & 2) != 0)
                    {
                        offsets[y * w + x] = x + y * w;
                    }
                    else
                    {
                        var xp = w / 2 + ((x & ~1) - w / 2) * 7 / 8;
                        var yp = h / 2 + ((y & ~1) - h / 2) * 7 / 8;
                        offsets[y * w + x] = xp + yp * w;
                    }
                }
            }
        }
        else if (_effect >= FirstRadial && _effect <= LastEffect && !UsesCode(_effect))
        {
            BuildRadial(context, w, h);
        }
        else
        {
            BuildFromCode(w, h);
        }
    }

    private void BuildRadial(AvsRenderContext context, int w, int h)
    {
        var maxD = Math.Sqrt((w * w + h * h) / 4.0);
        var effect = _effect;
        context.ForEachRow(y =>
        {
            for (var x = 0; x < w; x++)
            {
                double xd = x - w / 2;
                double yd = y - h / 2;
                var d = Math.Sqrt(xd * xd + yd * yd);
                var r = Math.Atan2(yd, xd);
                var xo = 0;
                Radial(effect, ref r, ref d, maxD, ref xo);
                var tmp1 = h / 2 + Math.Sin(r) * d + 0.5;
                var tmp2 = w / 2 + Math.Cos(r) * d + 0.5 + xo * w * (1.0 / 256.0);
                Store(y * w + x, tmp1, tmp2, w, h);
            }
        });
    }

    /// <summary>The built-in movements AVS wrote in C, in polar coordinates (r in radians, d in pixels).</summary>
    private static void Radial(int effect, ref double r, ref double d, double maxD, ref int xo)
    {
        double t;
        switch (effect)
        {
            case 3:
                r += 0.1 - 0.2 * (d / maxD);
                d *= 0.96;
                break;
            case 4:
                d *= 0.99 * (1.0 - Math.Sin(r) / 32.0);
                r += 0.03 * Math.Sin(d / maxD * Math.PI * 4);
                break;
            case 5:
                d *= 0.94 + Math.Cos(r * 32.0) * 0.06;
                break;
            case 6:
                d *= 1.01 + Math.Cos(r * 4.0) * 0.04;
                r += 0.03 * Math.Sin(d / maxD * Math.PI * 4);
                break;
            case 8:
                r += 0.1 * Math.Sin(d / maxD * Math.PI * 5);
                break;
            case 9:
                t = Math.Sin(d / maxD * Math.PI);
                d -= 8 * t * t * t * t * t;
                break;
            case 10:
                t = Math.Sin(d / maxD * Math.PI);
                d -= 8 * t * t * t * t * t;
                t = Math.Cos(d / maxD * Math.PI / 2.0);
                r += 0.1 * t * t * t;
                break;
            case 11:
                d *= 0.95 + Math.Cos(r * 5.0 - Math.PI / 2.50) * 0.03;
                break;
            case 12:
                r += 0.04;
                d *= 0.96 + Math.Cos(d / maxD * Math.PI) * 0.05;
                break;
            case 13:
                t = Math.Cos(d / maxD * Math.PI);
                r += 0.07 * t;
                d *= 0.98 + t * 0.10;
                break;
            case 14:
                r += 0.1 - 0.2 * (d / maxD);
                d *= 0.96;
                xo = 8;
                break;
            case 15:
                d = maxD * 0.15;
                break;
            case 16:
                r = Math.Cos(r * 3);
                break;
            case 17:
                d *= 1 - (d / maxD - .35) * .5;
                r += .1;
                break;
        }
    }

    private void BuildFromCode(int w, int h)
    {
        var isCustom = _effect == Custom;
        var source = isCustom ? _expression : BuiltInCode[_effect - 18];
        var rectangular = isCustom ? _rectangular != 0 : BuiltInRectangular[_effect - 18];

        // A fresh set of variables each time, as AVS made for each new table.
        var code = NewContext();
        var dIndex = code.Index("d");
        var rIndex = code.Index("r");
        var xIndex = code.Index("x");
        var yIndex = code.Index("y");
        var swIndex = code.Index("sw");
        var shIndex = code.Index("sh");
        var compiled = MoveHelpers.Compile(this, code, source, "The Movement's code", out var hasCode);
        if (!hasCode || w < 2 || h < 2)
        {
            _tableSubpixel = false;
            for (var i = 0; i < w * h; i++)
            {
                _offsets[i] = i;
            }

            return;
        }

        var v = code.Values;
        v[swIndex] = w;
        v[shIndex] = h;
        var maxD = Math.Sqrt((double)(w * w + h * h)) / 2.0;
        var divMaxD = 1.0 / maxD;
        double w2 = w / 2;
        double h2 = h / 2;
        double xsc = 1.0 / w2, ysc = 1.0 / h2;
        var subpixel = _tableSubpixel;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var xd = x - w2;
                var yd = y - h2;
                v[xIndex] = xd * xsc;
                v[yIndex] = yd * ysc;
                v[dIndex] = Math.Sqrt(xd * xd + yd * yd) * divMaxD;
                v[rIndex] = Math.Atan2(yd, xd) + Math.PI * 0.5;
                compiled.Run();
                double tmp1, tmp2;
                if (!rectangular)
                {
                    var d = v[dIndex] * maxD;
                    var r = v[rIndex] - Math.PI / 2.0;
                    tmp1 = h / 2 + Math.Sin(r) * d;
                    tmp2 = w / 2 + Math.Cos(r) * d;
                }
                else
                {
                    tmp1 = (v[yIndex] + 1.0) * h2;
                    tmp2 = (v[xIndex] + 1.0) * w2;
                }

                if (!subpixel)
                {
                    tmp1 += 0.5;
                    tmp2 += 0.5;
                }

                Store(y * w + x, tmp1, tmp2, w, h);
            }
        }
    }

    /// <summary>Puts where pixel <paramref name="at"/> comes from (row <paramref name="tmp1"/>, column <paramref name="tmp2"/>) in the table, wrapped or kept inside the picture.</summary>
    private void Store(int at, double tmp1, double tmp2, int w, int h)
    {
        var oh = (int)tmp1;
        var ow = (int)tmp2;
        if (_tableSubpixel)
        {
            var xPartial = (int)(32.0 * (tmp2 - ow));
            var yPartial = (int)(32.0 * (tmp1 - oh));
            if (_wrap != 0)
            {
                ow %= w - 1;
                oh %= h - 1;
                if (ow < 0)
                {
                    ow += w - 1;
                }

                if (oh < 0)
                {
                    oh += h - 1;
                }
            }
            else
            {
                if (ow < 0)
                {
                    xPartial = 0;
                    ow = 0;
                }

                if (ow >= w - 1)
                {
                    xPartial = 31;
                    ow = w - 2;
                }

                if (oh < 0)
                {
                    yPartial = 0;
                    oh = 0;
                }

                if (oh >= h - 1)
                {
                    yPartial = 31;
                    oh = h - 2;
                }
            }

            // Packed and unpacked as AVS did, so a negative fraction (from wrapping) comes out the same.
            var packed = (yPartial << 22) | (xPartial << 27);
            _offsets[at] = ow + oh * w;
            _xFraction[at] = (byte)((packed >> 24) & (31 << 3));
            _yFraction[at] = (byte)((packed >> 19) & (31 << 3));
        }
        else
        {
            if (_wrap != 0)
            {
                ow %= w;
                oh %= h;
                if (ow < 0)
                {
                    ow += w;
                }

                if (oh < 0)
                {
                    oh += h;
                }
            }
            else
            {
                ow = Math.Clamp(ow, 0, w - 1);
                oh = Math.Clamp(oh, 0, h - 1);
            }

            _offsets[at] = ow + oh * w;
        }
    }
}
