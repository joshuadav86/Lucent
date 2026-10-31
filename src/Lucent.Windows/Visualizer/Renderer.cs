using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using Lucent.Audio;
using Lucent.Visualizer.Avs;
using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer;

/// <summary>
/// Draws the visualizer with Direct3D 11 on its own thread. Each frame the current preset reads
/// the previous frame (feedback) and draws on top of it. When drawing above window resolution the
/// result is shrunk to the window (supersampling), then it's bloomed and tone-mapped onto the
/// window, in HDR on HDR screens. Visuals compile in the background (and are cached on disk),
/// so the first one appears straight away. Particle visuals also move hundreds of thousands of
/// particles with a compute shader each frame and draw them as glowing dots before their own pass.
/// It can also draw without a window, one frame at a time, for recording a video (Renderer.Offscreen.cs).
/// MilkDrop presets (.milk files) are drawn by <see cref="MilkDropRenderer"/> into the same picture.
/// Winamp AVS presets (.avs files) draw on the CPU (<see cref="AvsVisual"/>); their picture is uploaded each
/// frame and drawn by Shaders/AvsDisplay.hlsl as if it were a visual of Lucent's own.
/// </summary>
public sealed partial class Renderer : IDisposable
{
    private const Format FeedbackFormat = Format.R16G16B16A16_Float;
    private const Format SdrBackBufferFormat = Format.B8G8R8A8_UNorm;
    private const Format HdrBackBufferFormat = Format.R16G16B16A16_Float;
    private const int ConstantCount = 40;
    private const int ParticleStride = 64;
    private const int MaxParticleCount = 4 * 1024 * 1024;
    private const double MaxRenderScale = 4.0;
    private const double MinRenderScale = 0.25;
    private const int MaxTextureSize = 16384;
    private const int MaxDeviceResets = 3;

