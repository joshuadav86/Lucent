using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>
/// An effect list (r_list.cpp), and the preset's main list. Runs its effects in order on a picture of
/// its own, which it blends in from the picture so far and back out again in one of AVS's ways (replace,
/// 50/50, maximum, additive, subtractive, every other line, every other pixel, XOR, adjustable, multiply,
/// a saved buffer's brightness, minimum). It can be switched on for a few frames on each beat, and from
/// AVS 2.8 on run code that sets its blending, enabling and clearing each frame.
/// </summary>
public sealed class AvsEffectList : AvsEffect
{
    private const string CodeSignature = "AVS 2.8+ Effect List Config";
    private const int MaxDepth = 64;

    private readonly bool _isRoot;
    private readonly int _depth;
    private readonly List<AvsEffect> _effects = new();
    private int _mode;
    private int _inBlendValue = 128;
    private int _outBlendValue = 128;
    private int _bufferIn;
    private int _bufferOut;
    private int _inInvert;
    private int _outInvert;
    private int _beatRender;
    private int _beatRenderFrames = 1;
    private int _fakeEnabled;
    private string _initCode = string.Empty;
    private string _frameCode = string.Empty;
    private bool _useCode;
    private int[]? _picture;
    private CodeState? _code;
    private HashSet<AvsEffect>? _broken;

    public AvsEffectList(AvsShared shared, bool isRoot, int depth = 0)
        : base(shared)
    {
        _isRoot = isRoot;
        _depth = depth;
    }

    /// <summary>The effects in the list, in the order they run.</summary>
    public IReadOnlyList<AvsEffect> Effects => _effects;

    /// <summary>Whether the main list clears the picture each frame ("Clear every frame").</summary>
    private bool ClearsEveryFrame => (_mode & 1) != 0;

    private int BlendIn => (_mode >> 8) & 31;

    private int BlendOut => ((_mode >> 16) & 31) ^ 1;

    private bool Enabled => ((_mode & 2) ^ 2) != 0 || _fakeEnabled > 0;

    /// <summary>
    /// The effects saved in a list's settings, in order, after its header (the mode, and for nested lists
    /// AVS 2.8's extra blend settings). The entry holding the list's own code is among them.
    /// </summary>
    public static IEnumerable<AvsEffectEntry> Entries(AvsConfigReader list)
    {
        var position = ReadHeader(list, out _, out _);
        var entries = new List<AvsEffectEntry>();
        while (position + 4 <= list.Length)
        {
            list.Position = position;
            var id = list.ReadInt();
            string? ape = null;
            if (id >= AvsEffects.ApeId && id != AvsEffects.ListId)
            {
                if (list.Remaining < 32)
                {
                    break;
                }

                ape = list.ReadFixedString(32);
                id = AvsEffects.ApeId;
            }

            if (!list.HasInt)
            {
                break;
            }

            var length = list.ReadInt();
            if (length < 0 || length > list.Remaining)
            {
                break;
            }

            entries.Add(new AvsEffectEntry(id, ape, list.Slice(list.Position, length)));
            position = list.Position + length;
        }

        return entries;
    }

    /// <summary>Reads the mode and the extra settings' size, returning where the effects start.</summary>
    private static int ReadHeader(AvsConfigReader list, out int mode, out int extendedEnd)
    {
        list.Position = 0;
        mode = list.ReadByte();
        if ((mode & 0x80) != 0)
        {
            mode &= ~0x80;
            mode |= list.ReadInt();
        }

        // The top byte of the mode is the size of the extra settings, plus 4 ("cause we fucked up", says AVS).
        extendedEnd = ((mode >> 24) & 0xff) + 5;
        if (extendedEnd > 5)
        {
            // Six numbers while there's room, then two more that only count with 4 bytes to spare.
            var position = list.Position;
            var count = 0;
            while (count < 6 && position < extendedEnd)
            {
                position += 4;
                count++;
            }

            while (count < 8 && position < extendedEnd - 4)
            {
                position += 4;
                count++;
            }

            return position;
        }

        return list.Position;
    }

