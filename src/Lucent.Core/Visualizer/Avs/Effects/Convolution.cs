namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Convolution Filter (Holden03, e_convolution.cpp): each pixel becomes a weighted sum of the 7x7
/// pixels around it (sharpen, blur, emboss, edge detection...), plus a bias, divided by a scale.
/// </summary>
/// <remarks>
/// The plug-in wrote machine code for each kernel, working on 16-bit numbers per channel; this does the
/// same sums with the same 16-bit wrapping and clamping, in plain loops.
/// </remarks>
[AvsEffect("Holden03: Convolution Filter")]
public sealed class Convolution : AvsEffect
{
    private const int Dim = 7;
    private const int Size = Dim * Dim;

    private readonly int[] _kernel = new int[Size];
    private bool _enabled = true;
    private bool _wrap;
    private bool _absolute;
    private bool _twoPass;
    private int _bias;
    private int _scale = 1;
    private Plan? _plan;
    private ulong[] _spread = Array.Empty<ulong>();

    public Convolution(AvsShared shared)
        : base(shared)
    {
        _kernel[Size / 2] = 1;
    }

    public override void Load(AvsConfigReader config)
    {
        if (config.HasInt)
        {
            _enabled = config.ReadInt() == 1;
        }

        if (config.HasInt)
        {
            _wrap = config.ReadInt() == 1;
        }

        if (config.HasInt)
        {
            _absolute = config.ReadInt() == 1;
        }

        if (config.HasInt)
        {
            _twoPass = config.ReadInt() == 1;
        }

        for (var i = 0; i < Size; i++)
        {
            _kernel[i] = config.ReadInt(_kernel[i]);
        }

        _bias = config.ReadInt(_bias);
        _scale = config.ReadInt(_scale);
        if (_scale == 0)
        {
            _scale = 1;
        }

        _plan = null;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (!_enabled)
        {
            return 0;
        }

        var plan = _plan ??= new Plan(this);
        int w = context.Width, h = context.Height;
        var lastRow = Math.Max(0, h - 2);
        ulong[]? spread = null;
        if (plan.Packed)
        {
            // Each pixel's three channels spread 16 bits apart, so one multiply weighs all three at once.
            if (_spread.Length != w * h)
            {
                _spread = new ulong[w * h];
            }

            spread = _spread;
            context.ForEachRow(y =>
            {
                for (int at = y * w, end = at + w; at < end; at++)
                {
                    var p = (ulong)(uint)framebuffer[at];
                    spread[at] = (p & 0xff) | ((p & 0xff00) << 8) | ((p & 0xff0000) << 16);
                }
            });
        }

        context.ForEachRow(y =>
        {
            Span<int> rows = stackalloc int[Dim];
            for (var k = 0; k < Dim; k++)
            {
                // As the plug-in: rows past the top repeat the first, and it never reached the very last row.
                rows[k] = Math.Clamp(y + k - 3, 0, lastRow) * w;
            }

            if (spread is not null)
            {
                plan.PackedRow(spread, framebuffer, fbout, rows, y * w, w);
            }
            else
            {
                plan.Row(framebuffer, fbout, rows, y * w, w);
            }
        });
        return Swapped;
    }

    /// <summary>One kernel entry: where it reads, and its weight (only its low 16 bits count, as in the plug-in).</summary>
    private readonly record struct Tap(int X, int Y, uint Weight);

    /// <summary>The kernel worked out once: the adding and subtracting taps of each pass, and how to scale.</summary>
    private sealed class Plan
    {
        private readonly Tap[][] _positive;
        private readonly Tap[][] _negative;
        private readonly bool _saturatePositive;
        private readonly bool _saturateNegative;
        private readonly int _positiveBias;
        private readonly int _negativeBias;
        private readonly bool _hasNegative;
        private readonly bool _wrap;
        private readonly bool _absolute;
        private readonly int _divisor;
        private readonly int _divisorShift;
        private readonly int _reciprocal;

