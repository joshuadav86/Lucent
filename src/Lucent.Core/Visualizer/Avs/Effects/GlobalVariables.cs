using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Misc / Global Variables (Jheriko's "Global" APE, e_globalvariables.cpp in the modern fork): init, frame
/// and beat code that draws nothing but sets up reg00-reg99 and gmegabuf for the effects after it.
/// </summary>
[AvsEffect("Jheriko: Global")]
public sealed class GlobalVariables : AvsEffect
{
    private string _init = string.Empty;
    private string _frame = string.Empty;
    private string _beat = string.Empty;
    private Compiled? _compiled;

    public GlobalVariables(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        var loadTime = config.ReadInt();

        // 24 bytes the APE saved but never used.
        config.Position = Math.Min(config.Length, 28);
        _init = ReadCode(config);
        _frame = ReadCode(config);
        _beat = ReadCode(config);
        var file = ReadCode(config);
        if (file.Length > 0)
        {
            Warn($"Global Variables can't load or save its file of values (\"{file}\") in Lucent, so the code starts without them.");
        }
        else if (loadTime != 0)
        {
            Warn("Global Variables can't load or save files of values in Lucent, so the code starts without them.");
        }
    }

    private static string ReadCode(AvsConfigReader config) =>
        config.Remaining > 0 ? config.ReadTerminatedString() : string.Empty;

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

        // w, h and b are set, and load and save start at 0, every frame.
        var v = code.Values;
        v[code.W] = context.Width;
        v[code.H] = context.Height;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;
        v[code.Load] = 0.0;
        v[code.Save] = 0.0;
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

        return 0;
    }

    /// <summary>The three pieces of code, compiled the first time the effect runs, and their variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(GlobalVariables effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            W = _context.Index("w");
            H = _context.Index("h");
            B = _context.Index("b");
            Load = _context.Index("load");
            Save = _context.Index("save");
            Init = effect.Compile(_context, effect._init, "Global Variables' init code");
            Frame = effect.Compile(_context, effect._frame, "Global Variables' frame code");
            Beat = effect.Compile(_context, effect._beat, "Global Variables' beat code");
        }

        public int W { get; }

        public int H { get; }

        public int B { get; }

        public int Load { get; }

        public int Save { get; }

        public EelCode Init { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public bool Initialised { get; set; }

        /// <summary>The variables, read fresh each time since compiling can make room for more.</summary>
        public double[] Values => _context.Values;
    }
}
