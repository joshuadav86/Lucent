using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>
/// One effect in an AVS preset (a C_RBASE in AVS): it reads its settings once, then each frame draws
/// onto, or changes, the picture. Pixels are 0x00RRGGBB ints, row by row from the top-left.
/// </summary>
public abstract class AvsEffect
{
    /// <summary><see cref="Render"/>'s answer when the effect left the picture in fbout: the next effect reads it from there.</summary>
    public const int Swapped = 1;

    /// <summary>Added to <see cref="Render"/>'s answer to make the rest of the frame a beat (Custom BPM does).</summary>
    public const int SetBeat = 0x10000000;

    /// <summary>Added to <see cref="Render"/>'s answer to make the rest of the frame not a beat.</summary>
    public const int ClearBeat = 0x20000000;

    private readonly List<string> _warnings = new();

    protected AvsEffect(AvsShared shared)
    {
        Shared = shared;
    }

    /// <summary>What the preset's effects share: the sound, saved buffers, line blend mode, registers.</summary>
    protected internal AvsShared Shared { get; }

    /// <summary>What was left out or couldn't be done, for the error log.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Reads the effect's settings, as AVS's load_config did. Missing settings keep their defaults.</summary>
    public abstract void Load(AvsConfigReader config);

    /// <summary>
    /// Draws one frame. <paramref name="framebuffer"/> holds the picture so far; an effect that makes a
    /// new picture rather than changing it in place writes it to <paramref name="fbout"/> (the same size)
    /// and returns <see cref="Swapped"/>. Otherwise it returns 0.
    /// </summary>
    public abstract int Render(AvsRenderContext context, int[] framebuffer, int[] fbout);

    /// <summary>Called when the preset starts over (coming on screen), so code runs its init again.</summary>
    public virtual void Restart()
    {
    }

    protected void Warn(string warning)
    {
        if (!_warnings.Contains(warning))
        {
            _warnings.Add(warning);
        }
    }

    /// <summary>A fresh set of variables for this effect's code, with AVS's functions (see <see cref="AvsScript.NewContext"/>).</summary>
    protected EelContext NewContext() => AvsScript.NewContext(Shared);

    /// <summary>
    /// Compiles one piece of an effect's code into <paramref name="context"/>. Code that doesn't compile is
    /// left out (and noted) rather than stopping the preset, as AVS carried on without it.
    /// </summary>
    protected internal EelCode Compile(EelContext context, string code, string label)
    {
        var warnings = new List<string>();
        try
        {
            var compiled = EelCompiler.Compile(AvsScript.Clean(code), context, warnings);
            foreach (var warning in warnings)
            {
                Warn($"{label}: {warning}");
            }

            return compiled;
        }
        catch (EelException ex)
        {
            Warn($"{label} didn't compile, so it's left out: {ex.Message}");
            return EelCode.Empty(context);
        }
    }
}

/// <summary>What every effect is drawing with this frame.</summary>
public sealed class AvsRenderContext
{
    public AvsRenderContext(AvsShared shared)
    {
        Shared = shared;
    }

    public AvsShared Shared { get; }

    public int Width { get; internal set; }

    public int Height { get; internal set; }

    /// <summary>Whether this frame is on a beat. Effect lists (and Custom BPM) can change it for what follows.</summary>
    public bool IsBeat { get; internal set; }

    /// <summary>The sound this frame.</summary>
    public AvsAudio Audio => Shared.Audio;

    /// <summary>
    /// How much thicker than AVS drew them lines and dots are drawn: 1 at AVS's sizes, more when
    /// Lucent draws presets bigger and sharper than AVS's window was (see <see cref="AvsShared.DotScale"/>).
    /// </summary>
    public float DotScale => Shared.DotScale;

    /// <summary>
    /// Runs <paramref name="row"/> for every row of the picture, on as many cores as are free. Use it
    /// for per-pixel work that only writes to its own rows.
    /// </summary>
    public void ForEachRow(Action<int> row) => ForEachRow(0, Height, row);

    /// <summary>Runs <paramref name="row"/> for rows <paramref name="from"/> to <paramref name="to"/> (not including it), in parallel.</summary>
    public void ForEachRow(int from, int to, Action<int> row)
    {
        var count = to - from;
        if (count <= 0)
        {
            return;
        }

        if (count * Width < 32768 || Shared.Threads <= 1)
        {
            for (var y = from; y < to; y++)
            {
                row(y);
            }

            return;
        }

        // Bands of rows, a few per core, so each task has enough to do.
        var bands = Math.Min(count, Shared.Threads * 4);
        Parallel.For(0, bands, Shared.Parallel, band =>
        {
            var start = from + (int)((long)count * band / bands);
            var end = from + (int)((long)count * (band + 1) / bands);
            for (var y = start; y < end; y++)
            {
                row(y);
            }
        });
    }
}

