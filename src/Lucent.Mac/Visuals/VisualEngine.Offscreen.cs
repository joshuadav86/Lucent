using Lucent.Mac.Interop;
using SharpMetal.Metal;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Drawing without a window, for recording a video, like the Windows renderer's Renderer.Offscreen.cs:
/// the caller asks for each frame in turn, at whatever time it likes, so frames come out evenly spaced
/// however long each takes to draw. The caller also hands the analyzer each frame's sound. Everything
/// happens on the caller's thread, in an engine (and command queue) of the recorder's own, so the
/// visuals on screen carry on undisturbed.
/// </summary>
public sealed partial class VisualEngine
{
    private bool _offscreen;
    private MTLTexture? _offscreenOutput;
    private BeatMap? _offscreenBeats;
    private double _offscreenPosition;

    /// <summary>
    /// An engine with no window. <paramref name="presets"/> is the list the visuals window uses, so
    /// visuals are numbered the same way. Call <see cref="OpenOffscreen"/>, then <see cref="DrawOffscreen"/>
    /// for each frame, then <see cref="Dispose"/>, all on the same thread.
    /// </summary>
    public static VisualEngine CreateOffscreen(MTLDevice device, AudioAnalyzer analyzer, MetalShaderFiles files,
        IReadOnlyList<PresetSource> presets) =>
        new(device, analyzer, files, MTLPixelFormat.BGRA8Unorm, presets) { _offscreen = true };

    /// <summary>The song's bars, so Auto lands each change on a downbeat, as it does on screen. Null if they aren't known.</summary>
    public BeatMap? SongBeats
    {
        get => _offscreenBeats;
        set => _offscreenBeats = value;
    }

    /// <summary>
    /// The finished frame, once <see cref="OpenOffscreen"/> has run: BGRA, 8 bits a channel, in standard
    /// range as the window shows it. It's drawn over by each frame.
    /// </summary>
    public MTLTexture OffscreenOutput => _offscreenOutput ?? throw new InvalidOperationException("The engine isn't open.");

    /// <summary>
    /// Gets ready to draw <paramref name="width"/> x <paramref name="height"/> frames, and compiles the
    /// visuals in <paramref name="presets"/> (from the translation cache, usually, since the visuals window
    /// has translated them already). <paramref name="compiled"/> is told how many are done out of how many.
    /// Visuals that don't compile are left out (<see cref="PresetFailed"/> says which and why); if none of
    /// them does, this throws.
    /// </summary>
    public void OpenOffscreen(int width, int height, IReadOnlyCollection<int> presets, Action<int, int>? compiled,
        CancellationToken cancellation)
    {
        if (!_offscreen)
        {
            throw new InvalidOperationException("This engine draws into a window.");
        }

        if (_slots.Length == 0)
        {
            throw new InvalidOperationException("There's no visual to draw.");
        }

        var wanted = presets.Where(index => index >= 0 && index < _slots.Length).Distinct().ToList();
        var done = 0;
        var errors = new List<string>();
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 4),
            CancellationToken = cancellation,
        };
        Parallel.ForEach(wanted, options, index =>
        {
            var slot = _slots[index];
            if (Interlocked.CompareExchange(ref slot.State, PresetSlot.Compiling, PresetSlot.Pending) == PresetSlot.Pending
                && CompilePreset(slot) is { } problem)
            {
                lock (errors)
                {
                    errors.Add(problem);
                }
            }

            compiled?.Invoke(Interlocked.Increment(ref done), wanted.Count);
        });

        // Start on the visual asked for, or the first that works if it doesn't.
        var start = Math.Clamp(_requestedPreset, 0, _slots.Length - 1);
        if (!IsReady(start))
        {
            start = wanted.FirstOrDefault(IsReady, -1);
            if (start < 0)
            {
                throw new InvalidOperationException(errors.Count > 0 ? $"The visual didn't compile. {errors[0]}" : "There's no visual to draw.");
            }

            _requestedPreset = start;
        }

        // The finished frame, in the same format the window's layer has in standard range. Only the
        // graphics card touches it: frames are copied out of it to be read back (see VideoFrameReader).
        using var pool = AutoreleasePool.Begin();
        ReleaseOffscreenOutput();
        var description = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, (ulong)width, (ulong)height, false);
        description.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
        description.StorageMode = MTLStorageMode.Private;
        _offscreenOutput = _device.NewTexture(description);
    }

    /// <summary>
    /// Encodes the next frame, <paramref name="deltaTime"/> seconds after the last, from the audio the
    /// analyzer was last updated with. <paramref name="now"/> is the visuals' clock (it only ever moves on)
    /// and <paramref name="songPosition"/> how far into the song the frame is. The frame ends in
    /// <see cref="OffscreenOutput"/>; the command buffer comes back for the caller to add to and commit.
    /// </summary>
    public MTLCommandBuffer DrawOffscreen(double now, float deltaTime, double songPosition)
    {
        var output = _offscreenOutput ?? throw new InvalidOperationException("The engine isn't open.");
        _offscreenPosition = songPosition;
        return EncodeFrame(output, now, deltaTime);
    }

    /// <summary>The song's bars and where it is now, for Auto: from the player on screen, or from the recording off screen.</summary>
    private (BeatMap Beats, double Position)? CurrentSong()
    {
        if (!_offscreen)
        {
            return SongClock?.Read();
        }

        return _offscreenBeats is { } beats ? (beats, _offscreenPosition) : null;
    }

    private void ReleaseOffscreenOutput()
    {
        _offscreenOutput?.Dispose();
        _offscreenOutput = null;
    }
}
