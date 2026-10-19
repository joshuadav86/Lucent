using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Color Modifier (r_dcolormod.cpp): code turns each level of red, green and blue (0 to 1) into a
/// new level, building a table that then recolours every pixel.
/// </summary>
[AvsEffect(45)]
public sealed class ColorModifier : AvsEffect
{
    // Level (per value), frame, beat and init code, in AVS's order.
    private readonly string[] _code = { string.Empty, string.Empty, string.Empty, string.Empty };
    private readonly byte[] _table = new byte[768];
    private int _recompute = 1;
    private bool _tableValid;
    private Compiled? _compiled;

    public ColorModifier(AvsShared shared)
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

        _recompute = config.ReadInt(_recompute);
        _tableValid = false;
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
        code.Values[code.Beat] = context.IsBeat ? 1.0 : 0.0;
        if (!code.Initialised)
        {
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.OnBeat.Run();
        }

        if (_recompute != 0 || !_tableValid)
        {
            // The code isn't thread-safe, so the table is built here, then used on every row in parallel.
            for (var x = 0; x < 256; x++)
            {
                var v = code.Values;
                v[code.Red] = v[code.Green] = v[code.Blue] = x / 255.0;
                code.Level.Run();
                v = code.Values;
                _table[512 + x] = ToByte(v[code.Red]);
                _table[256 + x] = ToByte(v[code.Green]);
                _table[x] = ToByte(v[code.Blue]);
            }

            _tableValid = true;
        }

        var table = _table;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                framebuffer[at] = table[c & 0xff] | (table[256 + ((c >> 8) & 0xff)] << 8) | (table[512 + ((c >> 16) & 0xff)] << 16);
            }
        });
        return 0;
    }

    // (int)(v * 255 + 0.5), held to 0..255.
    private static byte ToByte(double v)
    {
        var scaled = v * 255.0 + 0.5;
        return scaled >= 255.0 ? (byte)255 : scaled >= 1.0 ? (byte)(int)scaled : (byte)0;
    }

    /// <summary>The four pieces of code, compiled the first time the effect draws, and their variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(ColorModifier effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            Red = _context.Index("red");
            Green = _context.Index("green");
            Blue = _context.Index("blue");
            Beat = _context.Index("beat");
            Level = effect.Compile(_context, effect._code[0], "The Color Modifier's level code");
            Frame = effect.Compile(_context, effect._code[1], "The Color Modifier's per frame code");
            OnBeat = effect.Compile(_context, effect._code[2], "The Color Modifier's on beat code");
            Init = effect.Compile(_context, effect._code[3], "The Color Modifier's init code");
        }

        public int Red { get; }

        public int Green { get; }

        public int Blue { get; }

        public int Beat { get; }

        public EelCode Level { get; }

        public EelCode Frame { get; }

        public EelCode OnBeat { get; }

        public EelCode Init { get; }

        public bool Initialised { get; set; }

        /// <summary>The variables, read fresh each time since compiling can make room for more.</summary>
        public double[] Values => _context.Values;
    }
}
