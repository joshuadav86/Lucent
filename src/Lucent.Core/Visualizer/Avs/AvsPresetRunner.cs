using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs;

/// <summary>
/// Runs an AVS preset: its main effect list draws each frame into a picture held in memory, as AVS did
/// on the CPU, which each app then puts on screen through the graphics card. AVS drew one frame per
/// call however fast that was; Lucent calls it at most <see cref="FramesPerSecond"/> times a second
/// (presets were tuned for about that), and the picture in between stays as it is.
/// </summary>
public sealed class AvsPresetRunner
{
    /// <summary>How often a preset moves on a frame, so presets run at the same speed on any screen.</summary>
    public const double FramesPerSecond = 60.0;

    private readonly AvsEffectList _root;
    private readonly AvsShared _shared;
    private readonly AvsRenderContext _context;
    private int[] _front = Array.Empty<int>();
    private int[] _back = Array.Empty<int>();
    private double _nextFrame = double.NegativeInfinity;

    /// <summary>
    /// Gets a preset ready. <paramref name="folder"/> is where its file is, where effects look for the
    /// pictures it names. Effects Lucent doesn't have are left out and listed in <see cref="Warnings"/>.
    /// </summary>
    public AvsPresetRunner(AvsPreset preset, EelGlobals globals, string? folder = null)
    {
        Name = preset.Name;
        _shared = new AvsShared(globals) { Folder = folder };
        _context = new AvsRenderContext(_shared);
        _root = new AvsEffectList(_shared, isRoot: true);
        _root.Load(preset.Root);
    }

    public string Name { get; }

    /// <summary>The preset's sound: update it each frame before <see cref="Render"/>.</summary>
    public AvsAudio Audio => _shared.Audio;

    /// <summary>What was left out of the preset, or went wrong in it, for the error log.</summary>
    public IReadOnlyList<string> Warnings => _root.AllWarnings().Distinct().ToList();

    public int Width { get; private set; }

    public int Height { get; private set; }

    /// <summary>The latest picture: <see cref="Width"/> x <see cref="Height"/> pixels, 0x00RRGGBB, from the top-left.</summary>
    public int[] Picture => _front;

    /// <summary>How many frames the preset has drawn since it started, so callers know when the picture changed.</summary>
    public long FrameCount { get; private set; }

    /// <summary>Starts the preset over (as when it comes on screen): code runs its init again and the picture is black.</summary>
    public void Restart()
    {
        _root.Restart();
        _shared.ClearBuffers();
        Array.Clear(_front);
        Array.Clear(_back);
        _nextFrame = double.NegativeInfinity;
        FrameCount = 0;
    }

    /// <summary>
    /// Draws the next frame at <paramref name="width"/> x <paramref name="height"/> if it's time for one
    /// (<paramref name="time"/> in seconds). <paramref name="dotScale"/> makes lines and dots thicker, for
    /// pictures bigger than AVS drew. Returns whether the picture changed.
    /// </summary>
    public bool Render(int width, int height, double time, float dotScale = 1f)
    {
        width = Math.Max(width, 16);
        height = Math.Max(height, 16);
        var resized = width != Width || height != Height;
        if (!resized && time < _nextFrame)
        {
            return false;
        }

        // Steady steps, but never a run of catch-up frames after a pause.
        _nextFrame = Math.Max(_nextFrame + 1.0 / FramesPerSecond, time - 0.5 / FramesPerSecond);
        if (resized)
        {
            Resize(width, height);
        }

        _shared.Time = time;
        _shared.DotScale = Math.Max(1f, dotScale);
        _shared.LineBlendMode = 0;
        _context.Width = width;
        _context.Height = height;
        _context.IsBeat = _shared.Audio.IsBeat;
        if ((_root.Render(_context, _front, _back) & AvsEffect.Swapped) != 0)
        {
            (_front, _back) = (_back, _front);
        }

        FrameCount++;
        return true;
    }

    /// <summary>New picture sizes, keeping what's on screen (stretched), as AVS did when its window changed size.</summary>
    private void Resize(int width, int height)
    {
        var old = _front;
        var oldWidth = Width;
        var oldHeight = Height;
        _front = new int[width * height];
        _back = new int[width * height];
        if (old.Length > 0 && oldWidth > 0 && oldHeight > 0)
        {
            for (var y = 0; y < height; y++)
            {
                var from = y * oldHeight / height * oldWidth;
                for (var x = 0; x < width; x++)
                {
                    _front[y * width + x] = old[from + x * oldWidth / width];
                }
            }
        }

        Width = width;
        Height = height;
    }
}
