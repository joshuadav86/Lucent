using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Dynamic Distance Modifier (r_ddm.cpp): the preset's code says, for each ring of pixels around the
/// centre, how far out the ring's picture is read from, so the picture zooms, pulses, or warps around the middle.
/// </summary>
[AvsEffect(35)]
public sealed class DynamicDistanceModifier : AvsEffect
{
    // AVS's integer square root (after Arne Steinarson): the top bits looked up here, the rest by powers of two.
    private static readonly byte[] SquareRoots =
    {
        0, 16, 22, 27, 32, 35, 39, 42, 45, 48, 50, 53, 55, 57, 59, 61, 64, 65,
        67, 69, 71, 73, 75, 76, 78, 80, 81, 83, 84, 86, 87, 89, 90, 91, 93, 94,
        96, 97, 98, 99, 101, 102, 103, 104, 106, 107, 108, 109, 110, 112, 113,
        114, 115, 116, 117, 118, 119, 120, 121, 122, 123, 124, 125, 126, 128,
        128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141,
        142, 143, 144, 144, 145, 146, 147, 148, 149, 150, 150, 151, 152, 153,
        154, 155, 155, 156, 157, 158, 159, 160, 160, 161, 162, 163, 163, 164,
        165, 166, 167, 167, 168, 169, 170, 170, 171, 172, 173, 173, 174, 175,
        176, 176, 177, 178, 178, 179, 180, 181, 181, 182, 183, 183, 184, 185,
        185, 186, 187, 187, 188, 189, 189, 190, 191, 192, 192, 193, 193, 194,
        195, 195, 196, 197, 197, 198, 199, 199, 200, 201, 201, 202, 203, 203,
        204, 204, 205, 206, 206, 207, 208, 208, 209, 209, 210, 211, 211, 212,
        212, 213, 214, 214, 215, 215, 216, 217, 217, 218, 218, 219, 219, 220,
        221, 221, 222, 222, 223, 224, 224, 225, 225, 226, 226, 227, 227, 228,
        229, 229, 230, 230, 231, 231, 232, 232, 233, 234, 234, 235, 235, 236,
        236, 237, 237, 238, 238, 239, 240, 240, 241, 241, 242, 242, 243, 243,
        244, 244, 245, 245, 246, 246, 247, 247, 248, 248, 249, 249, 250, 250,
        251, 251, 252, 252, 253, 253, 254, 254, 255,
    };

    // pixel, frame, beat, init
    private readonly string[] _code =
    {
        "d=d-sigmoid((t-50)/100,2)",
        "t=t+u;t=min(100,t);t=max(0,t);u=if(equal(t,100),-1,u);u=if(equal(t,0),1,u)",
        string.Empty,
        "u=1;t=0",
    };

    private int _blend;
    private int _subpixel;
    private int[] _table = Array.Empty<int>();
    private Compiled? _compiled;

    public DynamicDistanceModifier(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        if (config[0] == 1)
        {
            config.Position = 1;
            for (var i = 0; i < 4; i++)
            {
                _code[i] = config.ReadString();
            }
        }
        else
        {
            // Older presets: pixel and frame code in 256 bytes each, then beat and init code the same way.
            if (config.Remaining >= 512)
            {
                _code[0] = config.ReadFixedString(256);
                _code[1] = config.ReadFixedString(256);
            }

            if (config.Remaining >= 512)
            {
                _code[2] = config.ReadFixedString(256);
                _code[3] = config.ReadFixedString(256);
            }
        }

        _blend = config.ReadInt(_blend);
        _subpixel = config.ReadInt(_subpixel);
    }