        public Plan(Convolution filter)
        {
            var kernel = filter._kernel;
            long positiveSum = 0, negativeSum = 0;
            foreach (var v in kernel)
            {
                if (v > 0)
                {
                    positiveSum += v;
                }
                else
                {
                    negativeSum -= v;
                }
            }

            var bias = filter._bias;
            if (bias < 0)
            {
                negativeSum -= bias;
            }
            else
            {
                positiveSum += bias;
            }

            _saturatePositive = positiveSum >= 256;
            _saturateNegative = negativeSum >= 256;
            _wrap = filter._wrap;
            _absolute = filter._absolute;

            // A negative scale swaps which taps add and which subtract.
            var flip = filter._scale < 0;
            _divisor = (int)Math.Min(Math.Abs((long)filter._scale), int.MaxValue);

            // The bias was stored as 256 times its size, as a 16-bit number.
            var biasWord = (int)((256L * Math.Abs((long)bias)) & 0xffff);
            if (bias != 0)
            {
                if ((bias > 0) != flip)
                {
                    _positiveBias = biasWord;
                }
                else
                {
                    _negativeBias = biasWord;
                }
            }

            var passes = filter._twoPass ? 2 : 1;
            _positive = new Tap[passes][];
            _negative = new Tap[passes][];
            for (var pass = 0; pass < passes; pass++)
            {
                var positive = new List<Tap>();
                var negative = new List<Tap>();
                for (var i = 0; i < Size; i++)
                {
                    var v = kernel[i];
                    if (v == 0)
                    {
                        continue;
                    }

                    // The second pass uses the kernel turned through 90 degrees.
                    var weight = (uint)(Math.Abs((long)v) & 0xffff);
                    var tap = pass == 0
                        ? new Tap(i % Dim, i / Dim, weight)
                        : new Tap(6 - i / Dim, i % Dim, weight);
                    ((v > 0) != flip ? positive : negative).Add(tap);
                }

                _positive[pass] = positive.ToArray();
                _negative[pass] = negative.ToArray();
            }

            _hasNegative = _negative[0].Length > 0 || _negativeBias != 0;

            // While neither side's weights add up to 256, no channel's sum can pass 16 bits.
            Packed = !_saturatePositive && !_saturateNegative;
            _divisorShift = -1;
            if (_divisor > 1 && (_divisor & (_divisor - 1)) == 0)
            {
                _divisorShift = System.Numerics.BitOperations.Log2((uint)_divisor);
            }

            _reciprocal = _divisor > 1 ? (65536 / _divisor) & 0xffff : 0;
        }

        /// <summary>Whether the sums are small enough to work on all three channels at once (<see cref="PackedRow"/>).</summary>
        public bool Packed { get; }

        /// <summary>
        /// <see cref="Row"/> for small sums: the channels of <paramref name="spread"/> are added side by side in
        /// one 64-bit number, with the picture's sides still done one channel at a time.
        /// </summary>
        public void PackedRow(ulong[] spread, int[] f, int[] o, ReadOnlySpan<int> rows, int start, int w)
        {
            // Where each tap reads, less x, for this row (each pass's adding taps, then its subtracting ones).
            var passes = _positive.Length;
            Span<int> offsets = stackalloc int[Size * 4];
            Span<ulong> weights = stackalloc ulong[Size * 4];
            Span<int> counts = stackalloc int[4];
            var n = 0;
            for (var list = 0; list < passes * 2; list++)
            {
                var taps = list % 2 == 0 ? _positive[list / 2] : _negative[list / 2];
                counts[list] = taps.Length;
                foreach (var tap in taps)
                {
                    offsets[n] = rows[tap.Y] + tap.X - 3;
                    weights[n] = tap.Weight;
                    n++;
                }
            }

            var positiveBias = (ulong)_positiveBias * 0x0000_0001_0001_0001UL;
            var negativeBias = (ulong)_negativeBias * 0x0000_0001_0001_0001UL;
            for (var x = 0; x < w; x++)
            {
                if (x < 3 || x >= w - 3)
                {
                    EdgePixel(f, o, rows, start, x, w);
                    continue;
                }

                int b = 0, g = 0, r = 0;
                var t = 0;
                for (var pass = 0; pass < passes; pass++)
                {
                    var add = positiveBias;
                    for (var end = t + counts[pass * 2]; t < end; t++)
                    {
                        add += spread[x + offsets[t]] * weights[t];
                    }

                    var take = negativeBias;
                    for (var end = t + counts[pass * 2 + 1]; t < end; t++)
                    {
                        take += spread[x + offsets[t]] * weights[t];
                    }

                    int pb = (int)(add & 0xffff), pg = (int)((add >> 16) & 0xffff), pr = (int)((add >> 32) & 0xffff);
                    if (_hasNegative)
                    {
                        pb = Subtract(pb, (int)(take & 0xffff));
                        pg = Subtract(pg, (int)((take >> 16) & 0xffff));
                        pr = Subtract(pr, (int)((take >> 32) & 0xffff));
                    }

                    if (pass == 0)
                    {
                        (b, g, r) = (pb, pg, pr);
                    }
                    else
                    {
                        b = Math.Min(b + pb, 0xffff);
                        g = Math.Min(g + pg, 0xffff);
                        r = Math.Min(r + pr, 0xffff);
                    }
                }

                o[start + x] = Finish(b) | (Finish(g) << 8) | (Finish(r) << 16);
            }
        }

