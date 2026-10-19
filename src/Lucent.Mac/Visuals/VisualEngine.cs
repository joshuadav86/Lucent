using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using Lucent.Mac.Visualizer;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.Avs;

namespace Lucent.Mac.Visuals;

/// <summary>
/// The Mac version of the Windows app's Renderer: the same passes, with Metal in place of
/// Direct3D 11. Each frame the current visual reads the previous frame (feedback) and draws on top
/// of it; mid-change, the old and new visuals both draw and the engine's blend shader mixes them.
/// Above window resolution the result is shrunk to the window (supersampling), then it's bloomed
/// and tone-mapped onto the output. Particle visuals move their particles with a compute shader and
/// draw them as glowing dots into a layer before their own pass. Winamp AVS presets draw on the CPU
/// (<see cref="AvsVisual"/>); their picture is uploaded each frame and shown by Shaders/AvsDisplay.hlsl
/// as if it were a visual of Lucent's own. Visuals compile in the background,
/// so the first one appears straight away. Given a half-float output it draws in HDR (EDR), with
/// normal white at 1.0 and highlights reaching up to the screen's peak, as the Windows renderer does
/// on HDR screens.
/// </summary>
/// <remarks>
/// It draws into whatever texture it's given, so the same code draws into the window (see
/// <see cref="MetalRenderer"/>), into an off-screen picture for the self-test, and frame by frame into
/// a video (see VisualEngine.Offscreen.cs).
/// </remarks>
public sealed partial class VisualEngine : IDisposable
{
    /// <summary>The visuals' own pictures: half-float, so glow and HDR have room above white.</summary>
    internal const MTLPixelFormat FeedbackFormat = MTLPixelFormat.RGBA16Float;
    private const int ConstantCount = 24;
    private const int ParticleStride = 48;
    private const int MaxParticleCount = 4 * 1024 * 1024;
    private const int ParticleGroupSize = 256;
    private const double MaxRenderScale = 4.0;
    private const double MinRenderScale = 0.25;
    private const int MaxTextureSize = 16384;

    // Frames the CPU can get ahead of the GPU. Each has its own copy of the audio texture, which
    // changes every frame, so the CPU never writes one the GPU is still reading.
    private const int FramesInFlight = 3;

