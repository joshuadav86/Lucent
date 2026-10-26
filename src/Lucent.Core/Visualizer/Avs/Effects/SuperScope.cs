using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Render / SuperScope (r_sscope.cpp): code places n points (x and y from -1 to 1) along the waveform or
/// spectrum, and they're drawn as dots or joined into lines, in a colour that cycles through the effect's
/// colours unless the code sets red, green and blue itself.
/// </summary>
[AvsEffect(36)]
public sealed class SuperScope : AvsEffect
{
    private const int MaxPoints = 128 * 1024;

    private readonly string[] _code = { "d=i+v*0.2; r=t+i*$PI*4; x=cos(r)*d; y=sin(r)*d", "t=t-0.05", string.Empty, "n=800" };
    private readonly int[] _colours = new int[16];
    private readonly byte[] _source = new byte[AvsAudio.Samples + 1];
    private int _channel = 2;
    private int _colourCount = 1;
    private int _mode;
    private int _colourPosition;
    private Compiled? _compiled;

    public SuperScope(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
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
        else if (config.Remaining >= 1024)
        {
            // Older presets: four pieces of code in 256 bytes each.
            for (var i = 0; i < 4; i++)
            {
                config.Position = i * 256;
                _code[i] = config.ReadTerminatedString(256);
            }

            config.Position = 1024;
        }

        _channel = config.ReadInt(_channel);
        _colourCount = config.ReadInt(_colourCount);
        if (_colourCount <= 16)
        {
            for (var i = 0; i < _colourCount && config.HasInt; i++)
            {
                _colours[i] = config.ReadInt();
            }
        }
        else
        {
            _colourCount = 0;
        }

        _mode = config.ReadInt(_mode);
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
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var audio = context.Audio;

        // Bit 2 picks the spectrum rather than the waveform; the low bits the channel (2 = both, averaged).
        var spectrum = (_channel & 4) != 0;
        var type = spectrum ? 0 : 1;
        var xor = spectrum ? 0 : 128;
        for (var i = 0; i < AvsAudio.Samples; i++)
        {
            // As AVS: both channels averaged as signed chars, then read back unsigned.
            var value = (_channel & 3) >= 2
                ? audio.Signed(type, 0, i) / 2 + audio.Signed(type, 1, i) / 2
                : audio.Signed(type, _channel & 3, i);
            _source[i] = (byte)((byte)value ^ xor);
        }

        _source[AvsAudio.Samples] = _source[AvsAudio.Samples - 1];

        _colourPosition++;
        if (_colourPosition >= _colourCount * 64)
        {
            _colourPosition = 0;
        }

        var p = _colourPosition / 64;
        var r = _colourPosition & 63;
        var c1 = _colours[p];
        var c2 = p + 1 < _colourCount ? _colours[p + 1] : _colours[0];
        var r1 = ((c1 & 255) * (63 - r) + (c2 & 255) * r) / 64;
        var r2 = (((c1 >> 8) & 255) * (63 - r) + ((c2 >> 8) & 255) * r) / 64;
        var r3 = (((c1 >> 16) & 255) * (63 - r) + ((c2 >> 16) & 255) * r) / 64;
        var colour = r1 | (r2 << 8) | (r3 << 16);

        var v = code.Values;
        v[code.H] = h;
        v[code.W] = w;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;
        v[code.Blue] = (colour & 0xff) / 255.0;
        v[code.Green] = ((colour >> 8) & 0xff) / 255.0;
        v[code.Red] = ((colour >> 16) & 0xff) / 255.0;
        v[code.Skip] = 0.0;
        v[code.LineSize] = (Shared.LineBlendMode >> 16) & 0xff;
        v[code.DrawMode] = _mode != 0 ? 1.0 : 0.0;
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

        var blend = Shared.LineBlendMode;
        var scale = context.DotScale;
        var dotSize = (int)MathF.Round(scale);
        var candraw = false;
        int lx = 0, ly = 0;
        var count = Math.Min((int)code.Values[code.N], MaxPoints);
        for (var a = 0; a < count; a++)
        {
            var position = a * 576.0 / count;
            var whole = (int)position;
            var fraction = position - whole;
            var sample = _source[whole] * (1.0 - fraction) + _source[whole + 1] * fraction;
            v = code.Values;
            v[code.V] = sample / 128.0 - 1.0;
            v[code.I] = (double)a / (count - 1);
            v[code.Skip] = 0.0;
            code.Point.Run();
            v = code.Values;
            var x = (int)((v[code.X] + 1.0) * w * 0.5);
            var y = (int)((v[code.Y] + 1.0) * h * 0.5);
            if (v[code.Skip] < 0.00001)
            {
                var pointColour = ToByte(v[code.Blue]) | (ToByte(v[code.Green]) << 8) | (ToByte(v[code.Red]) << 16);
                if (v[code.DrawMode] < 0.00001)
                {
                    AvsDraw.Dot(framebuffer, x, y, w, h, pointColour, dotSize, blend);
                }
                else if (candraw && ((pointColour & 0xffffff) != 0 || (blend & 0xff) != 1))
                {
                    var lineWidth = (int)((v[code.LineSize] + 0.5) * scale);
                    AvsDraw.Line(framebuffer, lx, ly, x, y, w, h, pointColour, lineWidth, blend);
                }
            }

            candraw = true;
            lx = x;
            ly = y;
        }

        return 0;
    }

    private static int ToByte(double t) => t <= 0.0 ? 0 : t >= 1.0 ? 255 : (int)(t * 255.0);

    /// <summary>The scope's four pieces of code, compiled the first time it draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(SuperScope scope)
        {
            _context = AvsScript.NewContext(scope.Shared);
            N = _context.Index("n");
            B = _context.Index("b");
            X = _context.Index("x");
            Y = _context.Index("y");
            I = _context.Index("i");
            V = _context.Index("v");
            W = _context.Index("w");
            H = _context.Index("h");
            Red = _context.Index("red");
            Green = _context.Index("green");
            Blue = _context.Index("blue");
            LineSize = _context.Index("linesize");
            Skip = _context.Index("skip");
            DrawMode = _context.Index("drawmode");
            _context.Values[N] = 100.0;
            Point = scope.Compile(_context, scope._code[0], "The SuperScope's per point code");
            Frame = scope.Compile(_context, scope._code[1], "The SuperScope's per frame code");
            Beat = scope.Compile(_context, scope._code[2], "The SuperScope's on beat code");
            Init = scope.Compile(_context, scope._code[3], "The SuperScope's init code");
        }

        public int N { get; }

        public int B { get; }

        public int X { get; }

        public int Y { get; }

        public int I { get; }

        public int V { get; }

        public int W { get; }

        public int H { get; }

        public int Red { get; }

        public int Green { get; }

        public int Blue { get; }

        public int LineSize { get; }

        public int Skip { get; }

        public int DrawMode { get; }

        public EelCode Point { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Init { get; }

        public bool Initialised { get; set; }

        /// <summary>The variables, read fresh each time since compiling can make room for more.</summary>
        public double[] Values => _context.Values;
    }
}
