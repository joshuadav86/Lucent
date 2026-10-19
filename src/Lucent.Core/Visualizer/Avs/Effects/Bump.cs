using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Bump (r_bump.cpp): lights the picture as if it were embossed, its brightness taken as height, from a
/// light whose place the preset's code moves around.
/// </summary>
[AvsEffect(29)]
public sealed class Bump : AvsEffect
{
    private int _enabled = 1;
    private int _onBeat;
    private int _durationFrames = 15;
    private int _depth = 30;
    private int _beatDepth = 100;
    private int _blend;
    private int _blendAverage;
    private string _frameCode = "x=0.5+cos(t)*0.3;\r\ny=0.5+sin(t)*0.3;\r\nt=t+0.1;";
    private string _beatCode = string.Empty;
    private string _initCode = "t=0;";
    private int _showLight;
    private int _invert;
    private int _oldStyle;
    private int _buffer;

    private int _thisDepth = 30;
    private int _framesLeft;
    private Compiled? _compiled;

    public Bump(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _onBeat = config.ReadInt(_onBeat);
        _durationFrames = config.ReadInt(_durationFrames);
        _depth = config.ReadInt(_depth);
        _beatDepth = config.ReadInt(_beatDepth);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _frameCode = config.ReadString();
        _beatCode = config.ReadString();
        _initCode = config.ReadString();
        _showLight = config.ReadInt(_showLight);
        _invert = config.ReadInt(_invert);

        // Presets from before the light's place went from 0..100 to 0..1 didn't save this.
        _oldStyle = config.ReadInt(1);
        _buffer = config.ReadInt(_buffer);
        _thisDepth = _depth;
        _framesLeft = 0;
    }

    public override void Restart()
    {
        if (_compiled is not null)
        {
            _compiled.Initialised = false;
        }

        _thisDepth = _depth;
        _framesLeft = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        var code = _compiled ??= new Compiled(this);
        int w = context.Width, h = context.Height;
        var depthBuffer = _buffer == 0 ? framebuffer : Shared.Buffer(_buffer - 1, w, h, false);
        if (depthBuffer is null)
        {
            return 0;
        }

        var sameBuffer = depthBuffer == framebuffer;
        if (!code.Initialised)
        {
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        var v = code.Values;
        v[code.IsBeat] = context.IsBeat ? -1 : 1;
        v[code.IsLongBeat] = _framesLeft != 0 ? -1 : 1;
        if (_onBeat != 0 && context.IsBeat)
        {
            _thisDepth = _beatDepth;
            _framesLeft = _durationFrames;
        }
        else if (_framesLeft == 0)
        {
            _thisDepth = _depth;
        }

        Array.Clear(fbout, 0, w * h);
        int cx, cy;
        if (_oldStyle != 0)
        {
            cx = (int)(v[code.X] / 100.0 * w);
            cy = (int)(v[code.Y] / 100.0 * h);
        }
        else
        {
            cx = (int)(v[code.X] * w);
            cy = (int)(v[code.Y] * h);
        }

        cx = Math.Max(0, Math.Min(w, cx));
        cy = Math.Max(0, Math.Min(h, cy));
        if (_showLight != 0)
        {
            AvsDraw.Dot(fbout, cx, cy, w, h, 0xffffff, (int)MathF.Round(context.DotScale), 0);
        }

        v[code.Bi] = Math.Min(Math.Max(v[code.Bi], 0), 1);
        _thisDepth = (int)(_thisDepth * v[code.Bi]);
        var depthScaled = (_thisDepth << 8) / 100;

        if (w >= 3 && h >= 3)
        {
            var invert = _invert != 0;
            var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
            context.ForEachRow(1, h - 1, y =>
            {
                var ly = y - cy;
                var lx = 1 - cx;
                for (int at = y * w + 1, end = y * w + w - 1; at < end; at++, lx++)
                {
                    int m1 = depthBuffer[at - 1], p1 = depthBuffer[at + 1], mw = depthBuffer[at - w], pw = depthBuffer[at + w];
                    if (sameBuffer && m1 == 0 && p1 == 0 && mw == 0 && pw == 0)
                    {
                        continue;
                    }

                    var c1 = DepthOf(p1, invert) - DepthOf(m1, invert) - lx;
                    var c2 = DepthOf(pw, invert) - DepthOf(mw, invert) - ly;
                    c1 = 127 - Math.Abs(c1);
                    c2 = 127 - Math.Abs(c2);
                    var under = framebuffer[at];
                    var lit = c1 <= 0 || c2 <= 0 ? SetDepth0(under) : SetDepth((c1 * c2 * depthScaled) >> (8 + 6), under);
                    fbout[at] = mode switch
                    {
                        1 => AvsBlend.Add(under, lit),
                        2 => AvsBlend.Average(under, lit),
                        _ => lit,
                    };
                }
            });
        }

        if (_framesLeft != 0)
        {
            _framesLeft--;
            if (_framesLeft != 0)
            {
                var step = Math.Abs(_depth - _beatDepth) / _durationFrames;
                _thisDepth += step * (_beatDepth > _depth ? -1 : 1);
            }
        }

        return Swapped;
    }

    private static int DepthOf(int c, bool invert)
    {
        var r = Math.Max(Math.Max(c & 0xff, (c & 0xff00) >> 8), (c & 0xff0000) >> 16);
        return invert ? 255 - r : r;
    }

    private static int SetDepth(int l, int c)
    {
        var r = Math.Min((c & 0xff) + l, 254);
        r |= Math.Min((c & 0xff00) + (l << 8), 254 << 8);
        r |= Math.Min((c & 0xff0000) + (l << 16), 254 << 16);
        return r;
    }

    private static int SetDepth0(int c)
    {
        var r = Math.Min(c & 0xff, 254);
        r |= Math.Min(c & 0xff00, 254 << 8);
        r |= Math.Min(c & 0xff0000, 254 << 16);
        return r;
    }

    /// <summary>The effect's three pieces of code, compiled the first time it draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(Bump effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            X = _context.Index("x");
            Y = _context.Index("y");
            IsBeat = _context.Index("isbeat");
            IsLongBeat = _context.Index("islbeat");
            Bi = _context.Index("bi");
            _context.Values[Bi] = 1.0;
            Frame = effect.Compile(_context, effect._frameCode, "The Bump's per frame code");
            Beat = effect.Compile(_context, effect._beatCode, "The Bump's on beat code");
            Init = effect.Compile(_context, effect._initCode, "The Bump's init code");
        }

        public int X { get; }

        public int Y { get; }

        public int IsBeat { get; }

        public int IsLongBeat { get; }

        public int Bi { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Init { get; }

        public bool Initialised { get; set; }

        public double[] Values => _context.Values;
    }
}