    // The same transition styles, and odds, as the Windows renderer (see Engine.hlsl).
    private static readonly int[] AllTransitionStyles = { 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
    private static readonly int[] MorphTransitionStyles = { 4, 5, 6, 7 };

    private readonly MTLDevice _device;
    private readonly MTLCommandQueue _queue;
    private readonly AudioAnalyzer _analyzer;
    private readonly MetalShaderFiles _files;
    private readonly Dictionary<MTLPixelFormat, MTLRenderPipelineState> _presentPipelines = new();
    private readonly object _displayLock = new();
    private readonly PresetSlot[] _slots;
    private readonly List<Thread> _compileThreads = new();
    private readonly float[] _constants = new float[ConstantCount];
    private readonly Random _random = new();
    private readonly MTLTexture[] _audioTextures = new MTLTexture[FramesInFlight];
    private readonly IntPtr[] _inFlight = new IntPtr[FramesInFlight];
    private readonly RenderTarget?[] _feedback = new RenderTarget?[2];
    private readonly RenderTarget?[] _transitionTargets = new RenderTarget?[2];
    private readonly List<MTLLibrary> _engineLibraries = new();
    private readonly MilkDropRenderer? _milkDrop;
    private readonly string? _milkDropProblem;
    private MTLFunction _vertexFunction;
    private MTLFunction _spriteFunction;
    private MTLFunction _presentFunction;
    private MTLRenderPipelineState _blendPipeline;
    private MTLRenderPipelineState _downsamplePipeline;
    private MTLSamplerState _sampler;
    private RenderTarget? _windowTarget;
    private RenderTarget? _particleLayer;
    private volatile bool _stopCompiling;
    private volatile int _requestedPreset;
    private volatile bool _autoCycle = true;
    private volatile int _targetPixels;
    private volatile float _quality = 1f;
    private volatile float _bloomStrength = 1f;
    private volatile float _transitionSeconds = 3f;
    private volatile float _autoCycleSeconds = 25f;
    private volatile bool _morphsOnly;
    private volatile int[]? _autoCycleChoices;
    private DisplayInfo _display = DisplayInfo.Standard;
    private int _compilersRunning;
    private int _frame;
    private int _frameSlot;
    private double _frameTime;
    private float _frameDelta;
    private int _current;
    private int _frameCount;
    private int _activePreset = -1;
    private readonly AutoResolution _autoResolution = new();
    private volatile bool _autoResolutionOn;
    private int _fromPreset = -1;
    private double _transitionStart;
    private double _lastSwitch;
    private double? _landing;
    private BeatMap? _landingSong;
    private int _transitionStyle;
    private int _windowWidth;
    private int _windowHeight;
    private int _renderWidth;
    private int _renderHeight;

    /// <summary>
    /// An engine that draws with <paramref name="device"/>. Its visuals are the ones <paramref name="files"/>
    /// finds, or <paramref name="presets"/> if given (another engine's list, so visuals are numbered the same way).
    /// </summary>
    public VisualEngine(MTLDevice device, AudioAnalyzer analyzer, MetalShaderFiles files, MTLPixelFormat outputFormat,
        IReadOnlyList<PresetSource>? presets = null)
    {
        _device = device;
        _analyzer = analyzer;
        _files = files;
        _queue = device.NewCommandQueue();
        _slots = (presets ?? files.LoadPresets()).Select(source => new PresetSlot(source)).ToArray();
        Presets = _slots.Select(slot => slot.Source).ToList();
        PresetNames = Presets.Select(preset => preset.Name).ToList();

        CreateResources();
        CompileEngineShaders();
        PresentPipelineFor(outputFormat);
        if (_slots.Any(slot => slot.Source.IsMilkDrop))
        {
            try
            {
                using var pool = AutoreleasePool.Begin();
                _milkDrop = new MilkDropRenderer(device, files, FramesInFlight);
            }
            catch (Exception ex)
            {
                // Without MilkDrop's own shaders the presets are skipped; Lucent's visuals carry on.
                _milkDropProblem = ex.Message;
                ErrorLog.Write("MilkDrop presets", ex);
            }
        }
    }

    /// <summary>Raised (on a compile thread) when a visual fails to compile; it is skipped.</summary>
    public event Action<string>? PresetFailed;

    /// <summary>Raised (on the drawing thread) when the engine moves to another visual by itself.</summary>
    public event Action<int>? PresetChanged;

    /// <summary>Every visual's file (with its family and tags), in the order <see cref="PresetIndex"/> counts them.</summary>
    public IReadOnlyList<PresetSource> Presets { get; }

    /// <summary>Every visual's name, in the same order as <see cref="Presets"/>.</summary>
    public IReadOnlyList<string> PresetNames { get; }

    public MTLDevice Device => _device;

    public MTLCommandQueue Queue => _queue;

    public int PresetIndex
    {
        get => _requestedPreset;
        set => _requestedPreset = value;
    }

    /// <summary>True when the last frame was drawn in HDR: into a half-float output, in extended linear light.</summary>
    public bool HdrActive { get; private set; }

    /// <summary>The visual on screen now, or -1 before the first one has compiled.</summary>
    public int ActivePreset => _activePreset;

    public bool AutoCycle
    {
        get => _autoCycle;
        set => _autoCycle = value;
    }

    /// <summary>How many pixels to draw, e.g. 3840 x 2160 for 4K, or 0 to match the window.</summary>
    public int TargetPixels
    {
        get => _targetPixels;
        set => _targetPixels = Math.Max(0, value);
    }

    /// <summary>
    /// With <see cref="TargetPixels"/> at 0, draw a visual that can't keep up with the screen at fewer
    /// pixels until it can (the Automatic resolution).
    /// </summary>
    public bool AutoResolution
    {
        get => _autoResolutionOn;
        set => _autoResolutionOn = value;
    }

    /// <summary>Detail level for visuals: 0 low, 1 normal, 2 high, 3 ultra.</summary>
    public float Quality
    {
        get => _quality;
        set => _quality = Math.Clamp(value, 0f, 3f);
    }

    public float BloomStrength
    {
        get => _bloomStrength;
        set => _bloomStrength = Math.Clamp(value, 0f, 3f);
    }

    public float TransitionSeconds
    {
        get => _transitionSeconds;
        set => _transitionSeconds = Math.Clamp(value, 0.25f, 30f);
    }

    public float AutoCycleSeconds
    {
        get => _autoCycleSeconds;
        set => _autoCycleSeconds = Math.Clamp(value, 5f, 3600f);
    }

    public bool MorphTransitionsOnly
    {
        get => _morphsOnly;
        set => _morphsOnly = value;
    }

    /// <summary>The visuals Auto picks from (by index), or null for all of them.</summary>
    public IReadOnlyCollection<int>? AutoCycleChoices
    {
        get => _autoCycleChoices;
        set => _autoCycleChoices = value?.ToArray();
    }

    /// <summary>
    /// The screen the visuals are on: its normal white and peak brightness set how bright HDR
    /// highlights go. Set from the UI thread; read when each frame is drawn.
    /// </summary>
    public DisplayInfo Display
    {
        get
        {
            lock (_displayLock)
            {
                return _display;
            }
        }

        set
        {
            lock (_displayLock)
            {
                _display = value;
            }
        }
    }

    /// <summary>The size the last frame was drawn at (bigger than the output when supersampling).</summary>
    public int RenderWidth => _renderWidth;

    public int RenderHeight => _renderHeight;

    /// <summary>The song that's playing and its bars, so Auto can change visual on a downbeat.</summary>
    public SongClock? SongClock { get; set; }

    public bool IsReady(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Ready;

    public bool HasFailed(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Failed;

    /// <summary>True once every visual has either compiled or failed.</summary>
    public bool CompilingFinished => _slots.All(slot => Volatile.Read(ref slot.State) is PresetSlot.Ready or PresetSlot.Failed);

    // ---- Setting up ----

    private void CreateResources()
    {
        for (var i = 0; i < FramesInFlight; i++)
        {
            var description = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.R32Float, AudioAnalyzer.TextureWidth, 2, false);
            description.Usage = MTLTextureUsage.ShaderRead;
            description.StorageMode = MTLStorageMode.Shared;
            _audioTextures[i] = _device.NewTexture(description);
        }

        var sampler = new MTLSamplerDescriptor
        {
            MinFilter = MTLSamplerMinMagFilter.Linear,
            MagFilter = MTLSamplerMinMagFilter.Linear,
            SAddressMode = MTLSamplerAddressMode.ClampToEdge,
            TAddressMode = MTLSamplerAddressMode.ClampToEdge,
            RAddressMode = MTLSamplerAddressMode.ClampToEdge,
        };
        _sampler = _device.NewSamplerState(sampler);
        sampler.Dispose();
    }

    private void CompileEngineShaders()
    {
        using var pool = AutoreleasePool.Begin();
        _vertexFunction = LoadFunction(_files.Engine("VSMain"), "engine_vertex", _engineLibraries);
        _spriteFunction = LoadFunction(_files.Engine("PSSprite"), "sprite_fragment", _engineLibraries);
        _presentFunction = LoadFunction(_files.Engine("PSPresent"), "engine_present", _engineLibraries);
        _blendPipeline = MakePipeline(_vertexFunction, LoadFunction(_files.Engine("PSBlend"), "engine_blend", _engineLibraries), FeedbackFormat, additive: false);
        _downsamplePipeline = MakePipeline(_vertexFunction, LoadFunction(_files.Engine("PSDownsample"), "engine_downsample", _engineLibraries), FeedbackFormat, additive: false);
    }

    /// <summary>
    /// The last pass, onto the output, for the output's pixel format: 8-bit for a normal screen,
    /// half-float for HDR. Each is made the first time it's needed.
    /// </summary>
    private MTLRenderPipelineState PresentPipelineFor(MTLPixelFormat format)
    {
        if (!_presentPipelines.TryGetValue(format, out var pipeline))
        {
            using var pool = AutoreleasePool.Begin();
            pipeline = MakePipeline(_vertexFunction, _presentFunction, format, additive: false);
            _presentPipelines[format] = pipeline;
        }

        return pipeline;
    }

    private MTLFunction LoadFunction(string source, string name, List<MTLLibrary> libraries) =>
        MetalPipelines.LoadFunction(_device, source, name, libraries);

    private MTLRenderPipelineState MakePipeline(MTLFunction vertex, MTLFunction fragment, MTLPixelFormat format, bool additive) =>
        MetalPipelines.Make(_device, vertex, fragment, format, additive ? MetalBlend.Add : MetalBlend.None);

    private static string Describe(NSError error, string fallback) => MetalPipelines.Describe(error, fallback);

    // ---- Compiling visuals in the background ----

    /// <summary>Starts compiling the visuals, the one asked for first, on a few background threads.</summary>
    public void StartCompiling()
    {
        _stopCompiling = false;
        var count = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _compilersRunning = count;
        for (var i = 0; i < count; i++)
        {
            var thread = new Thread(CompileLoop)
            {
                IsBackground = true,
                Name = "Visual compiler",
                Priority = ThreadPriority.BelowNormal,
            };
            _compileThreads.Add(thread);
            thread.Start();
        }
    }

    private void CompileLoop()
    {
        while (!_stopCompiling)
        {
            var index = ClaimNextPreset();
            if (index < 0)
            {
                break;
            }

            CompilePreset(_slots[index]);
        }

        // The last compiler to finish a full pass tidies away translations of visuals that changed or went.
        if (Interlocked.Decrement(ref _compilersRunning) == 0 && !_stopCompiling)
        {
            _files.RemoveUnusedTranslations();
        }
    }

    /// <summary>
    /// Compiles one visual's pipelines and marks it ready, or failed if it doesn't compile (<see cref="PresetFailed"/>
    /// says why). Returns the reason it failed, or null.
    /// </summary>
    private string? CompilePreset(PresetSlot slot)
    {
        try
        {
            using var pool = AutoreleasePool.Begin();
            if (slot.Source.IsMilkDrop)
            {
                // MilkDrop presets compile their equations, and their shaders if they have them.
                slot.MilkDrop = MilkDropVisual.Compile(
                    _milkDrop ?? throw new InvalidOperationException($"MilkDrop presets can't be drawn: {_milkDropProblem}"), _files, slot.Source);
                Volatile.Write(ref slot.State, PresetSlot.Ready);
                return null;
            }

            if (slot.Source.IsAvs)
            {
                // AVS presets compile their code; every one is shown by the same display shader.
                slot.Avs = AvsVisual.Load(slot.Source);
                var display = LoadFunction(_files.AvsDisplay(), "preset_fragment", slot.Libraries);
                slot.Pipeline = MakePipeline(_vertexFunction, display, FeedbackFormat, additive: false);
                Volatile.Write(ref slot.State, PresetSlot.Ready);
                return null;
            }

            var fragment = LoadFunction(_files.Preset(slot.Source, "PSMain"), "preset_fragment", slot.Libraries);
            slot.Pipeline = MakePipeline(_vertexFunction, fragment, FeedbackFormat, additive: false);
            if (slot.Source.IsParticles)
            {
                var simulate = LoadFunction(_files.Preset(slot.Source, "CSMain"), "particles_simulate", slot.Libraries);
                var error = new NSError(IntPtr.Zero);
                var simulation = _device.NewComputePipelineState(simulate, ref error);
                if (simulation.NativePtr == IntPtr.Zero)
                {
                    throw new InvalidOperationException(Describe(error, "The particle simulation couldn't be made."));
                }

                slot.Simulation = simulation;

                var vertices = LoadFunction(_files.Preset(slot.Source, "VSParticle"), "particles_vertex", slot.Libraries);
                slot.SpritePipeline = MakePipeline(vertices, _spriteFunction, FeedbackFormat, additive: true);
            }

            Volatile.Write(ref slot.State, PresetSlot.Ready);
            return null;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref slot.State, PresetSlot.Failed);
            if (slot.Source.IsCommunityPreset)
            {
                // A broken .milk or .avs file is skipped; the log says which and why.
                ErrorLog.Write($"{KindOf(slot.Source)} {Path.GetFileName(slot.Source.FilePath)} was skipped", ex.Message);
            }

            var message = $"{slot.Source.Name}: {ex.Message}";
            PresetFailed?.Invoke(message);
            return message;
        }
    }

    /// <summary>Picks the visual that's been asked for first, then its neighbours, then the rest in order.</summary>
    private int ClaimNextPreset()
    {
        var count = _slots.Length;
        if (count == 0)
        {
            return -1;
        }

        var requested = Math.Clamp(_requestedPreset, 0, count - 1);
        foreach (var offset in new[] { 0, 1, -1 })
        {
            var index = (requested + offset + count) % count;
            if (TryClaim(index))
            {
                return index;
            }
        }

        for (var index = 0; index < count; index++)
        {
            if (TryClaim(index))
            {
                return index;
            }
        }

        return -1;
    }

    private bool TryClaim(int index) =>
        Interlocked.CompareExchange(ref _slots[index].State, PresetSlot.Compiling, PresetSlot.Pending) == PresetSlot.Pending;

    // ---- Drawing ----

    /// <summary>
    /// Encodes one frame into a new command buffer, ending with the picture in <paramref name="output"/>,
    /// and returns the buffer for the caller to present (if it's a window) and commit.
    /// </summary>
    public MTLCommandBuffer EncodeFrame(MTLTexture output, double now, float deltaTime)
    {
        // Wait for the frame that last used this frame's audio texture to finish on the GPU.
        var slot = _frame % FramesInFlight;
        if (_inFlight[slot] != IntPtr.Zero)
        {
            new MTLCommandBuffer(_inFlight[slot]).WaitUntilCompleted();
            ObjC.Release(_inFlight[slot]);
            _inFlight[slot] = IntPtr.Zero;
        }

        var commands = _queue.CommandBuffer();
        _inFlight[slot] = ObjC.Send(commands.NativePtr, "retain");
        _frame++;
        _frameSlot = slot;
        _frameTime = now;
        _frameDelta = deltaTime;

        var width = (int)output.Width;
        var height = (int)output.Height;
        HdrActive = output.PixelFormat == MTLPixelFormat.RGBA16Float;
        if (width != _windowWidth || height != _windowHeight)
        {
            _windowWidth = width;
            _windowHeight = height;
            _renderWidth = 0;
            _renderHeight = 0;
        }

        var (renderWidth, renderHeight) = RenderSizeFor(width, height);
        if (renderWidth != _renderWidth || renderHeight != _renderHeight)
        {
            ResizeRenderTargets(commands, renderWidth, renderHeight);
        }

        if (!_offscreen)
        {
            // Off screen, the recorder hands the analyzer each frame's sound itself.
            _analyzer.Update(deltaTime);
        }

        var progress = UpdatePresetChoice(now);
        if (_activePreset < 0)
        {
            // Nothing compiled yet: show black until the first visual is ready.
            EndPass(BeginPass(commands, output, clear: true));
            return commands;
        }

        var audio = _audioTextures[slot];
        var data = _analyzer.TextureData;
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            audio.ReplaceRegion(
                new MTLRegion { origin = new MTLOrigin(), size = new MTLSize { width = AudioAnalyzer.TextureWidth, height = 2, depth = 1 } },
                0, handle.AddrOfPinnedObject(), AudioAnalyzer.TextureWidth * sizeof(float));
        }
        finally
        {
            handle.Free();
        }

        var supersampling = _renderWidth > _windowWidth;
        _constants[0] = _renderWidth;
        _constants[1] = _renderHeight;
        _constants[2] = (float)now;
        _constants[3] = deltaTime;
        _constants[4] = _analyzer.Bass;
        _constants[5] = _analyzer.Mid;
        _constants[6] = _analyzer.Treble;
        _constants[7] = _analyzer.Beat;
        _constants[8] = _analyzer.Level;
        _constants[9] = _analyzer.BeatCount;
        _constants[10] = (float)_renderWidth / _renderHeight;
        _constants[11] = _quality;
        _constants[12] = progress;
        _constants[13] = _transitionStyle;
        _constants[14] = _bloomStrength;
        _constants[15] = (float)_renderWidth / _windowWidth;
        var display = Display;
        _constants[16] = HdrActive ? 1f : 0f;
        _constants[17] = display.SdrWhiteNits / 80f;
        _constants[18] = display.PeakNits / 80f;
        _constants[19] = _frameCount++;
        _constants[23] = PlainOutputAmount(progress);

        var target = _feedback[_current]!;
        var previous = _feedback[1 - _current]!;
        if (_fromPreset < 0)
        {
            // Pass 1: the preset draws into the feedback buffer, reading last frame's.
            DrawPreset(commands, audio, target, previous, _activePreset);
        }
        else
        {
            // Mid-transition: run both presets from the same last frame, then blend them.
            DrawPreset(commands, audio, _transitionTargets[0]!, previous, _fromPreset);
            DrawPreset(commands, audio, _transitionTargets[1]!, previous, _activePreset);
            DrawPass(commands, audio, target.Texture, _renderWidth, _renderHeight, _blendPipeline,
                _transitionTargets[0]!.Texture, _transitionTargets[1]!.Texture, null);
        }

        // Pass 2 (only above window resolution): shrink the image to the window.
        var shown = target;
        if (supersampling)
        {
            DrawPass(commands, audio, _windowTarget!.Texture, _windowWidth, _windowHeight, _downsamplePipeline, target.Texture, null, null);
            shown = _windowTarget;
        }

        // Pass 3: bloom and tone-map the result onto the output.
        DrawPass(commands, audio, output, _windowWidth, _windowHeight, PresentPipelineFor(output.PixelFormat), shown.Texture, null, null);
        _current = 1 - _current;
        return commands;
    }

