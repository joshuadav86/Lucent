using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Trans / Dynamic Shift (r_shift.cpp): slides the whole picture by an x and y the preset's code works out each frame.</summary>
[AvsEffect(42)]
public sealed class DynamicShift : AvsEffect
{
    private readonly string[] _code = { "d=0;", "x=sin(d)*1.4; y=1.4*cos(d); d=d+0.01;", "d=d+2.0" };
    private int _blend;
    private int _subpixel = 1;
    private int _lastWidth;
    private int _lastHeight;
    private Compiled? _compiled;

    public DynamicShift(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        if (config[0] == 1)
        {
            config.Position = 1;
            for (var i = 0; i < 3; i++)
            {
                _code[i] = config.ReadString();
            }
        }
        else if (config.Remaining >= 768)
        {
            // Older presets: init, frame, and beat code in 256 bytes each.
            for (var i = 0; i < 3; i++)
            {
                _code[i] = config.ReadFixedString(256);
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
        var v = code.Values;
        v[code.W] = w;
        v[code.H] = h;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;

        // As in AVS, the init code (and with it x, y, and alpha's starting values) only runs if there is some.
        if (code.HasInit && (!code.Initialised || _lastWidth != w || _lastHeight != h))
        {
            _lastWidth = w;
            _lastHeight = h;
            v[code.X] = 0;
            v[code.Y] = 0;
            v[code.Alpha] = 0.5;
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        if (w < 2 || h < 2)
        {
            return 0;
        }

        var doBlend = _blend != 0;
        var alpha = 127;
        if (doBlend)
        {
            alpha = (int)(v[code.Alpha] * 255.0);
            if (alpha <= 0)
            {
                return 0;
            }

            if (alpha >= 255)
            {
                doBlend = false;
            }
        }

        var vx = v[code.X];
        var vy = v[code.Y];
        var xa = (int)vx;
        var ya = (int)vy;
        if (_subpixel == 0)
        {
            ShiftWhole(context, framebuffer, fbout, xa, ya, doBlend, alpha);
        }
        else
        {
            ShiftFiltered(context, framebuffer, fbout, vx, vy, xa, ya, doBlend, alpha);
        }

        return Swapped;
    }

    private static void ShiftWhole(AvsRenderContext context, int[] framebuffer, int[] fbout, int xa, int ya, bool doBlend, int alpha)
    {
        int w = context.Width, h = context.Height;
        var endY = Math.Min(h + ya, h);
        var endX = Math.Min(w + xa, w);
        ya = Math.Min(ya, h);
        xa = Math.Min(xa, w);
        context.ForEachRow(y =>
        {
            var at = y * w;
            if (y < ya || y >= endY)
            {
                Empty(framebuffer, fbout, at, w, doBlend, alpha);
                return;
            }

            // The source row is y - ya, and the column x - xa.
            var from = (y - ya) * w - xa;
            for (var x = 0; x < w; x++, at++)
            {
                var colour = x >= xa && x < endX ? framebuffer[from + x] : 0;
                fbout[at] = doBlend ? AvsBlend.Adjust(colour, framebuffer[at], alpha) : colour;
            }
        });
    }

    private static void ShiftFiltered(AvsRenderContext context, int[] framebuffer, int[] fbout, double vx, double vy, int xa, int ya, bool doBlend, int alpha)
    {
        int w = context.Width, h = context.Height;
        var xPart = (int)((vx - (int)vx) * 255.0);
        if (xPart < 0)
        {
            xPart = -xPart;
        }
        else
        {
            xa++;
            xPart = 255 - xPart;
        }

        xPart = Math.Clamp(xPart, 0, 255);
        var yPart = (int)((vy - (int)vy) * 255.0);
        if (yPart < 0)
        {
            yPart = -yPart;
        }
        else
        {
            ya++;
            yPart = 255 - yPart;
        }

        yPart = Math.Clamp(yPart, 0, 255);
        ya = Math.Clamp(ya, 1 - h, h - 1);
        xa = Math.Clamp(xa, 1 - w, w - 1);
        var endY = Math.Max(Math.Min(h - 1 + ya, h - 1), 0);
        var endX = Math.Max(Math.Min(w - 1 + xa, w - 1), 0);
        context.ForEachRow(y =>
        {
            var at = y * w;
            if (y < ya || y >= endY)
            {
                Empty(framebuffer, fbout, at, w, doBlend, alpha);
                return;
            }

            // The source row is y - ya, and the column x - xa; endX and endY keep the four pixels read inside the picture.
            var from = (y - ya) * w - xa;
            for (var x = 0; x < w; x++, at++)
            {
                var colour = x >= xa && x < endX ? MoveHelpers.Bilinear(framebuffer, from + x, w, xPart, yPart) : 0;
                fbout[at] = doBlend ? AvsBlend.Adjust(colour, framebuffer[at], alpha) : colour;
            }
        });
    }

    private static void Empty(int[] framebuffer, int[] fbout, int at, int w, bool doBlend, int alpha)
    {
        for (var end = at + w; at < end; at++)
        {
            fbout[at] = doBlend ? AvsBlend.Adjust(0, framebuffer[at], alpha) : 0;
        }
    }

    /// <summary>The effect's three pieces of code, compiled the first time it draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(DynamicShift effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            X = _context.Index("x");
            Y = _context.Index("y");
            W = _context.Index("w");
            H = _context.Index("h");
            B = _context.Index("b");
            Alpha = _context.Index("alpha");
            Init = MoveHelpers.Compile(effect, _context, effect._code[0], "The Dynamic Shift's init code", out var hasInit);
            Frame = MoveHelpers.Compile(effect, _context, effect._code[1], "The Dynamic Shift's per frame code", out _);
            Beat = MoveHelpers.Compile(effect, _context, effect._code[2], "The Dynamic Shift's on beat code", out _);
            HasInit = hasInit;
        }

        public int X { get; }

        public int Y { get; }

        public int W { get; }

        public int H { get; }

        public int B { get; }

        public int Alpha { get; }

        public EelCode Init { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public bool HasInit { get; }

        public bool Initialised { get; set; }

        public double[] Values => _context.Values;
    }
}