        private void EdgePixel(int[] f, int[] o, ReadOnlySpan<int> rows, int start, int x, int w)
        {
            Pass(f, rows, x, w, true, 0, out var b, out var g, out var r);
            if (_positive.Length == 2)
            {
                Pass(f, rows, x, w, true, 1, out var b2, out var g2, out var r2);
                b = Math.Min(b + b2, 0xffff);
                g = Math.Min(g + g2, 0xffff);
                r = Math.Min(r + r2, 0xffff);
            }

            o[start + x] = Finish(b) | (Finish(g) << 8) | (Finish(r) << 16);
        }

        /// <summary>Filters one row, starting at <paramref name="start"/>, into <paramref name="o"/>.</summary>
        public void Row(int[] f, int[] o, ReadOnlySpan<int> rows, int start, int w)
        {
            for (var x = 0; x < w; x++)
            {
                // Away from the sides no column needs holding inside the picture.
                var edge = x < 3 || x >= w - 3;
                Pass(f, rows, x, w, edge, 0, out var b, out var g, out var r);
                if (_positive.Length == 2)
                {
                    Pass(f, rows, x, w, edge, 1, out var b2, out var g2, out var r2);
                    b = Math.Min(b + b2, 0xffff);
                    g = Math.Min(g + g2, 0xffff);
                    r = Math.Min(r + r2, 0xffff);
                }

                o[start + x] = Finish(b) | (Finish(g) << 8) | (Finish(r) << 16);
            }
        }

        private int Finish(int v)
        {
            if (_divisor > 1)
            {
                v = _divisorShift >= 0 ? v >> _divisorShift : (v * _reciprocal) >> 16;
            }

            // Packed back to a byte as a signed 16-bit number: "negative" values (top bit set) become 0.
            return v >= 0x8000 ? 0 : Math.Min(v, 255);
        }

        private void Pass(int[] f, ReadOnlySpan<int> rows, int x, int w, bool edge, int pass, out int b, out int g, out int r)
        {
            Sum(f, rows, x, w, edge, _positive[pass], _saturatePositive, _positiveBias, out b, out g, out r);
            if (!_hasNegative)
            {
                return;
            }

            Sum(f, rows, x, w, edge, _negative[pass], _saturateNegative, _negativeBias, out var nb, out var ng, out var nr);
            b = Subtract(b, nb);
            g = Subtract(g, ng);
            r = Subtract(r, nr);
        }

        private int Subtract(int add, int take)
        {
            if (_absolute)
            {
                // A saturating signed subtraction, then the sign bit dropped.
                return Math.Clamp((short)add - (short)take, short.MinValue, short.MaxValue) & 0x7fff;
            }

            return _wrap ? (add - take) & 0xffff : Math.Max(add - take, 0);
        }

        private static void Sum(int[] f, ReadOnlySpan<int> rows, int x, int w, bool edge, Tap[] taps, bool saturate, int bias, out int b, out int g, out int r)
        {
            uint sb = 0, sg = 0, sr = 0;
            foreach (var tap in taps)
            {
                int at;
                if (edge)
                {
                    var column = x + tap.X - 3;
                    if (x >= w - 3)
                    {
                        // The last three columns were done separately and never read the last column.
                        column = Math.Min(column, w - 2);
                    }

                    at = rows[tap.Y] + Math.Clamp(column, 0, w - 1);
                }
                else
                {
                    at = rows[tap.Y] + x + tap.X - 3;
                }

                var p = (uint)f[at];
                var weight = tap.Weight;
                sb += (p & 0xff) * weight & 0xffff;
                sg += ((p >> 8) & 0xff) * weight & 0xffff;
                sr += ((p >> 16) & 0xff) * weight & 0xffff;
                if (saturate)
                {
                    sb = Math.Min(sb, 0xffff);
                    sg = Math.Min(sg, 0xffff);
                    sr = Math.Min(sr, 0xffff);
                }
                else
                {
                    sb &= 0xffff;
                    sg &= 0xffff;
                    sr &= 0xffff;
                }
            }

            // The bias is always added with saturation.
            b = (int)Math.Min(sb + (uint)bias, 0xffff);
            g = (int)Math.Min(sg + (uint)bias, 0xffff);
            r = (int)Math.Min(sr + (uint)bias, 0xffff);
        }
    }
}