    private void DrawPreset(MTLCommandBuffer commands, MTLTexture audio, RenderTarget destination, RenderTarget source, int presetIndex)
    {
        var slot = _slots[presetIndex];
        if (slot.MilkDrop is { } milkDrop && _milkDrop is not null)
        {
            DrawMilkDrop(commands, destination, presetIndex, milkDrop);
            return;
        }

        if (slot.Avs is { } avs)
        {
            DrawAvs(commands, audio, destination, source, presetIndex, avs);
            return;
        }

        MTLTexture? particleLayer = null;
        if (slot.Simulation is { } simulation)
        {
            RunParticles(commands, audio, slot, simulation, source);
            particleLayer = _particleLayer!.Texture;
        }

        DrawPass(commands, audio, destination.Texture, _renderWidth, _renderHeight, slot.Pipeline, source.Texture, null, particleLayer);
    }

    private void DrawMilkDrop(MTLCommandBuffer commands, RenderTarget destination, int presetIndex, MilkDropVisual milkDrop)
    {
        var slot = _slots[presetIndex];
        if (!milkDrop.NotesLogged)
        {
            // What had to be left out of it, logged when it's first shown rather than for a whole pack at every start.
            milkDrop.NotesLogged = true;
            if (milkDrop.Notes.Count > 0)
            {
                ErrorLog.Write($"MilkDrop preset {Path.GetFileName(slot.Source.FilePath)}", string.Join(Environment.NewLine, milkDrop.Notes));
            }
        }

        if (HasFailed(presetIndex))
        {
            return;
        }

        try
        {
            _milkDrop!.Draw(
                milkDrop, commands, destination.Texture, _renderWidth, _renderHeight, _analyzer,
                _frame, _frameSlot, _frameTime, _frameDelta, _quality, _autoCycleSeconds);
        }
        catch (Exception ex)
        {
            StopCommunityPreset(presetIndex, ex);
        }
    }