    // Transition styles the engine's blend shader knows (see Engine.hlsl). The morphs (4-7)
    // look the most natural, so they come up twice as often as the reveals (1-3) and the crossfade (0).
    private static readonly int[] AllTransitionStyles = { 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
    private static readonly int[] MorphTransitionStyles = { 4, 5, 6, 7 };

    private readonly IntPtr _hwnd;
    // No window: frames are drawn into a texture when asked for (see Renderer.Offscreen.cs).
    private readonly bool _offscreen;
    private readonly AudioAnalyzer _analyzer;
    private readonly PresetSlot[] _slots;
    private readonly ShaderCache _shaderCache = new();
    private readonly Thread _thread;
    private readonly List<Thread> _compileThreads = new();
    private readonly float[] _constants = new float[ConstantCount];
    private readonly Random _random = new();
    private volatile bool _running;
    private volatile bool _stopCompiling;
    private volatile bool _compileInterrupted;
    private volatile int _requestedPreset;
    private volatile bool _autoCycle = true;
    private volatile int _targetPixels;
    private volatile float _quality = 1f;
    private volatile float _bloomStrength = 1f;
    private volatile float _transitionSeconds = 3f;
    private volatile float _autoCycleSeconds = 25f;
    private volatile int[]? _autoCycleChoices;
    private volatile bool _morphsOnly;
    private volatile bool _hdrRequested;
    private int _compilersRunning;
    private int _activePreset = -1;
    private readonly AutoResolution _autoResolution = new();
    private volatile bool _autoResolutionOn;
    private int _fromPreset = -1;
    private double _transitionStart;
    private double _transitionLength = 3;
    private double _lastSwitch;
    // The downbeat (in seconds into the song) the next Auto change is timed to land on, and the song it's in.
    private double? _landing;
    private BeatMap? _landingSong;
    // Whether that landing is on a drop, which gets a quick cut.
    private bool _landingOnDrop;
    private readonly SongFollower _songFollower = new();
    private int _transitionStyle;
    private int _currentFeedback;
    private int _frameCount;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private RenderTarget? _windowTarget;
    private readonly RenderTarget?[] _feedback = new RenderTarget?[2];
    private readonly RenderTarget?[] _transitionTargets = new RenderTarget?[2];
    private ID3D11Texture2D? _audioTexture;
    private ID3D11ShaderResourceView? _audioView;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11SamplerState? _sampler;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _presentShader;
    private ID3D11PixelShader? _blendShader;
    private ID3D11PixelShader? _downsampleShader;
    private ID3D11PixelShader? _spriteShader;
    private ID3D11BlendState? _additiveBlend;
    private ID3D11RasterizerState? _noCulling;
    private RenderTarget? _particleLayer;
    private MilkDropRenderer? _milkDrop;
    private double _frameTime;
    private float _frameDelta;
    private int _frameNumber;
    private int _windowWidth;
    private int _windowHeight;
    private int _renderWidth;
    private int _renderHeight;
    private bool _hdrActive;
    private bool _hdrRefused;
    private DisplayInfo _display = DisplayInfo.Standard;
    private Task<DisplayInfo?>? _displayCheck;

    public Renderer(IntPtr hwnd, AudioAnalyzer analyzer)
        : this(hwnd, analyzer, PresetLibrary.LoadPresets())
    {
    }

    private Renderer(IntPtr hwnd, AudioAnalyzer analyzer, IReadOnlyList<PresetSource> presets)
    {
        _hwnd = hwnd;
        _offscreen = hwnd == IntPtr.Zero;
        _analyzer = analyzer;
        _slots = presets.Select(source => new PresetSlot(source)).ToArray();
        Presets = _slots.Select(slot => slot.Source).ToList();
        PresetNames = _slots.Select(slot => slot.Source.Name).ToList();
        _thread = new Thread(Run) { IsBackground = true, Name = "Visualizer renderer" };
    }

    /// <summary>Raised (on the render thread) as soon as it starts, with every visual's name.</summary>
    public event Action<IReadOnlyList<string>>? PresetsReady;

    /// <summary>Raised (on a compile thread) when a visual fails to compile; it is skipped.</summary>
    public event Action<string>? PresetFailed;

    /// <summary>Raised (on the render thread) when the renderer moves to another visual by itself.</summary>
    public event Action<int>? PresetChanged;

    /// <summary>Raised (on the render thread) if the GPU can't be used at all.</summary>
    public event Action<string>? Failed;

    /// <summary>Raised (on the render thread) when the graphics card reset and the visuals restarted.</summary>
    public event Action? DeviceReset;

    /// <summary>Raised (on the render thread) about once a second with the frame rate and drawing size.</summary>
    public event Action<FrameStats>? FrameRateMeasured;

    /// <summary>Raised (on the render thread) when the screen's HDR support is found or changes.</summary>
    public event Action<DisplayInfo>? DisplayChanged;

    /// <summary>Every visual, in the order <see cref="PresetIndex"/> counts them.</summary>
    public IReadOnlyList<string> PresetNames { get; }

    /// <summary>Every visual's file (with its family and tags), in the same order as <see cref="PresetNames"/>.</summary>
    public IReadOnlyList<PresetSource> Presets { get; }

    public int PresetIndex
    {
        get => _requestedPreset;
        set => _requestedPreset = value;
    }

    /// <summary>When on, moves to a random visual every so often, on a beat.</summary>
    public bool AutoCycle
    {
        get => _autoCycle;
        set => _autoCycle = value;
    }

    /// <summary>
    /// How many pixels to draw, e.g. 3840 x 2160 for 4K, or 0 to match the window. Above the
    /// window's size the image is drawn bigger and shrunk to fit, which smooths edges and adds detail.
    /// </summary>
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

    /// <summary>How strongly highlights glow: 0 off, 1 normal.</summary>
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

    /// <summary>The visuals Auto picks from (by index), or null for all of them.</summary>
    public IReadOnlyCollection<int>? AutoCycleChoices
    {
        get => _autoCycleChoices;
        set => _autoCycleChoices = value?.ToArray();
    }

    public float AutoCycleSeconds
    {
        get => _autoCycleSeconds;
        set => _autoCycleSeconds = Math.Clamp(value, 5f, 3600f);
    }

    /// <summary>
    /// The song that's playing and its bars, once they've been worked out. With them, Auto times
    /// each change so the new visual arrives right on a downbeat.
    /// </summary>
    public SongClock? SongClock { get; set; }

    /// <summary>The mouse over the visuals, which pushes a Particles visual's dots aside; null when there's no mouse (recording, say).</summary>
    public PointerStir? Pointer { get; set; }

    /// <summary>When on, only the morphing transitions are used.</summary>
    public bool MorphTransitionsOnly
    {
        get => _morphsOnly;
        set => _morphsOnly = value;
    }

    /// <summary>Draw in HDR when the screen has HDR turned on in Windows.</summary>
    public bool Hdr
    {
        get => _hdrRequested;
        set => _hdrRequested = value;
    }

    public void Start()
    {
        _running = true;
        _thread.Start();
    }

    public void Dispose() => Stop(TimeSpan.FromSeconds(2));

    /// <summary>Stops drawing and waits (up to <paramref name="timeout"/>) for the GPU's things to be let go.</summary>
    public void Stop(TimeSpan timeout)
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(timeout);
        }
        else if (_offscreen)
        {
            // Drawn on the caller's thread, so everything is let go here, on that same thread.
            ReleaseAll();
        }
    }

    private void Run()
    {
        PresetsReady?.Invoke(PresetNames);
        var resets = 0;
        var attempt = new Stopwatch();
        while (_running)
        {
            attempt.Restart();
            try
            {
                if (_slots.Length == 0)
                {
                    throw new InvalidOperationException("No visual presets were found.");
                }

                CreateDevice();
                CreateResources();
                CompileEngineShaders();
                StartCompiling();
                RenderLoop();
                return;
            }
            catch (Exception ex) when (_running && IsDeviceLost(ex))
            {
                // The graphics card reset (a driver update, or a frame that took too long).
                // Everything is rebuilt on the next pass round the loop. A card that ran fine
                // for a while since the last reset gets its retries back.
                resets = attempt.Elapsed.TotalMinutes > 1 ? 1 : resets + 1;
                if (resets > MaxDeviceResets)
                {
                    Failed?.Invoke(ex.Message);
                    return;
                }

                DeviceReset?.Invoke();
            }
            catch (Exception ex)
            {
                Failed?.Invoke(ex.Message);
                return;
            }
            finally
            {
                ReleaseAll();
            }
        }
    }

    private void CreateDevice()
    {
        _factory = DXGI.CreateDXGIFactory1<IDXGIFactory2>();

        // Prefer the high-performance GPU (e.g. the Nvidia card on laptops with two GPUs).
        IDXGIAdapter1? adapter = null;
        using (var factory6 = _factory.QueryInterfaceOrNull<IDXGIFactory6>())
        {
            if (factory6 is not null)
            {
                adapter = factory6.EnumAdapterByGpuPreference<IDXGIAdapter1>(0, GpuPreference.HighPerformance);
            }
        }

        var levels = new[] { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        var result = D3D11.D3D11CreateDevice(
            adapter,
            adapter is null ? DriverType.Hardware : DriverType.Unknown,
            DeviceCreationFlags.BgraSupport,
            levels,
            out _device,
            out _context);
        adapter?.Dispose();

        if (result.Failure)
        {
            // No usable GPU (e.g. a remote desktop session): fall back to software rendering.
            D3D11.D3D11CreateDevice(null, DriverType.Warp, DeviceCreationFlags.BgraSupport, levels, out _device, out _context)
                .CheckError();
        }

        // The swap chain starts at the window's size in standard range; the first frame
        // creates the views and switches to HDR if it's wanted.
        if (!_offscreen)
        {
            var (width, height) = GetClientSize();
            var description = new SwapChainDescription1(
                (uint)width, (uint)height, SdrBackBufferFormat, false, Usage.RenderTargetOutput, 2,
                Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
            _swapChain = _factory.CreateSwapChainForHwnd(_device!, _hwnd, description, null, null);
            _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
        }

        _windowWidth = 0;
        _windowHeight = 0;
        _renderWidth = 0;
        _renderHeight = 0;
        _hdrActive = false;
        _hdrRefused = false;
    }

    private void CreateResources()
    {
        var device = _device!;

        _constantBuffer = device.CreateBuffer(
            ConstantCount * sizeof(float), BindFlags.ConstantBuffer, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.None, 0);

        _audioTexture = device.CreateTexture2D(
            Format.R32_Float, AudioAnalyzer.TextureWidth, 2, 1, 1, null,
            BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
        _audioView = device.CreateShaderResourceView(_audioTexture, null);

        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);

        // Particles are glowing dots added on top of each other, drawn from both sides.
        _additiveBlend = device.CreateBlendState(new BlendDescription(Blend.One, Blend.One));
        _noCulling = device.CreateRasterizerState(RasterizerDescription.CullNone);
    }

    private void CompileEngineShaders()
    {
        var device = _device!;
        var engine = PresetLibrary.ReadShader("Common.hlsli") + "\n" + PresetLibrary.ReadShader("Engine.hlsl");
        _vertexShader = device.CreateVertexShader(_shaderCache.GetOrCompile(engine, "VSMain", "Engine.hlsl", "vs_5_0"), null);
        _presentShader = CreatePixelShader(device, engine, "PSPresent", "Engine.hlsl");
        _blendShader = CreatePixelShader(device, engine, "PSBlend", "Engine.hlsl");
        _downsampleShader = CreatePixelShader(device, engine, "PSDownsample", "Engine.hlsl");
        var sprites = PresetLibrary.ReadShader("Common.hlsli") + "\n" + PresetLibrary.ReadShader("Particles.hlsli");
        _spriteShader = CreatePixelShader(device, sprites, "PSSprite", "Particles.hlsli");
        if (_slots.Any(slot => slot.Source.IsMilkDrop))
        {
            _milkDrop = new MilkDropRenderer(
                device, _context!, _shaderCache, PresetLibrary.ReadShader("MilkDrop.hlsli"), PresetLibrary.ReadShader("MilkDrop.hlsl"));
        }
    }

    private ID3D11PixelShader CreatePixelShader(ID3D11Device device, string source, string entryPoint, string sourceName)
    {
        var bytecode = _shaderCache.GetOrCompile(source, entryPoint, sourceName, "ps_5_0");
        try
        {
            return device.CreatePixelShader(bytecode, null);
        }
        catch (Exception)
        {
            // A damaged cache file: compile it again from scratch.
            bytecode = _shaderCache.GetOrCompile(source, entryPoint, sourceName, "ps_5_0", useCache: false);
            return device.CreatePixelShader(bytecode, null);
        }
    }

    // ---- Compiling visuals in the background ----

    private void StartCompiling()
    {
        _stopCompiling = false;
        _compileInterrupted = false;
        var device = _device!;
        var shaders = ReadEngineSources();
        var count = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _compilersRunning = count;
        for (var i = 0; i < count; i++)
        {
            var thread = new Thread(() => CompileLoop(device, shaders))
            {
                IsBackground = true,
                Name = "Visual compiler",
                Priority = ThreadPriority.BelowNormal,
            };
            _compileThreads.Add(thread);
            thread.Start();
        }
    }

    private void CompileLoop(ID3D11Device device, EngineSources shaders)
    {
        while (!_stopCompiling)
        {
            var index = ClaimNextPreset();
            if (index < 0)
            {
                break;
            }

            var slot = _slots[index];
            try
            {
                BuildShaders(device, shaders, slot);
                Volatile.Write(ref slot.State, PresetSlot.Ready);
            }
            catch (Exception ex) when (IsDeviceLost(ex))
            {
                // The graphics card reset: the render thread starts over, and this visual compiles again then.
                Volatile.Write(ref slot.State, PresetSlot.Pending);
                _compileInterrupted = true;
                break;
            }
            catch (Exception ex)
            {
                Volatile.Write(ref slot.State, PresetSlot.Failed);
                if (slot.Source.IsCommunityPreset)
                {
                    // A broken .milk or .avs file is skipped; the log says which and why.
                    ErrorLog.Write($"{KindOf(slot.Source)} {Path.GetFileName(slot.Source.FilePath)} was skipped", ex.Message);
                }

                PresetFailed?.Invoke($"{slot.Source.Name}: {ex.Message}");
            }
        }

        // The last compiler to finish a full pass tidies away cache files for visuals that changed or went.
        if (Interlocked.Decrement(ref _compilersRunning) == 0 && !_stopCompiling && !_compileInterrupted)
        {
            _shaderCache.RemoveUnused();
        }
    }

    private EngineSources ReadEngineSources() => new(
        PresetLibrary.ReadShader("Common.hlsli"),
        PresetLibrary.ReadShader("PresetMain.hlsli"),
        PresetLibrary.ReadShader("Particles.hlsli"),
        PresetLibrary.ReadShader("ParticleMain.hlsli"),
        _milkDrop is null ? string.Empty : PresetLibrary.ReadShader("MilkDrop.hlsli"),
        _slots.Any(slot => slot.Source.IsAvs) ? PresetLibrary.ReadShader("AvsDisplay.hlsl") : string.Empty);

    private static string KindOf(PresetSource source) => source.IsAvs ? "AVS preset" : "MilkDrop preset";

    /// <summary>Compiles (or fetches from the cache) a visual's shaders. Throws with the compiler's message if it doesn't compile.</summary>
    private void BuildShaders(ID3D11Device device, EngineSources shaders, PresetSlot slot)
    {
        if (slot.Source.IsMilkDrop)
        {
            // MilkDrop presets compile their equations, and their shaders if they have them.
            slot.MilkDrop = MilkDropVisual.Compile(device, _shaderCache, slot.Source, shaders.MilkDrop);
            return;
        }

        if (slot.Source.IsAvs)
        {
            // AVS presets compile their code; every one is shown by the same display shader.
            slot.Avs = AvsVisual.Load(slot.Source);
            var display = shaders.Common + "\n#line 1\n" + shaders.AvsDisplay + "\n" + shaders.PresetMain;
            slot.Shader = CreatePixelShader(device, display, "PSMain", "AvsDisplay.hlsl");
            return;
        }

        // Same assembly as the build's shader check (.github/workflows/build.yml).
        var fileName = Path.GetFileName(slot.Source.FilePath);
        var api = slot.Source.IsParticles ? shaders.Common + "\n" + shaders.Particles : shaders.Common;
        var code = api + "\n#line 1\n" + slot.Source.Code + "\n";
        if (slot.Source.IsParticles)
        {
            var particleCode = code + shaders.ParticleMain;
            slot.Simulation = device.CreateComputeShader(_shaderCache.GetOrCompile(particleCode, "CSMain", fileName, "cs_5_0"), null);
            slot.SpriteVertices = device.CreateVertexShader(_shaderCache.GetOrCompile(particleCode, "VSParticle", fileName, "vs_5_0"), null);
        }

        slot.Shader = CreatePixelShader(device, code + shaders.PresetMain, "PSMain", fileName);
    }

    /// <summary>Picks the visual that's been asked for first, then its neighbours, then the rest in order.</summary>
    private int ClaimNextPreset()
    {
        var count = _slots.Length;
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

    /// <summary>Whether a visual has compiled and can be shown.</summary>
    public bool IsReady(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Ready;

    /// <summary>Whether a visual didn't compile, or (a MilkDrop preset) went wrong while drawing, so it's skipped.</summary>
    public bool HasFailed(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Failed;

    /// <summary>Stops the compile threads, once each has finished the visual it's compiling.</summary>
    private void StopCompiling()
    {
        _stopCompiling = true;
        foreach (var thread in _compileThreads)
        {
            thread.Join();
        }

        _compileThreads.Clear();
    }

    // ---- Drawing ----

    private void RenderLoop()
    {
        var context = _context!;
        var clock = Stopwatch.StartNew();
        var lastTime = 0.0;
        var frames = 0;
        var frameRateStart = 0.0;
        var nextDisplayCheck = 1.0;
        UseDisplay(ReadDisplay(_hwnd));

        while (_running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var deltaTime = (float)Math.Clamp(now - lastTime, 0.0, 0.1);
            lastTime = now;

            var (width, height) = GetClientSize();
            if (width <= 1 || height <= 1 || !IsWindowVisible(_hwnd))
            {
                // Minimized or hidden (the visuals window can be closed while music plays): nothing to draw.
                Thread.Sleep(50);
                continue;
            }

            // Once a second, check in the background whether the screen under the window has HDR on
            // (it can be switched, or the window moved to another screen).
            if (_displayCheck is { IsCompleted: true } check)
            {
                _displayCheck = null;
                nextDisplayCheck = now + 1.0;
                UseDisplay(check.Result);
            }
            else if (_displayCheck is null && now >= nextDisplayCheck)
            {
                var hwnd = _hwnd;
                _displayCheck = Task.Run(() => ReadDisplay(hwnd));
            }

            var wantHdr = _hdrRequested && _display.HdrOn && !_hdrRefused;
            if (width != _windowWidth || height != _windowHeight || wantHdr != _hdrActive)
            {
                ResizeWindow(width, height, wantHdr);
            }

            var (renderWidth, renderHeight) = RenderSizeFor(width, height);
            if (renderWidth != _renderWidth || renderHeight != _renderHeight)
            {
                ResizeRenderTargets(renderWidth, renderHeight);
            }

            _analyzer.Update(deltaTime);
            FollowSong(deltaTime);
            var progress = UpdatePresetChoice(now);
            if (_activePreset < 0)
            {
                // Nothing compiled yet: show black until the first visual is ready.
                context.ClearRenderTargetView(_backBufferView!, new Color4(0f, 0f, 0f, 1f));
                Present();
                continue;
            }

            DrawFrame(now, deltaTime, progress, _backBufferView!);
            Present();
            if (_autoResolutionOn && _targetPixels <= 0)
            {
                _autoResolution.Frame(now, _display.RefreshRate, _activePreset);
            }

            frames++;
            if (now - frameRateStart >= 1.0)
            {
                FrameRateMeasured?.Invoke(new FrameStats(frames / (now - frameRateStart), _renderWidth, _renderHeight, _hdrActive));
                frames = 0;
                frameRateStart = now;
            }
        }
    }

    /// <summary>
    /// Draws one frame of the visual (or of a transition, <paramref name="progress"/> of the way through)
    /// into <paramref name="output"/>, which is window-sized: the window's back buffer, or a texture.
    /// </summary>
    private void DrawFrame(double now, float deltaTime, float progress, ID3D11RenderTargetView output)
    {
        var context = _context!;
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
        // The glow swells through a build-up and flares on the drop.
        var moment = _songFollower.Moment;
        var (buildUp, drop) = SongLook.Of(_songFollower);
        _constants[14] = _bloomStrength * (1f + 0.6f * buildUp + 1f * drop);
        _constants[15] = (float)_renderWidth / _windowWidth;
        _constants[16] = _hdrActive ? 1f : 0f;
        _constants[17] = _display.SdrWhiteNits / 80f;
        _constants[18] = _display.PeakNits / 80f;
        _constants[19] = _frameCount++;
        _constants[23] = LookAmount(AvsVisual.ShownPlain, progress);
        _constants[24] = LookAmount(MilkDropLook.BringsIntoLight, progress);
        _constants[25] = moment.BeatPhase;
        _constants[26] = moment.BarPhase;
        _constants[27] = moment.Intensity;
        _constants[28] = buildUp;
        _constants[29] = drop;
        _constants[30] = moment.SectionPhase;
        _constants[31] = _songFollower.Mapped ? 1f : _songFollower.Live ? 0.5f : 0f;
        if (Pointer is { } pointer)
        {
            pointer.Update((float)_renderWidth / _renderHeight);
            _constants[32] = pointer.X;
            _constants[33] = pointer.Y;
            _constants[34] = pointer.VelocityX;
            _constants[35] = pointer.VelocityY;
        }
        context.UpdateSubresource(_constants, _constantBuffer!, 0, 0, 0, null);
        context.UpdateSubresource(_analyzer.TextureData, _audioTexture!, 0, AudioAnalyzer.TextureWidth * sizeof(float), 0, null);
        _frameTime = now;
        _frameDelta = deltaTime;
        _frameNumber = _frameCount;
        UseFrameState();

        var target = _feedback[_currentFeedback]!;
        var previous = _feedback[1 - _currentFeedback]!;

        if (_fromPreset < 0)
        {
            // Pass 1: the preset draws into the feedback buffer, reading last frame's.
            DrawPreset(target, previous, _activePreset);
        }
        else
        {
            // Mid-transition: run both presets from the same last frame, then blend them.
            DrawPreset(_transitionTargets[0]!, previous, _fromPreset);
            DrawPreset(_transitionTargets[1]!, previous, _activePreset);
            DrawPass(target.RenderTargetView, _transitionTargets[0]!.ShaderResource, _blendShader!, _transitionTargets[1]!.ShaderResource);
        }

        // Pass 2 (only above window resolution): shrink the image to the window.
        context.RSSetViewport(0, 0, _windowWidth, _windowHeight, 0, 1);
        var shown = target;
        if (supersampling)
        {
            DrawPass(_windowTarget!.RenderTargetView, target.ShaderResource, _downsampleShader!, null);
            shown = _windowTarget;
        }

        // Pass 3: bloom and tone-map the result onto the window.
        DrawPass(output, shown.ShaderResource, _presentShader!, null);
        _currentFeedback = 1 - _currentFeedback;
    }

    /// <summary>
    /// A MilkDrop or AVS preset that went wrong while drawing (rather than while compiling) is dropped for
    /// the rest of the session and the visuals move on, instead of the whole visualizer stopping.
    /// </summary>
    private void StopCommunityPreset(int presetIndex, Exception ex)
    {
        var slot = _slots[presetIndex];
        Volatile.Write(ref slot.State, PresetSlot.Failed);
        _context!.OMSetBlendState(null);
        _context.RSSetState(null);
        ErrorLog.Write($"{KindOf(slot.Source)} {Path.GetFileName(slot.Source.FilePath)} stopped", ex);
        PresetFailed?.Invoke($"{slot.Source.Name}: {ex.Message}");
        var next = NextUsable(presetIndex);
        if (_requestedPreset == presetIndex && next >= 0)
        {
            _requestedPreset = next;
            PresetChanged?.Invoke(next);
        }
    }

    /// <summary>
    /// How much of the picture is a visual that <paramref name="shows"/> picks out (1 when one is showing, part-way
    /// during a transition to or from one), for the screen pass: classic presets shown as they first looked, which
    /// it shows without Lucent's glow and tone-mapping, and modernised MilkDrop presets, which it brings into
    /// Lucent's light first.
    /// </summary>
    private float LookAmount(Func<PresetSource, bool> shows, float progress)
    {
        var to = shows(_slots[_activePreset].Source) ? 1f : 0f;
        if (_fromPreset < 0)
        {
            return to;
        }

        var from = shows(_slots[_fromPreset].Source) ? 1f : 0f;
        return from + (to - from) * progress;
    }

    /// <summary>The pipeline state every full-screen pass expects; MilkDrop presets change it, so it's set again after them.</summary>
    private void UseFrameState()
    {
        var context = _context!;
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetConstantBuffer(0, _constantBuffer);
        context.PSSetSampler(0, _sampler);
        context.PSSetShaderResource(1, _audioView!);
        context.RSSetViewport(0, 0, _renderWidth, _renderHeight, 0, 1);
    }

    private void DrawPreset(RenderTarget destination, RenderTarget source, int presetIndex)
    {
        var slot = _slots[presetIndex];
        if (slot.MilkDrop is { } milkDrop && _milkDrop is not null)
        {
            if (!milkDrop.NotesLogged)
            {
                // What had to be left out of it, logged when it's first shown rather than for a whole pack at every start.
                milkDrop.NotesLogged = true;
                if (milkDrop.Notes.Count > 0)
                {
                    ErrorLog.Write($"MilkDrop preset {Path.GetFileName(slot.Source.FilePath)}", string.Join(Environment.NewLine, milkDrop.Notes));
                }
            }

            if (!HasFailed(presetIndex))
            {
                try
                {
                    _milkDrop.Draw(
                        milkDrop, destination.RenderTargetView, _renderWidth, _renderHeight, _analyzer,
                        _frameNumber, _frameTime, _frameDelta, _quality, _autoCycleSeconds);
                }
                catch (Exception ex) when (!IsDeviceLost(ex))
                {
                    StopCommunityPreset(presetIndex, ex);
                }
            }

            UseFrameState();
            return;
        }

        if (slot.Avs is { } avs)
        {
            if (!avs.NotesLogged)
            {
                avs.NotesLogged = true;
                if (avs.Notes.Count > 0)
                {
                    ErrorLog.Write($"AVS preset {Path.GetFileName(slot.Source.FilePath)}", string.Join(Environment.NewLine, avs.Notes));
                }
            }

            ID3D11ShaderResourceView? picture = null;
            if (!HasFailed(presetIndex))
            {
                try
                {
                    picture = UploadAvsPicture(slot, avs);
                }
                catch (Exception ex) when (!IsDeviceLost(ex))
                {
                    StopCommunityPreset(presetIndex, ex);
                }
            }

            if (picture is null)
            {
                _context!.ClearRenderTargetView(destination.RenderTargetView, new Color4(0f, 0f, 0f, 1f));
                return;
            }

            DrawPass(destination.RenderTargetView, source.ShaderResource, slot.Shader!, null, picture);
            return;
        }

        ID3D11ShaderResourceView? particleLayer = null;
        if (slot.Simulation is not null)
        {
            RunParticles(slot, source);
            particleLayer = _particleLayer!.ShaderResource;
        }

        DrawPass(destination.RenderTargetView, source.ShaderResource, slot.Shader!, null, particleLayer);
    }

    /// <summary>Moves an AVS preset on by a frame and puts its picture in the slot's texture, made to fit.</summary>
    private ID3D11ShaderResourceView UploadAvsPicture(PresetSlot slot, AvsVisual avs)
    {
        var pixels = avs.Draw(_analyzer, _frameNumber, _renderWidth, _renderHeight, _frameTime, _quality, AvsVisual.ModernLook);
        var width = avs.Runner.Width;
        var height = avs.Runner.Height;
        if (slot.AvsTexture is null || slot.AvsTexture.Description.Width != width || slot.AvsTexture.Description.Height != height)
        {
            slot.AvsView?.Dispose();
            slot.AvsTexture?.Dispose();
            slot.AvsTexture = _device!.CreateTexture2D(
                Format.B8G8R8A8_UNorm, (uint)width, (uint)height, 1, 1, null,
                BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);
            slot.AvsView = _device.CreateShaderResourceView(slot.AvsTexture, null);
        }

        _context!.UpdateSubresource(pixels, slot.AvsTexture, 0, (uint)width * sizeof(uint), 0, null);
        return slot.AvsView!;
    }

    /// <summary>
    /// Moves a particle visual's particles on by a frame (compute shader), then draws each one as a
    /// soft dot, all added together into the particle layer that the visual's own pass reads.
    /// </summary>
    private void RunParticles(PresetSlot slot, RenderTarget previous)
    {
        var context = _context!;
        var count = ParticleCountFor(slot.Source);
        if (slot.Particles is null || slot.Particles.Count != count)
        {
            slot.Particles?.Dispose();
            slot.Particles = new ParticleBuffer(_device!, count);
        }

        var particles = slot.Particles;
        _constants[20] = count;
        _constants[21] = particles.Fresh ? 1f : 0f;
        _constants[22] = (float)slot.Source.NormalParticleCount / count;
        // Only the Particles family can be pushed around by the mouse.
        _constants[36] = Pointer is { } pointer && slot.Source.Family == VisualStyles.ParticlesFamily ? pointer.Strength : 0f;
        particles.Fresh = false;
        context.UpdateSubresource(_constants, _constantBuffer!, 0, 0, 0, null);

        context.CSSetShader(slot.Simulation);
        context.CSSetConstantBuffer(0, _constantBuffer);
        context.CSSetSampler(0, _sampler);
        context.CSSetShaderResource(0, previous.ShaderResource);
        context.CSSetShaderResource(1, _audioView!);
        context.CSSetUnorderedAccessView(0, particles.Writer);
        context.Dispatch((uint)((count + 255) / 256), 1, 1);
        context.CSSetUnorderedAccessView(0, null!);
        context.CSSetShaderResource(0, null!);

        var layer = _particleLayer!;
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(3, null!);
        context.OMSetRenderTargets(layer.RenderTargetView, null);
        context.ClearRenderTargetView(layer.RenderTargetView, new Color4(0f, 0f, 0f, 0f));
        context.OMSetBlendState(_additiveBlend);
        context.RSSetState(_noCulling);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        context.VSSetShader(slot.SpriteVertices);
        context.VSSetConstantBuffer(0, _constantBuffer);
        context.VSSetSampler(0, _sampler);
        context.VSSetShaderResource(1, _audioView!);
        context.VSSetShaderResource(4, particles.Reader);
        context.PSSetShader(_spriteShader);
        context.DrawInstanced(4, (uint)count, 0, 0);

        // Back to the full-screen passes.
        context.VSSetShaderResource(4, null!);
        context.OMSetBlendState(null);
        context.RSSetState(null);
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
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
        var count = (long)Math.Ceiling(source.NormalParticleCount * factor / 256.0) * 256;
        return (int)Math.Clamp(count, 256, MaxParticleCount);
    }

    /// <summary>Frees a visual's particles (or a MilkDrop preset's images) once it's no longer on screen; they start afresh next time.</summary>
    private void ReleaseParticles(int presetIndex)
    {
        if (presetIndex >= 0 && presetIndex != _activePreset)
        {
            _slots[presetIndex].Particles?.Dispose();
            _slots[presetIndex].Particles = null;
            _slots[presetIndex].MilkDrop?.ReleaseImages();
            ReleaseAvsPicture(_slots[presetIndex]);
        }
    }

    /// <summary>Frees an AVS preset's picture; it starts over when it's next shown.</summary>
    private static void ReleaseAvsPicture(PresetSlot slot)
    {
        slot.Avs?.Hide();
        slot.AvsView?.Dispose();
        slot.AvsView = null;
        slot.AvsTexture?.Dispose();
        slot.AvsTexture = null;
    }

    /// <remarks>
    /// Textures are bound for reading only after the destination is set: Direct3D quietly unbinds a
    /// texture that's bound for reading while it's still the one being drawn into, as the particle
    /// layer is until this pass switches away from it.
    /// </remarks>
    private void DrawPass(
        ID3D11RenderTargetView destination, ID3D11ShaderResourceView source, ID3D11PixelShader shader,
        ID3D11ShaderResourceView? extra, ID3D11ShaderResourceView? particleLayer = null)
    {
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(2, null!);
        context.PSSetShaderResource(3, null!);
        context.OMSetRenderTargets(destination, null);
        context.PSSetShaderResource(0, source);
        if (extra is not null)
        {
            context.PSSetShaderResource(2, extra);
        }

        if (particleLayer is not null)
        {
            context.PSSetShaderResource(3, particleLayer);
        }

        context.PSSetShader(shader);
        context.Draw(3, 0);
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(2, null!);
        context.PSSetShaderResource(3, null!);
    }

    private void Present()
    {
        var result = _swapChain!.Present(1, PresentFlags.None);
        if (IsDeviceLost(result))
        {
            throw new DeviceLostException();
        }

        result.CheckError();
    }

    private static bool IsDeviceLost(SharpGen.Runtime.Result result) =>
        result == Vortice.DXGI.ResultCode.DeviceRemoved || result == Vortice.DXGI.ResultCode.DeviceReset
        || result == Vortice.DXGI.ResultCode.DeviceHung;

    private static bool IsDeviceLost(Exception ex) =>
        ex is DeviceLostException || (ex is SharpGen.Runtime.SharpGenException sharpGen && IsDeviceLost(sharpGen.ResultCode));

    /// <summary>
    /// Starts transitions for visual changes (from the UI or auto-cycle) and returns how far
    /// through the current transition we are, 0..1. Visuals that haven't compiled yet are
    /// waited for; ones that failed are skipped.
    /// </summary>
    private float UpdatePresetChoice(double now)
    {
        var requested = Math.Clamp(_requestedPreset, 0, _slots.Length - 1);
        if (HasFailed(requested))
        {
            // That visual doesn't work: stay on (or move to) one that does, and tell the UI.
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
            if (ReadSong() is { } song)
            {
                // The song's bars are known: start the change early enough that the new visual is fully in
                // right on a downbeat, preferring the start of a phrase, or cut to it right on a drop.
                var lead = _transitionSeconds;
                if (_landing is { } planned && (_landingSong != song.Beats || song.Position > planned + 0.25 || planned - song.Position > interval * 1.5 + 30))
                {
                    // A new song, or the song was moved on or back past it.
                    _landing = null;
                }

                if (_landing is null && AutoLanding.Plan(song.Beats, song.Position, waited, interval, lead) is { } plan)
                {
                    _landing = plan.At;
                    _landingOnDrop = plan.OnDrop;
                    _landingSong = song.Beats;
                }

                // A cut onto a drop is quick, and centred on it so the new visual hits with the music.
                var length = _landingOnDrop ? Math.Min(lead, AutoLanding.DropCutSeconds) : lead;
                var start = _landingOnDrop ? length / 2 : length;
                if (_landing is { } landing && song.Position >= landing - start)
                {
                    AutoSwitch(now, length);
                }
                else if (waited > interval * 1.5 + 30)
                {
                    AutoSwitch(now, lead);
                }
            }
            else if (AutoLanding.Heard(_songFollower, waited, interval, _analyzer.Beat) is { } onDrop)
            {
                // Followed live: a cut the moment a drop is heard, otherwise a change on a downbeat (or a beat).
                AutoSwitch(now, onDrop ? Math.Min(_transitionSeconds, AutoLanding.DropCutSeconds / 2) : _transitionSeconds);
            }
        }

        if (_fromPreset < 0)
        {
            return 1f;
        }

        var t = Math.Clamp((now - _transitionStart) / _transitionLength, 0.0, 1.0);
        if (t >= 1.0)
        {
            ReleaseParticles(_fromPreset);
            _fromPreset = -1;
            return 1f;
        }

        return (float)(t * t * (3 - 2 * t));
    }

    /// <summary>The song's bars and where it's got to: from the player, or from the video being recorded.</summary>
    private (BeatMap Beats, double Position)? ReadSong() =>
        _offscreen
            ? _offscreenBeats is { } beats ? (beats, _offscreenPosition) : null
            : SongClock?.Read();

    /// <summary>Moves the song follower on a frame, and lets the song's own beats drive the beat pulse.</summary>
    private void FollowSong(float deltaTime)
    {
        _songFollower.Update(ReadSong(), deltaTime, _analyzer);
        // In a quiet stretch, a breakdown without drums, only beats that are heard count.
        _analyzer.FollowsSong = (_songFollower.Mapped || _songFollower.Live) && _songFollower.Moment.Intensity >= 0.3f;
        if (_analyzer.FollowsSong && _songFollower.BeatNow)
        {
            // Beats hit harder in the more intense parts of the song.
            _analyzer.MarkBeat(0.55f + 0.45f * _songFollower.Moment.Intensity);
        }
    }

    private void AutoSwitch(double now, double length)
    {
        var next = RandomReadyPreset();
        if (next >= 0)
        {
            _requestedPreset = next;
            StartTransition(next, now, length);
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

    /// <summary>Starts blending to a visual, over <paramref name="length"/> seconds (the Transition setting when not given).</summary>
    private void StartTransition(int presetIndex, double now, double? length = null)
    {
        // Any change, Auto's or the user's, starts the wait for the next one afresh.
        _landing = null;

        // Cutting a transition short: the visual it was leaving is gone straight away.
        var leaving = _fromPreset;
        _fromPreset = _activePreset;
        _activePreset = presetIndex;
        if (leaving != _fromPreset)
        {
            ReleaseParticles(leaving);
        }

        _transitionStart = now;
        _transitionLength = length ?? _transitionSeconds;
        _lastSwitch = now;
        var styles = _morphsOnly ? MorphTransitionStyles : AllTransitionStyles;
        _transitionStyle = styles[_random.Next(styles.Length)];
    }

    // ---- Sizes, supersampling and HDR ----

    /// <summary>The size to draw at: the window's own, or about <see cref="TargetPixels"/> pixels in the window's shape.</summary>
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

    private void ResizeWindow(int width, int height, bool hdr)
    {
        var context = _context!;
        context.ClearState();
        _backBufferView?.Dispose();
        _backBufferView = null;
        _windowTarget?.Dispose();
        _windowTarget = null;
        context.Flush();

        _hdrActive = false;
        if (hdr)
        {
            _swapChain!.ResizeBuffers(2, (uint)width, (uint)height, HdrBackBufferFormat, SwapChainFlags.None).CheckError();
            _hdrActive = TrySetColorSpace(ColorSpaceType.RgbFullG10NoneP709);
            if (!_hdrActive)
            {
                // The screen or driver turned it down: stay in standard range until something changes.
                _hdrRefused = true;
            }
        }

        if (!_hdrActive)
        {
            _swapChain!.ResizeBuffers(2, (uint)width, (uint)height, SdrBackBufferFormat, SwapChainFlags.None).CheckError();
            TrySetColorSpace(ColorSpaceType.RgbFullG22NoneP709);
        }

        _windowWidth = width;
        _windowHeight = height;
        using (var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0))
        {
            _backBufferView = _device!.CreateRenderTargetView(backBuffer, null);
        }

        // Force the drawing size to be worked out again against the new window.
        _renderWidth = 0;
        _renderHeight = 0;
    }

    private bool TrySetColorSpace(ColorSpaceType colorSpace)
    {
        using var swapChain3 = _swapChain!.QueryInterfaceOrNull<IDXGISwapChain3>();
        if (swapChain3 is null)
        {
            return colorSpace == ColorSpaceType.RgbFullG22NoneP709;
        }

        try
        {
            if ((swapChain3.CheckColorSpaceSupport(colorSpace) & SwapChainColorSpaceSupportFlags.Present) == 0)
            {
                return false;
            }

            swapChain3.SetColorSpace1(colorSpace);
            return true;
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            return false;
        }
    }

    private void ResizeRenderTargets(int width, int height)
    {
        var device = _device!;
        _context!.ClearState();
        ReleaseRenderTargets();

        for (var i = 0; i < _feedback.Length; i++)
        {
            _feedback[i] = new RenderTarget(device, _context, width, height);
            _transitionTargets[i] = new RenderTarget(device, _context, width, height);
        }

        if (width > _windowWidth)
        {
            _windowTarget = new RenderTarget(device, _context, _windowWidth, _windowHeight);
        }

        _particleLayer = new RenderTarget(device, _context, width, height);
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

    /// <summary>What the screen under the window can do, or null if Windows couldn't say this time.</summary>
    private static DisplayInfo? ReadDisplay(IntPtr hwnd)
    {
        try
        {
            return DisplayInfo.ForWindow(hwnd);
        }
        catch (Exception)
        {
            // Asked again in a second.
            return null;
        }
    }

    private void UseDisplay(DisplayInfo? display)
    {
        if (display is { } found && found != _display)
        {
            _display = found;
            _hdrRefused = false;
            DisplayChanged?.Invoke(found);
        }
    }

    private (int Width, int Height) GetClientSize()
    {
        return GetClientRect(_hwnd, out var rect)
            ? (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top))
            : (1, 1);
    }

    private void ReleaseAll()
    {
        // Compile threads use the device, so it can only go once they've stopped.
        StopCompiling();

        _context?.ClearState();
        _context?.Flush();
        ReleaseRenderTargets();
        _backBufferView?.Dispose();
        _backBufferView = null;
        ReleaseOffscreenOutput();
        foreach (var slot in _slots)
        {
            slot.Shader?.Dispose();
            slot.Shader = null;
            slot.Simulation?.Dispose();
            slot.Simulation = null;
            slot.SpriteVertices?.Dispose();
            slot.SpriteVertices = null;
            slot.Particles?.Dispose();
            slot.Particles = null;
            slot.MilkDrop?.Dispose();
            slot.MilkDrop = null;
            ReleaseAvsPicture(slot);
            slot.Avs = null;
            if (Volatile.Read(ref slot.State) != PresetSlot.Failed)
            {
                Volatile.Write(ref slot.State, PresetSlot.Pending);
            }
        }

        _activePreset = -1;
        _fromPreset = -1;
        _presentShader?.Dispose();
        _presentShader = null;
        _blendShader?.Dispose();
        _blendShader = null;
        _downsampleShader?.Dispose();
        _downsampleShader = null;
        _spriteShader?.Dispose();
        _spriteShader = null;
        _milkDrop?.Dispose();
        _milkDrop = null;
        _additiveBlend?.Dispose();
        _additiveBlend = null;
        _noCulling?.Dispose();
        _noCulling = null;
        _vertexShader?.Dispose();
        _vertexShader = null;
        _sampler?.Dispose();
        _sampler = null;
        _constantBuffer?.Dispose();
        _constantBuffer = null;
        _audioView?.Dispose();
        _audioView = null;
        _audioTexture?.Dispose();
        _audioTexture = null;
        _swapChain?.Dispose();
        _swapChain = null;
        _context?.Dispose();
        _device?.Dispose();
        _context = null;
        _device = null;
        _factory?.Dispose();
        _factory = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    private sealed class DeviceLostException : Exception
    {
        public DeviceLostException()
            : base("The graphics card stopped responding.")
        {
        }
    }

    /// <summary>A visual and its compiled shader, which arrives from a compile thread.</summary>
    private sealed class PresetSlot
    {
        public const int Pending = 0;
        public const int Compiling = 1;
        public const int Ready = 2;
        public const int Failed = 3;

        public int State;
        public ID3D11PixelShader? Shader;

        // Particle visuals only: the shaders that move and place the particles, and the particles.
        public ID3D11ComputeShader? Simulation;
        public ID3D11VertexShader? SpriteVertices;
        public ParticleBuffer? Particles;

        // MilkDrop presets only: the running preset and its shaders, drawn instead of Shader.
        public MilkDropVisual? MilkDrop;

        // AVS presets only: the running preset, and the texture its picture goes into for Shader to show.
        public AvsVisual? Avs;
        public ID3D11Texture2D? AvsTexture;
        public ID3D11ShaderResourceView? AvsView;

        public PresetSlot(PresetSource source) => Source = source;

        public PresetSource Source { get; }
    }

    /// <summary>The shared shader files a visual is assembled from.</summary>
    private sealed record EngineSources(string Common, string PresetMain, string Particles, string ParticleMain, string MilkDrop, string AvsDisplay);

    /// <summary>A particle visual's particles, on the graphics card: written by its compute shader, read to draw them.</summary>
    private sealed class ParticleBuffer : IDisposable
    {
        public ParticleBuffer(ID3D11Device device, int count)
        {
            Count = count;
            Buffer = device.CreateBuffer(new BufferDescription(
                (uint)(count * ParticleStride), BindFlags.UnorderedAccess | BindFlags.ShaderResource, ResourceUsage.Default,
                CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, ParticleStride));
            Writer = device.CreateUnorderedAccessView(Buffer, new UnorderedAccessViewDescription(Buffer, Format.Unknown, 0, (uint)count));
            Reader = device.CreateShaderResourceView(Buffer, new ShaderResourceViewDescription(Buffer, Format.Unknown, 0, (uint)count));
        }

        public int Count { get; }

        /// <summary>True until the first frame has spawned every particle.</summary>
        public bool Fresh { get; set; } = true;

        public ID3D11Buffer Buffer { get; }

        public ID3D11UnorderedAccessView Writer { get; }

        public ID3D11ShaderResourceView Reader { get; }

        public void Dispose()
        {
            Reader.Dispose();
            Writer.Dispose();
            Buffer.Dispose();
        }
    }

    /// <summary>A texture the visualizer draws into and reads back from (feedback, transitions, supersampling).</summary>
    private sealed class RenderTarget : IDisposable
    {
        public RenderTarget(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            Texture = device.CreateTexture2D(
                FeedbackFormat, (uint)width, (uint)height, 1, 1, null,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceOptionFlags.None,
                ResourceUsage.Default, CpuAccessFlags.None);
            RenderTargetView = device.CreateRenderTargetView(Texture, null);
            ShaderResource = device.CreateShaderResourceView(Texture, null);
            context.ClearRenderTargetView(RenderTargetView, new Color4(0f, 0f, 0f, 1f));
        }

        public ID3D11Texture2D Texture { get; }

        public ID3D11RenderTargetView RenderTargetView { get; }

        public ID3D11ShaderResourceView ShaderResource { get; }

        public void Dispose()
        {
            ShaderResource.Dispose();
            RenderTargetView.Dispose();
            Texture.Dispose();
        }
    }
}