    public override void Load(AvsConfigReader config)
    {
        ReadHeader(config, out _mode, out var extendedEnd);
        config.Position = (config[0] & 0x80) != 0 ? 5 : 1;
        if (extendedEnd > 5)
        {
            if (config.Position < extendedEnd) { _inBlendValue = config.ReadInt(_inBlendValue); }
            if (config.Position < extendedEnd) { _outBlendValue = config.ReadInt(_outBlendValue); }
            if (config.Position < extendedEnd) { _bufferIn = config.ReadInt(_bufferIn); }
            if (config.Position < extendedEnd) { _bufferOut = config.ReadInt(_bufferOut); }
            if (config.Position < extendedEnd) { _inInvert = config.ReadInt(_inInvert); }
            if (config.Position < extendedEnd) { _outInvert = config.ReadInt(_outInvert); }
            if (config.Position < extendedEnd - 4) { _beatRender = config.ReadInt(_beatRender); }
            if (config.Position < extendedEnd - 4) { _beatRenderFrames = config.ReadInt(_beatRenderFrames); }
        }

        foreach (var entry in Entries(config))
        {
            if (extendedEnd > 5 && entry.Id == AvsEffects.ApeId && entry.Ape == CodeSignature)
            {
                var code = entry.Config;
                if (code.HasInt)
                {
                    _useCode = code.ReadInt() != 0;
                    _initCode = code.ReadString();
                    _frameCode = code.ReadString();
                }

                continue;
            }

            if (entry.Id == AvsEffects.ListId && _depth >= MaxDepth)
            {
                Warn("Effect lists nested too deep were left out.");
                continue;
            }

            var effect = AvsEffects.Create(entry.Id, entry.Ape, Shared, _depth + 1);
            if (effect is null)
            {
                Warn($"{AvsEffects.NameOf(entry.Id, entry.Ape)} isn't available in Lucent, so it's left out.");
                continue;
            }

            effect.Load(entry.Config);
            _effects.Add(effect);
        }
    }

    /// <summary>Every warning from this list and the effects in it.</summary>
    public IEnumerable<string> AllWarnings() =>
        Warnings.Concat(_effects.SelectMany(effect => effect is AvsEffectList list ? list.AllWarnings() : effect.Warnings));