    /// <summary>
    /// Moves an AVS preset on by a frame, puts its picture in this frame's texture (each frame in flight has its
    /// own, so the CPU never writes one the GPU is still reading) and shows it with the display shader.
    /// </summary>
    private void DrawAvs(MTLCommandBuffer commands, MTLTexture audio, RenderTarget destination, RenderTarget source, int presetIndex, AvsVisual avs)
    {
        var slot = _slots[presetIndex];
        if (!avs.NotesLogged)
        {
            avs.NotesLogged = true;
            if (avs.Notes.Count > 0)
            {
                ErrorLog.Write($"AVS preset {Path.GetFileName(slot.Source.FilePath)}", string.Join(Environment.NewLine, avs.Notes));
            }
        }

        MTLTexture? picture = null;
        if (!HasFailed(presetIndex))
        {
            try
            {
                var pixels = avs.Draw(_analyzer, _frame, _renderWidth, _renderHeight, _frameTime, _quality, AvsVisual.ModernLook);
                picture = slot.AvsPicture(_device, _frameSlot, avs.Runner.Width, avs.Runner.Height);
                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try
                {
                    picture.Value.ReplaceRegion(
                        new MTLRegion { origin = new MTLOrigin(), size = new MTLSize { width = (ulong)avs.Runner.Width, height = (ulong)avs.Runner.Height, depth = 1 } },
                        0, handle.AddrOfPinnedObject(), (ulong)avs.Runner.Width * sizeof(uint));
                }
                finally
                {
                    handle.Free();
                }
            }
            catch (Exception ex)
            {
                picture = null;
                StopCommunityPreset(presetIndex, ex);
            }
        }

        if (picture is null)
        {
            EndPass(BeginPass(commands, destination.Texture, clear: true));
            return;
        }

        DrawPass(commands, audio, destination.Texture, _renderWidth, _renderHeight, slot.Pipeline, source.Texture, null, picture);
    }

