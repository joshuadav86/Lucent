using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Drawing without a window, for recording a video: the caller asks for each frame in turn, at
/// whatever time it likes, so frames come out evenly spaced however long each takes to draw.
/// Everything happens on the caller's thread, on a graphics device of the renderer's own, so the
/// visuals on screen carry on undisturbed.
/// </summary>
public sealed partial class Renderer
{
    private ID3D11Texture2D? _offscreenOutput;
    private ID3D11RenderTargetView? _offscreenOutputView;
    private BeatMap? _offscreenBeats;
    private double _offscreenPosition;

    /// <summary>
    /// A renderer with no window. <paramref name="presets"/> is the list the visuals window uses, so
    /// visuals are numbered the same way. Call <see cref="OpenOffscreen"/>, then <see cref="DrawOffscreen"/>
    /// for each frame, then <see cref="Dispose"/>, all on the same thread.
    /// </summary>
    public static Renderer CreateOffscreen(AudioAnalyzer analyzer, IReadOnlyList<PresetSource> presets) =>
        new(IntPtr.Zero, analyzer, presets);

    /// <summary>The graphics device frames are drawn with, once <see cref="OpenOffscreen"/> has run.</summary>
    public ID3D11Device Device => _device ?? throw new InvalidOperationException("The renderer isn't open.");

    /// <summary>The device's context, for copying frames. Use it only on the drawing thread.</summary>
    public ID3D11DeviceContext Context => _context ?? throw new InvalidOperationException("The renderer isn't open.");

    /// <summary>The song's bars, so Auto lands each change on a downbeat, as it does on screen. Null if they aren't known.</summary>
    public BeatMap? SongBeats
    {
        get => _offscreenBeats;
        set => _offscreenBeats = value;
    }

    /// <summary>
    /// Sets up the graphics card to draw <paramref name="width"/> x <paramref name="height"/> frames,
    /// and compiles the visuals in <paramref name="presets"/> (from the shader cache, usually, since the
    /// visuals window has compiled them already). <paramref name="compiled"/> is told how many are done
    /// out of how many. Visuals that don't compile are left out; if none of them does, this throws.
    /// </summary>
    public void OpenOffscreen(int width, int height, IReadOnlyCollection<int> presets, Action<int, int>? compiled,
        CancellationToken cancellation)
    {
        if (!_offscreen)
        {
            throw new InvalidOperationException("This renderer draws into a window.");
        }

        CreateDevice();
        CreateResources();
        CompileEngineShaders();

        var device = _device!;
        var shaders = ReadEngineSources();
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
            try
            {
                BuildShaders(device, shaders, slot);
                Volatile.Write(ref slot.State, PresetSlot.Ready);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref slot.State, PresetSlot.Failed);
                lock (errors)
                {
                    errors.Add($"{slot.Source.Name}: {ex.Message}");
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

        _windowWidth = width;
        _windowHeight = height;
        var (renderWidth, renderHeight) = RenderSizeFor(width, height);
        ResizeRenderTargets(renderWidth, renderHeight);

        // The finished frame, in the same format the window's back buffer has in standard range.
        _offscreenOutput = device.CreateTexture2D(
            Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, null,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceOptionFlags.None,
            ResourceUsage.Default, CpuAccessFlags.None);
        _offscreenOutputView = device.CreateRenderTargetView(_offscreenOutput, null);
    }

    /// <summary>
    /// Draws the next frame, <paramref name="deltaTime"/> seconds after the last, from the audio the
    /// analyzer was last updated with. <paramref name="now"/> is the visuals' clock (it only ever moves on)
    /// and <paramref name="songPosition"/> how far into the song the frame is. Returns the finished frame,
    /// which stays the renderer's: it's drawn over by the next call.
    /// </summary>
    public ID3D11Texture2D DrawOffscreen(double now, float deltaTime, double songPosition)
    {
        var output = _offscreenOutputView ?? throw new InvalidOperationException("The renderer isn't open.");
        _offscreenPosition = songPosition;
        var progress = UpdatePresetChoice(now);
        if (_activePreset < 0)
        {
            _context!.ClearRenderTargetView(output, new Color4(0f, 0f, 0f, 1f));
        }
        else
        {
            DrawFrame(now, deltaTime, progress, output);
        }

        return _offscreenOutput!;
    }

    private void ReleaseOffscreenOutput()
    {
        _offscreenOutputView?.Dispose();
        _offscreenOutputView = null;
        _offscreenOutput?.Dispose();
        _offscreenOutput = null;
    }
}