    public override void Restart()
    {
        if (_compiled is not null)
        {
            _compiled.Initialised = false;
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var code = _compiled ??= new Compiled(this);
        int w = context.Width, h = context.Height;
        var maxD = Math.Sqrt((w * w + h * h) / 4.0);
        var tableSize = Math.Max(33, (int)(maxD + 32.9));
        if (_table.Length != tableSize)
        {
            _table = new int[tableSize];
        }

        var v = code.Values;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;
        if (code.HasInit && !code.Initialised)
        {
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        var table = _table;
        if (code.HasPixel)
        {
            int x;
            for (x = 0; x < tableSize - 32; x++)
            {
                v[code.D] = x / (maxD - 1);
                code.Pixel.Run();
                table[x] = (int)(v[code.D] * 256.0 * maxD / (x + 1));
            }

            for (; x < tableSize; x++)
            {
                table[x] = table[x - 1];
            }
        }
        else
        {
            Array.Clear(table);
        }

        if (w < 2 || h < 2)
        {
            return 0;
        }

        var subpixel = _subpixel != 0;
        var blend = _blend != 0;
        var last = tableSize - 1;
        context.ForEachRow(y =>
        {
            int w2 = w / 2, h2 = h / 2;
            var ty = y - h2;
            var x2 = w2 * w2 + w2 + ty * ty + 256;
            var dx2 = -2 * w2;
            var xxsc = -w2;
            var at = y * w;
            for (var end = at + w; at < end; at++)
            {
                var qd = table[Math.Min(SquareRoot((uint)x2), last)];
                x2 += dx2;
                dx2 += 2;
                int colour;
                if (subpixel)
                {
                    var xPart = qd * xxsc + 128;
                    var yPart = qd * ty + 128;
                    var ow = Math.Clamp(w2 + (xPart >> 8), 0, w - 2);
                    var oh = Math.Clamp(h2 + (yPart >> 8), 0, h - 2);
                    colour = MoveHelpers.Bilinear(framebuffer, ow + oh * w, w, xPart & 0xff, yPart & 0xff);
                }
                else
                {
                    var ow = Math.Clamp(w2 + ((qd * xxsc + 128) >> 8), 0, w - 1);
                    var oh = Math.Clamp(h2 + ((qd * ty + 128) >> 8), 0, h - 1);
                    colour = framebuffer[ow + oh * w];
                }

                xxsc++;
                fbout[at] = blend ? AvsBlend.Average(colour, framebuffer[at]) : colour;
            }
        });

        return Swapped;
    }

    private static int SquareRoot(uint n)
    {
        if (n >= 0x10000)
        {
            if (n >= 0x1000000)
            {
                if (n >= 0x10000000)
                {
                    return n >= 0x40000000 ? SquareRoots[n >> 24] << 8 : SquareRoots[n >> 22] << 7;
                }

                return n >= 0x4000000 ? SquareRoots[n >> 20] << 6 : SquareRoots[n >> 18] << 5;
            }

            if (n >= 0x100000)
            {
                return n >= 0x400000 ? SquareRoots[n >> 16] << 4 : SquareRoots[n >> 14] << 3;
            }

            return n >= 0x40000 ? SquareRoots[n >> 12] << 2 : SquareRoots[n >> 10] << 1;
        }

        if (n >= 0x100)
        {
            if (n >= 0x1000)
            {
                return n >= 0x4000 ? SquareRoots[n >> 8] : SquareRoots[n >> 6] >> 1;
            }

            return n >= 0x400 ? SquareRoots[n >> 4] >> 2 : SquareRoots[n >> 2] >> 3;
        }

        if (n >= 0x10)
        {
            return n >= 0x40 ? SquareRoots[n] >> 4 : SquareRoots[n << 2] << 5;
        }

        return n >= 0x4 ? SquareRoots[n >> 4] << 6 : SquareRoots[n >> 6] << 7;
    }

    /// <summary>The effect's four pieces of code, compiled the first time it draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(DynamicDistanceModifier effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            D = _context.Index("d");
            B = _context.Index("b");
            Pixel = MoveHelpers.Compile(effect, _context, effect._code[0], "The Dynamic Distance Modifier's per pixel code", out var hasPixel);
            Frame = MoveHelpers.Compile(effect, _context, effect._code[1], "The Dynamic Distance Modifier's per frame code", out _);
            Beat = MoveHelpers.Compile(effect, _context, effect._code[2], "The Dynamic Distance Modifier's on beat code", out _);
            Init = MoveHelpers.Compile(effect, _context, effect._code[3], "The Dynamic Distance Modifier's init code", out var hasInit);
            HasPixel = hasPixel;
            HasInit = hasInit;
        }

        public int D { get; }

        public int B { get; }

        public EelCode Pixel { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Init { get; }

        public bool HasPixel { get; }

        public bool HasInit { get; }

        public bool Initialised { get; set; }

        public double[] Values => _context.Values;
    }
}