    /// <summary>
    /// A MilkDrop or AVS preset that goes wrong while drawing (rather than while compiling) is dropped for the rest
    /// of the session and the visuals move on, instead of the whole visualizer stopping.
    /// </summary>
    private void StopCommunityPreset(int presetIndex, Exception ex)
    {
        var slot = _slots[presetIndex];
        Volatile.Write(ref slot.State, PresetSlot.Failed);
        ErrorLog.Write($"{KindOf(slot.Source)} {Path.GetFileName(slot.Source.FilePath)} stopped", ex);
        PresetFailed?.Invoke($"{slot.Source.Name}: {ex.Message}");
        var next = NextUsable(presetIndex);
        if (_requestedPreset == presetIndex && next >= 0)
        {
            _requestedPreset = next;
            PresetChanged?.Invoke(next);
        }
    }

    private static string KindOf(PresetSource source) => source.IsAvs ? "AVS preset" : "MilkDrop preset";

    /// <summary>
    /// How much of the picture is a MilkDrop preset's, or an AVS preset's shown as Winamp drew it (1 when one is
    /// showing, part-way during a transition to or from one), which the screen pass shows without Lucent's glow
    /// and tone-mapping.
    /// </summary>
    private float PlainOutputAmount(float progress)
    {
        var to = AvsVisual.ShownPlain(_slots[_activePreset].Source) ? 1f : 0f;
        if (_fromPreset < 0)
        {
            return to;
        }

        var from = AvsVisual.ShownPlain(_slots[_fromPreset].Source) ? 1f : 0f;
        return from + (to - from) * progress;
    }