    public override void Restart()
    {
        _picture = null;
        _fakeEnabled = 0;
        if (_code is not null)
        {
            _code.Initialised = false;
        }

        foreach (var effect in _effects)
        {
            effect.Restart();
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var w = context.Width;
        var h = context.Height;
        if (context.IsBeat && _beatRender != 0)
        {
            _fakeEnabled = _beatRenderFrames;
        }

        var enabled = Enabled;
        var inBlendValue = _inBlendValue;
        var outBlendValue = _outBlendValue;
        var clear = ClearsEveryFrame;

        if (!_isRoot && _useCode)
        {
            var code = _code ??= new CodeState(this);
            code.Values[code.Beat] = context.IsBeat ? 1.0 : 0.0;
            code.Values[code.Enabled] = enabled ? 1.0 : 0.0;
            code.Values[code.W] = w;
            code.Values[code.H] = h;
            code.Values[code.Clear] = clear ? 1.0 : 0.0;
            code.Values[code.AlphaIn] = inBlendValue / 255.0;
            code.Values[code.AlphaOut] = outBlendValue / 255.0;
            if (!code.Initialised)
            {
                code.Init.Run();
                code.Initialised = true;
            }

            code.Frame.Run();
            var values = code.Values;
            context.IsBeat = Truth(values[code.Beat]);
            inBlendValue = Math.Clamp((int)(values[code.AlphaIn] * 255.0), 0, 255);
            outBlendValue = Math.Clamp((int)(values[code.AlphaOut] * 255.0), 0, 255);
            enabled = Truth(values[code.Enabled]);
            clear = Truth(values[code.Clear]);
        }

        if (_isRoot || (enabled && BlendIn == 1 && BlendOut == 1))
        {
            // The main list, or a list that replaces in and out: its effects work on the picture itself.
            _picture = null;
            if (clear && (_isRoot || BlendIn != 1))
            {
                Array.Clear(framebuffer, 0, w * h);
            }

            var swapped = RunEffects(context, framebuffer, fbout);
            _fakeEnabled--;
            return swapped ? Swapped : 0;
        }

        if (!enabled)
        {
            _picture = null;
            return 0;
        }

        _fakeEnabled--;
        if (_picture is null || _picture.Length != w * h)
        {
            _picture = new int[w * h];
        }
        else if (clear)
        {
            Array.Clear(_picture);
        }

        var own = _picture;
        var blendIn = BlendIn == 10 && inBlendValue >= 255 ? 1 : BlendIn;
        Blend(context, blendIn, own, framebuffer, inBlendValue, _bufferIn, _inInvert);

        var ownSwapped = RunEffects(context, own, fbout);
        if (ownSwapped)
        {
            Array.Copy(fbout, own, w * h);
        }

        var blendOut = BlendOut == 10 && outBlendValue >= 255 ? 1 : BlendOut;
        if (blendOut == 1 && ownSwapped)
        {
            // Replacing: the list's picture is already in fbout, so that's the picture from here on.
            return Swapped;
        }

        Blend(context, blendOut, framebuffer, own, outBlendValue, _bufferOut, _outInvert);
        return 0;
    }

    /// <summary>Runs the effects in turn, each on the other's output when one swaps. True if the result ends up in fbout.</summary>
    private bool RunEffects(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var lineBlendMode = Shared.LineBlendMode;
        Shared.LineBlendMode = 0;
        var swapped = false;
        foreach (var effect in _effects)
        {
            if (_broken?.Contains(effect) == true)
            {
                continue;
            }

            int result;
            try
            {
                result = effect.Render(context, swapped ? fbout : framebuffer, swapped ? framebuffer : fbout);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && effect is not AvsEffectList)
            {
                // As AVS caught a crashing effect and carried on without it, an effect that goes wrong is
                // dropped from the preset and the rest keep drawing.
                (_broken ??= new HashSet<AvsEffect>()).Add(effect);
                Warn($"{effect.GetType().Name} went wrong while drawing, so it's left out: {ex.Message}");
                continue;
            }

            if ((result & Swapped) != 0)
            {
                swapped = !swapped;
            }

            if ((result & SetBeat) != 0)
            {
                context.IsBeat = true;
            }

            if ((result & ClearBeat) != 0)
            {
                context.IsBeat = false;
            }
        }

        Shared.LineBlendMode = lineBlendMode;
        return swapped;
    }

    /// <summary>Puts <paramref name="source"/> onto <paramref name="target"/> in one of the list's blend modes (r_list.cpp's two switches).</summary>
    private void Blend(AvsRenderContext context, int mode, int[] target, int[] source, int amount, int buffer, int invert)
    {
        var w = context.Width;
        var h = context.Height;
        var count = w * h;
        switch (mode)
        {
            case 1:
                Array.Copy(source, target, count);
                break;
            case 2:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Average(t, o), 0);
                break;
            case 3:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Max(o, t), 0);
                break;
            case 4:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Add(t, o), 0);
                break;
            case 5:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Subtract(o, t), 0);
                break;
            case 6:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Subtract(t, o), 0);
                break;
            case 7:
                // Every other line.
                for (var y = 0; y < h / 2 * 2; y += 2)
                {
                    Array.Copy(source, y * w, target, y * w, w);
                }

                break;
            case 8:
                // Every other pixel, alternating from line to line.
                for (var y = 0; y < h; y++)
                {
                    var row = y * w;
                    for (int x = y & 1, n = 0; n < w / 2; n++, x += 2)
                    {
                        target[row + x] = source[row + x];
                    }
                }

                break;
            case 9:
                PerPixel(context, target, source, static (o, t, _) => o ^ t, 0);
                break;
            case 10:
                PerPixel(context, target, source, static (o, t, v) => AvsBlend.Adjust(t, o, v), amount);
                break;
            case 11:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Multiply(t, o), 0);
                break;
            case 12:
            {
                // Adjustable by a saved buffer's brightness, pixel by pixel.
                var depth = Shared.Buffer(buffer, w, h, allocate: false);
                if (depth is null)
                {
                    break;
                }

                var inverted = invert != 0;
                context.ForEachRow(y =>
                {
                    for (int at = y * w, end = at + w; at < end; at++)
                    {
                        var c = depth[at];
                        var r = Math.Max(Math.Max(c & 0xff, (c >> 8) & 0xff), (c >> 16) & 0xff);
                        target[at] = AvsBlend.Adjust(source[at], target[at], inverted ? 255 - r : r);
                    }
                });
                break;
            }

            case 13:
                PerPixel(context, target, source, static (o, t, _) => AvsBlend.Min(o, t), 0);
                break;
        }
    }

    private static void PerPixel(AvsRenderContext context, int[] target, int[] source, Func<int, int, int, int> blend, int amount)
    {
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                target[at] = blend(target[at], source[at], amount);
            }
        });
    }

    private static bool Truth(double value) => value is > 0.1 or < -0.1;

    /// <summary>A list's code, compiled the first time it runs, with the variables it talks through.</summary>
    private sealed class CodeState
    {
        private readonly EelContext _context;

        public CodeState(AvsEffectList list)
        {
            _context = AvsScript.NewContext(list.Shared);
            Beat = _context.Index("beat");
            AlphaIn = _context.Index("alphain");
            AlphaOut = _context.Index("alphaout");
            Enabled = _context.Index("enabled");
            Clear = _context.Index("clear");
            W = _context.Index("w");
            H = _context.Index("h");
            Init = list.Compile(_context, list._initCode, "The effect list's init code");
            Frame = list.Compile(_context, list._frameCode, "The effect list's frame code");
        }

        public int Beat { get; }

        public int AlphaIn { get; }

        public int AlphaOut { get; }

        public int Enabled { get; }

        public int Clear { get; }

        public int W { get; }

        public int H { get; }

        public EelCode Init { get; }

        public EelCode Frame { get; }

        public bool Initialised { get; set; }

        /// <summary>The variables, read fresh each time since compiling can make room for more.</summary>
        public double[] Values => _context.Values;
    }
}