/// <summary>
/// What every effect in a running preset shares, as AVS kept it in globals: the sound, the line blend
/// mode "Set render mode" changes, the eight saved buffers (each preset has its own, as AVS's root
/// list kept them), and the code's reg00-reg99 and gmegabuf (shared by every AVS preset).
/// </summary>
public sealed class AvsShared
{
    /// <summary>NBUF: how many buffers Buffer Save and effect lists can keep pictures in.</summary>
    public const int BufferCount = 8;

    private readonly int[]?[] _buffers = new int[BufferCount][];

    public AvsShared(EelGlobals globals)
    {
        Globals = globals;
        Threads = Math.Clamp(Environment.ProcessorCount - 1, 1, 16);
        Parallel = new ParallelOptions { MaxDegreeOfParallelism = Threads };
    }

    public AvsAudio Audio { get; } = new();

    /// <summary>reg00-reg99 and gmegabuf, shared by all AVS presets (not with MilkDrop's).</summary>
    public EelGlobals Globals { get; }

    /// <summary>
    /// g_line_blend_mode: how dots and lines go onto the picture. Low byte the blend (see <see cref="AvsBlend.Line"/>),
    /// bits 8..15 the adjustable blend's amount, bits 16..23 the line width. Effect lists reset it to 0 for their
    /// effects and put it back after.
    /// </summary>
    public int LineBlendMode { get; set; }

    /// <summary>Line width from <see cref="LineBlendMode"/> (AVS's (g_line_blend_mode&amp;0xff0000)>>16), at least 1.</summary>
    public int LineWidth => Math.Max(1, (LineBlendMode >> 16) & 0xff);

    /// <summary>How much bigger than AVS lines and dots are drawn. Set by the runner from the picture's size.</summary>
    public float DotScale { get; set; } = 1f;

    /// <summary>The folder the preset's file is in, where effects look for the pictures it names (or null).</summary>
    public string? Folder { get; init; }

    /// <summary>The seconds the visualizer has been running, for gettime.</summary>
    public double Time { get; set; }

    /// <summary>How many cores the per-pixel effects use.</summary>
    public int Threads { get; }

    public ParallelOptions Parallel { get; }

    /// <summary>The same random numbers each run, so a preset looks the same from one start to the next.</summary>
    public Random Random { get; } = new(1234);

    /// <summary>
    /// getGlobalBuffer: saved picture <paramref name="index"/> (0-7) at this size, made (black) if it's
    /// missing or the wrong size and <paramref name="allocate"/> is set; otherwise null.
    /// </summary>
    public int[]? Buffer(int index, int width, int height, bool allocate)
    {
        if (index is < 0 or >= BufferCount)
        {
            return null;
        }

        var buffer = _buffers[index];
        if (buffer is null || buffer.Length != width * height)
        {
            if (!allocate)
            {
                return null;
            }

            buffer = new int[width * height];
            _buffers[index] = buffer;
        }

        return buffer;
    }

    /// <summary>Forgets the saved pictures (a new preset starting).</summary>
    public void ClearBuffers() => Array.Clear(_buffers);
}

/// <summary>AVS's flavour of the expression language: ns-eel with AVS's own functions added (avs_eelif.cpp).</summary>
public static class AvsScript
{
    /// <summary>
    /// A fresh set of variables for one effect's code (each AVS effect had its own), with getosc,
    /// getspec, gettime, getkbmouse and setmousepos reading this preset's sound and clock.
    /// </summary>
    public static EelContext NewContext(AvsShared shared)
    {
        var context = new EelContext(shared.Globals);
        context.Functions["getosc"] = (Func<double, double, double, double>)((band, width, channel) => shared.Audio.Get(true, band, width, channel));
        context.Functions["getspec"] = (Func<double, double, double, double>)((band, width, channel) => shared.Audio.Get(false, band, width, channel));

        // gettime(-1) and gettime(-2) asked Winamp for the track's position and length, which presets used for
        // little more than titles; they give 0. Anything else is the seconds since that time.
        context.Functions["gettime"] = (Func<double, double>)(start => start is > -1.001 and < -0.999 or > -2.001 and < -1.999 ? 0.0 : shared.Time - start);

        // The mouse and keyboard aren't the visual's to read in Lucent.
        context.Functions["getkbmouse"] = (Func<double, double>)(_ => 0.0);
        context.Functions["setmousepos"] = (Func<double, double, double>)((_, _) => 0.0);
        return context;
    }

    /// <summary>Code as AVS stored it, made ready for the compiler: stray control characters (other than line breaks and tabs) become spaces.</summary>
    public static string Clean(string code)
    {
        if (code.All(c => c >= ' ' || c is '\r' or '\n' or '\t'))
        {
            return code;
        }

        return new string(code.Select(c => c >= ' ' || c is '\r' or '\n' or '\t' ? c : ' ').ToArray());
    }
}