    /// <summary>
    /// Moves a particle visual's particles on by a frame (compute shader), then draws each one as a
    /// soft dot, all added together into the particle layer that the visual's own pass reads.
    /// </summary>
    private void RunParticles(MTLCommandBuffer commands, MTLTexture audio, PresetSlot slot, MTLComputePipelineState simulation, RenderTarget previous)
    {
        var count = ParticleCountFor(slot.Source);
        if (slot.Particles is null || slot.ParticleCount != count)
        {
            slot.ReleaseParticles();
            slot.Particles = _device.NewBuffer((ulong)count * ParticleStride, MTLResourceOptions.ResourceStorageModePrivate);
            slot.ParticleCount = count;
            slot.ParticlesFresh = true;
        }

        var particles = slot.Particles.Value;
        _constants[20] = count;
        _constants[21] = slot.ParticlesFresh ? 1f : 0f;
        _constants[22] = (float)slot.Source.NormalParticleCount / count;
        slot.ParticlesFresh = false;

        var compute = commands.ComputeCommandEncoder();
        compute.SetComputePipelineState(simulation);
        SetConstants(compute);
        compute.SetBuffer(particles, 0, 2);
        compute.SetTexture(previous.Texture, 0);
        compute.SetTexture(audio, 1);
        compute.SetSamplerState(_sampler, 0);
        compute.DispatchThreadgroups(
            new MTLSize { width = (ulong)(count / ParticleGroupSize), height = 1, depth = 1 },
            new MTLSize { width = ParticleGroupSize, height = 1, depth = 1 });
        compute.EndEncoding();

        var layer = _particleLayer!;
        var encoder = BeginPass(commands, layer.Texture, clear: true, clearAlpha: 0);
        encoder.SetViewport(Viewport(_renderWidth, _renderHeight));
        encoder.SetRenderPipelineState(slot.SpritePipeline);
        encoder.SetCullMode(MTLCullMode.None);
        SetVertexConstants(encoder);
        encoder.SetVertexBuffer(particles, 0, 1);
        encoder.SetVertexTexture(audio, 1);
        encoder.SetVertexSamplerState(_sampler, 0);
        encoder.DrawPrimitives(MTLPrimitiveType.TriangleStrip, 0, 4, (ulong)count);
        EndPass(encoder);
    }

    /// <summary>The visual's own particle count on Normal detail: half on Low, twice on High, four times on Ultra.</summary>
    private int ParticleCountFor(PresetSource source)
    {
        var factor = _quality switch
        {
            < 0.5f => 0.5,
            < 1.5f => 1.0,
            < 2.5f => 2.0,
            _ => 4.0,
        };
        var count = (long)Math.Ceiling(source.NormalParticleCount * factor / ParticleGroupSize) * ParticleGroupSize;
        return (int)Math.Clamp(count, ParticleGroupSize, MaxParticleCount);
    }

    /// <summary>A full-screen pass: one triangle that covers the destination, drawn by <paramref name="pipeline"/>.</summary>
    private void DrawPass(
        MTLCommandBuffer commands, MTLTexture audio, MTLTexture destination, int width, int height, MTLRenderPipelineState pipeline,
        MTLTexture source, MTLTexture? extra, MTLTexture? particleLayer)
    {
        var encoder = BeginPass(commands, destination, clear: false);
        encoder.SetViewport(Viewport(width, height));
        encoder.SetRenderPipelineState(pipeline);
        SetFragmentConstants(encoder);
        encoder.SetFragmentTexture(source, 0);
        encoder.SetFragmentTexture(audio, 1);
        if (extra is { } next)
        {
            encoder.SetFragmentTexture(next, 2);
        }

        if (particleLayer is { } layer)
        {
            encoder.SetFragmentTexture(layer, 3);
        }

        encoder.SetFragmentSamplerState(_sampler, 0);
        encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
        EndPass(encoder);
    }

    private static MTLRenderCommandEncoder BeginPass(MTLCommandBuffer commands, MTLTexture target, bool clear, double clearAlpha = 1)
    {
        var pass = new MTLRenderPassDescriptor();
        var colour = pass.ColorAttachments.Object(0);
        colour.Texture = target;
        // Full-screen passes draw every pixel, so there's nothing to load first.
        colour.LoadAction = clear ? MTLLoadAction.Clear : MTLLoadAction.DontCare;
        colour.StoreAction = MTLStoreAction.Store;
        colour.ClearColor = new MTLClearColor { red = 0, green = 0, blue = 0, alpha = clearAlpha };
        var encoder = commands.RenderCommandEncoder(pass);
        pass.Dispose();
        return encoder;
    }

    private static void EndPass(MTLRenderCommandEncoder encoder) => encoder.EndEncoding();

    private static MTLViewport Viewport(int width, int height) =>
        new() { originX = 0, originY = 0, width = width, height = height, znear = 0, zfar = 1 };

    private unsafe void SetFragmentConstants(MTLRenderCommandEncoder encoder)
    {
        fixed (float* constants = _constants)
        {
            encoder.SetFragmentBytes((IntPtr)constants, ConstantCount * sizeof(float), 0);
        }
    }

    private unsafe void SetVertexConstants(MTLRenderCommandEncoder encoder)
    {
        fixed (float* constants = _constants)
        {
            encoder.SetVertexBytes((IntPtr)constants, ConstantCount * sizeof(float), 0);
        }
    }

    private unsafe void SetConstants(MTLComputeCommandEncoder encoder)
    {
        fixed (float* constants = _constants)
        {
            encoder.SetBytes((IntPtr)constants, ConstantCount * sizeof(float), 0);
        }
    }

    // ---- Choosing visuals (the same rules as the Windows renderer) ----

    /// <summary>
    /// Starts transitions for visual changes (from the UI or auto-cycle) and returns how far
    /// through the current transition we are, 0..1. Visuals that haven't compiled yet are
    /// waited for; ones that failed are skipped.
    /// </summary>
    private float UpdatePresetChoice(double now)
    {
        if (_slots.Length == 0)
        {
            return 1f;
        }

        var requested = Math.Clamp(_requestedPreset, 0, _slots.Length - 1);
        if (HasFailed(requested))
        {
            var fallback = _activePreset >= 0 ? _activePreset : NextUsable(requested);
            if (fallback >= 0 && fallback != requested)
            {
                _requestedPreset = fallback;
                requested = fallback;
                PresetChanged?.Invoke(fallback);
            }
        }

        if (_activePreset < 0)
        {
            if (IsReady(requested))
            {
                // The very first visual appears without a transition.
                _activePreset = requested;
                _lastSwitch = now;
            }

            return 1f;
        }

        if (requested != _activePreset)
        {
            if (IsReady(requested))
            {
                StartTransition(requested, now);
            }
        }
        else if (_autoCycle && _fromPreset < 0)
        {
            var waited = now - _lastSwitch;
            var interval = _autoCycleSeconds;
            if (CurrentSong() is { } song)
            {
                // The song's bars are known: start the change early enough that the new visual is
                // fully in right on a downbeat, preferring the start of a phrase.
                var lead = _transitionSeconds;
                if (_landing is { } planned && (_landingSong != song.Beats || song.Position > planned + 0.25 || planned - song.Position > lead + 30))
                {
                    _landing = null;
                }

                if (_landing is null && waited + lead >= interval)
                {
                    _landing = song.Beats.LandingAfter(song.Position + lead, preferPhraseWithin: 8);
                    _landingSong = song.Beats;
                }

                if ((_landing is { } landing && song.Position >= landing - lead) || waited > interval + 30)
                {
                    AutoSwitch(now);
                }
            }
            else if ((waited > interval && _analyzer.Beat > 0.9f) || waited > interval + 8)
            {
                AutoSwitch(now);
            }
        }

        if (_fromPreset < 0)
        {
            return 1f;
        }

        var t = Math.Clamp((now - _transitionStart) / _transitionSeconds, 0.0, 1.0);
        if (t >= 1.0)
        {
            ReleaseParticles(_fromPreset);
            _fromPreset = -1;
            return 1f;
        }

        return (float)(t * t * (3 - 2 * t));
    }

    private void AutoSwitch(double now)
    {
        var next = RandomReadyPreset();
        if (next >= 0)
        {
            _requestedPreset = next;
            StartTransition(next, now);
            PresetChanged?.Invoke(next);
        }
    }

    private int NextUsable(int start)
    {
        for (var offset = 1; offset < _slots.Length; offset++)
        {
            var index = (start + offset) % _slots.Length;
            if (!HasFailed(index))
            {
                return index;
            }
        }

        return -1;
    }

    private int RandomReadyPreset()
    {
        var ready = new List<int>();
        var choices = _autoCycleChoices;
        for (var index = 0; index < _slots.Length; index++)
        {
            if (index != _activePreset && IsReady(index) && (choices is null || Array.IndexOf(choices, index) >= 0))
            {
                ready.Add(index);
            }
        }

        return ready.Count == 0 ? -1 : ready[_random.Next(ready.Count)];
    }

    private void StartTransition(int presetIndex, double now)
    {
        _landing = null;
        var leaving = _fromPreset;
        _fromPreset = _activePreset;
        _activePreset = presetIndex;
        if (leaving != _fromPreset)
        {
            ReleaseParticles(leaving);
        }

        _transitionStart = now;
        _lastSwitch = now;
        var styles = _morphsOnly ? MorphTransitionStyles : AllTransitionStyles;
        _transitionStyle = styles[_random.Next(styles.Length)];
    }

    /// <summary>Frees a visual's particles (or a MilkDrop or AVS preset's pictures) once it's no longer on screen; they start afresh next time.</summary>
    private void ReleaseParticles(int presetIndex)
    {
        if (presetIndex >= 0 && presetIndex != _activePreset)
        {
            _slots[presetIndex].ReleaseParticles();
            _slots[presetIndex].MilkDrop?.ReleaseImages();
            _slots[presetIndex].ReleaseAvsPictures();
        }
    }

    // ---- Sizes ----

    /// <summary>Call on the render thread once each frame is shown, so the Automatic resolution can watch the frame rate.</summary>
    public void FramePresented(double now)
    {
        if (_autoResolutionOn && _targetPixels <= 0 && _activePreset >= 0)
        {
            _autoResolution.Frame(now, Display.RefreshRate, _activePreset);
        }
    }

    /// <summary>The size to draw at: the output's own, or about <see cref="TargetPixels"/> pixels in its shape.</summary>
    private (int Width, int Height) RenderSizeFor(int windowWidth, int windowHeight)
    {
        var target = _targetPixels;
        if (target <= 0)
        {
            if (!_autoResolutionOn || _autoResolution.PixelShare >= 1.0)
            {
                return (windowWidth, windowHeight);
            }

            target = (int)(_autoResolution.PixelShare * windowWidth * windowHeight);
        }

        var scale = Math.Clamp(Math.Sqrt(target / ((double)windowWidth * windowHeight)), MinRenderScale, MaxRenderScale);
        if (Math.Abs(scale - 1.0) < 0.05)
        {
            return (windowWidth, windowHeight);
        }

        var width = windowWidth * scale;
        var height = windowHeight * scale;
        var fit = Math.Min(1.0, MaxTextureSize / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * fit)), Math.Max(1, (int)Math.Round(height * fit)));
    }

    private void ResizeRenderTargets(MTLCommandBuffer commands, int width, int height)
    {
        // Frames still on the GPU keep their own hold on the old textures, so they can go now.
        ReleaseRenderTargets();
        for (var i = 0; i < _feedback.Length; i++)
        {
            _feedback[i] = new RenderTarget(_device, commands, width, height);
            _transitionTargets[i] = new RenderTarget(_device, commands, width, height);
        }

        if (width > _windowWidth)
        {
            _windowTarget = new RenderTarget(_device, commands, _windowWidth, _windowHeight);
        }

        _particleLayer = new RenderTarget(_device, commands, width, height);
        _renderWidth = width;
        _renderHeight = height;
    }

    private void ReleaseRenderTargets()
    {
        for (var i = 0; i < _feedback.Length; i++)
        {
            _feedback[i]?.Dispose();
            _feedback[i] = null;
            _transitionTargets[i]?.Dispose();
            _transitionTargets[i] = null;
        }

        _windowTarget?.Dispose();
        _windowTarget = null;
        _particleLayer?.Dispose();
        _particleLayer = null;
    }

    public void Dispose()
    {
        _stopCompiling = true;
        foreach (var thread in _compileThreads)
        {
            thread.Join();
        }

        _compileThreads.Clear();
        for (var i = 0; i < FramesInFlight; i++)
        {
            if (_inFlight[i] != IntPtr.Zero)
            {
                new MTLCommandBuffer(_inFlight[i]).WaitUntilCompleted();
                ObjC.Release(_inFlight[i]);
                _inFlight[i] = IntPtr.Zero;
            }

            _audioTextures[i].Dispose();
        }

        ReleaseRenderTargets();
        ReleaseOffscreenOutput();
        foreach (var slot in _slots)
        {
            slot.Dispose();
        }

        foreach (var pipeline in _presentPipelines.Values)
        {
            pipeline.Dispose();
        }

        _presentFunction.Dispose();
        _blendPipeline.Dispose();
        _downsamplePipeline.Dispose();
        _vertexFunction.Dispose();
        _spriteFunction.Dispose();
        foreach (var library in _engineLibraries)
        {
            library.Dispose();
        }

        _milkDrop?.Dispose();
        _sampler.Dispose();
        _queue.Dispose();
    }

    /// <summary>A visual and its compiled pipelines, which arrive from a compile thread.</summary>
    private sealed class PresetSlot : IDisposable
    {
        public const int Pending = 0;
        public const int Compiling = 1;
        public const int Ready = 2;
        public const int Failed = 3;

        public int State;
        public MTLRenderPipelineState Pipeline;

        // Particle visuals only: the simulation, the pipeline that draws the dots, and the particles.
        public MTLComputePipelineState? Simulation;
        public MTLRenderPipelineState SpritePipeline;
        public MTLBuffer? Particles;
        public int ParticleCount;
        public bool ParticlesFresh;

        // MilkDrop presets only: the running preset and its shaders, drawn instead of Pipeline.
        public MilkDropVisual? MilkDrop;

        // AVS presets only: the running preset, and a texture per frame in flight for its picture, shown by Pipeline.
        public AvsVisual? Avs;
        private readonly MTLTexture?[] _avsPictures = new MTLTexture?[FramesInFlight];

        public PresetSlot(PresetSource source) => Source = source;

        public PresetSource Source { get; }

        public List<MTLLibrary> Libraries { get; } = new();

        public void ReleaseParticles()
        {
            Particles?.Dispose();
            Particles = null;
        }

        /// <summary>This frame's texture for the AVS picture, made (again) to fit.</summary>
        public MTLTexture AvsPicture(MTLDevice device, int frameSlot, int width, int height)
        {
            if (_avsPictures[frameSlot] is { } texture && (int)texture.Width == width && (int)texture.Height == height)
            {
                return texture;
            }

            _avsPictures[frameSlot]?.Dispose();
            var description = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, (ulong)width, (ulong)height, false);
            description.Usage = MTLTextureUsage.ShaderRead;
            description.StorageMode = MTLStorageMode.Shared;
            // The descriptor is autoreleased, so it isn't disposed here: releasing it as well crashed the app
            // when the autorelease pool emptied, as soon as the first AVS preset came on screen.
            var made = device.NewTexture(description);
            _avsPictures[frameSlot] = made;
            return made;
        }

        /// <summary>Frees the AVS picture's textures; the preset starts over when it's next shown.</summary>
        public void ReleaseAvsPictures()
        {
            Avs?.Hide();
            for (var i = 0; i < _avsPictures.Length; i++)
            {
                _avsPictures[i]?.Dispose();
                _avsPictures[i] = null;
            }
        }

        public void Dispose()
        {
            ReleaseParticles();
            ReleaseAvsPictures();
            if (Pipeline.NativePtr != IntPtr.Zero)
            {
                Pipeline.Dispose();
            }

            if (SpritePipeline.NativePtr != IntPtr.Zero)
            {
                SpritePipeline.Dispose();
            }

            Simulation?.Dispose();
            MilkDrop?.Dispose();
            foreach (var library in Libraries)
            {
                library.Dispose();
            }
        }
    }

    /// <summary>A texture the visuals draw into and read back from (feedback, transitions, supersampling, particles).</summary>
    private sealed class RenderTarget : IDisposable
    {
        public RenderTarget(MTLDevice device, MTLCommandBuffer commands, int width, int height)
        {
            var description = MTLTextureDescriptor.Texture2DDescriptor(FeedbackFormat, (ulong)width, (ulong)height, false);
            description.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
            description.StorageMode = MTLStorageMode.Private;
            Texture = device.NewTexture(description);

            // Start black: the first frame reads it as the "previous" picture.
            EndPass(BeginPass(commands, Texture, clear: true));
        }

        public MTLTexture Texture { get; }

        public void Dispose() => Texture.Dispose();
    }
}
