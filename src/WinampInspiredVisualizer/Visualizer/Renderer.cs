using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Draws the visualizer with Direct3D 11 on its own thread. Each frame the current preset reads
/// the previous frame (feedback) and draws on top of it. When drawing above window resolution the
/// result is shrunk to the window (supersampling), then it's bloomed and tone-mapped onto the
/// window, in HDR on HDR screens. Visuals compile in the background (and are cached on disk),
/// so the first one appears straight away.
/// </summary>
public sealed class Renderer : IDisposable
{
    private const Format FeedbackFormat = Format.R16G16B16A16_Float;
    private const Format SdrBackBufferFormat = Format.B8G8R8A8_UNorm;
    private const Format HdrBackBufferFormat = Format.R16G16B16A16_Float;
    private const int ConstantCount = 20;
    private const double MaxRenderScale = 4.0;
    private const double MinRenderScale = 0.25;
    private const int MaxTextureSize = 16384;
    private const int MaxDeviceResets = 3;

    // Transition styles the engine's blend shader knows (see Engine.hlsl). The morphs (4-7)
    // look the most natural, so they come up twice as often as the reveals (1-3) and the crossfade (0).
    private static readonly int[] AllTransitionStyles = { 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };
    private static readonly int[] MorphTransitionStyles = { 4, 5, 6, 7 };

    private readonly IntPtr _hwnd;
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
    private volatile bool _morphsOnly;
    private volatile bool _hdrRequested;
    private int _compilersRunning;
    private int _activePreset = -1;
    private int _fromPreset = -1;
    private double _transitionStart;
    private double _lastSwitch;
    private int _transitionStyle;

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
    private int _windowWidth;
    private int _windowHeight;
    private int _renderWidth;
    private int _renderHeight;
    private bool _hdrActive;
    private bool _hdrRefused;
    private DisplayInfo _display = DisplayInfo.Standard;
    private Task<DisplayInfo?>? _displayCheck;

    public Renderer(IntPtr hwnd, AudioAnalyzer analyzer)
    {
        _hwnd = hwnd;
        _analyzer = analyzer;
        _slots = PresetLibrary.LoadPresets().Select(source => new PresetSlot(source)).ToArray();
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

    public float AutoCycleSeconds
    {
        get => _autoCycleSeconds;
        set => _autoCycleSeconds = Math.Clamp(value, 5f, 3600f);
    }

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

    public void Dispose()
    {
        _running = false;
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(2));
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
        var (width, height) = GetClientSize();
        var description = new SwapChainDescription1(
            (uint)width, (uint)height, SdrBackBufferFormat, false, Usage.RenderTargetOutput, 2,
            Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
        _swapChain = _factory.CreateSwapChainForHwnd(_device!, _hwnd, description, null, null);
        _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
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
    }

    private void CompileEngineShaders()
    {
        var device = _device!;
        var engine = PresetLibrary.ReadShader("Common.hlsli") + "\n" + PresetLibrary.ReadShader("Engine.hlsl");
        _vertexShader = device.CreateVertexShader(_shaderCache.GetOrCompile(engine, "VSMain", "Engine.hlsl", "vs_5_0"), null);
        _presentShader = CreatePixelShader(device, engine, "PSPresent", "Engine.hlsl");
        _blendShader = CreatePixelShader(device, engine, "PSBlend", "Engine.hlsl");
        _downsampleShader = CreatePixelShader(device, engine, "PSDownsample", "Engine.hlsl");
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
        var common = PresetLibrary.ReadShader("Common.hlsli");
        var presetMain = PresetLibrary.ReadShader("PresetMain.hlsli");
        var count = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _compilersRunning = count;
        for (var i = 0; i < count; i++)
        {
            var thread = new Thread(() => CompileLoop(device, common, presetMain))
            {
                IsBackground = true,
                Name = "Visual compiler",
                Priority = ThreadPriority.BelowNormal,
            };
            _compileThreads.Add(thread);
            thread.Start();
        }
    }

    private void CompileLoop(ID3D11Device device, string common, string presetMain)
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
                var source = common + "\n#line 1\n" + slot.Source.Code + "\n" + presetMain;
                slot.Shader = CreatePixelShader(device, source, "PSMain", Path.GetFileName(slot.Source.FilePath));
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
                PresetFailed?.Invoke($"{slot.Source.Name}: {ex.Message}");
            }
        }

        // The last compiler to finish a full pass tidies away cache files for visuals that changed or went.
        if (Interlocked.Decrement(ref _compilersRunning) == 0 && !_stopCompiling && !_compileInterrupted)
        {
            _shaderCache.RemoveUnused();
        }
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

    private bool IsReady(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Ready;

    private bool HasFailed(int index) => Volatile.Read(ref _slots[index].State) == PresetSlot.Failed;

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
        var current = 0;
        var frames = 0;
        var frameCount = 0;
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
            var progress = UpdatePresetChoice(now);
            if (_activePreset < 0)
            {
                // Nothing compiled yet: show black until the first visual is ready.
                context.ClearRenderTargetView(_backBufferView!, new Color4(0f, 0f, 0f, 1f));
                Present();
                continue;
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
            _constants[16] = _hdrActive ? 1f : 0f;
            _constants[17] = _display.SdrWhiteNits / 80f;
            _constants[18] = _display.PeakNits / 80f;
            _constants[19] = frameCount++;
            context.UpdateSubresource(_constants, _constantBuffer!, 0, 0, 0, null);
            context.UpdateSubresource(_analyzer.TextureData, _audioTexture!, 0, AudioAnalyzer.TextureWidth * sizeof(float), 0, null);

            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader);
            context.PSSetConstantBuffer(0, _constantBuffer);
            context.PSSetSampler(0, _sampler);
            context.PSSetShaderResource(1, _audioView!);
            context.RSSetViewport(0, 0, _renderWidth, _renderHeight, 0, 1);

            var target = _feedback[current]!;
            var previous = _feedback[1 - current]!;

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
            DrawPass(_backBufferView!, shown.ShaderResource, _presentShader!, null);
            Present();
            current = 1 - current;

            frames++;
            if (now - frameRateStart >= 1.0)
            {
                FrameRateMeasured?.Invoke(new FrameStats(frames / (now - frameRateStart), _renderWidth, _renderHeight, _hdrActive));
                frames = 0;
                frameRateStart = now;
            }
        }
    }

    private void DrawPreset(RenderTarget destination, RenderTarget source, int presetIndex)
    {
        DrawPass(destination.RenderTargetView, source.ShaderResource, _slots[presetIndex].Shader!, null);
    }

    private void DrawPass(ID3D11RenderTargetView destination, ID3D11ShaderResourceView source, ID3D11PixelShader shader, ID3D11ShaderResourceView? extra)
    {
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(2, null!);
        context.OMSetRenderTargets(destination, null);
        context.PSSetShaderResource(0, source);
        if (extra is not null)
        {
            context.PSSetShaderResource(2, extra);
        }

        context.PSSetShader(shader);
        context.Draw(3, 0);
        context.PSSetShaderResource(0, null!);
        context.PSSetShaderResource(2, null!);
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
            // Switch on a beat once enough time has passed, or regardless a little later.
            var waited = now - _lastSwitch;
            var interval = _autoCycleSeconds;
            if ((waited > interval && _analyzer.Beat > 0.9f) || waited > interval + 8)
            {
                var next = RandomReadyPreset();
                if (next >= 0)
                {
                    _requestedPreset = next;
                    StartTransition(next, now);
                    PresetChanged?.Invoke(next);
                }
            }
        }

        if (_fromPreset < 0)
        {
            return 1f;
        }

        var t = Math.Clamp((now - _transitionStart) / _transitionSeconds, 0.0, 1.0);
        if (t >= 1.0)
        {
            _fromPreset = -1;
            return 1f;
        }

        return (float)(t * t * (3 - 2 * t));
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
        for (var index = 0; index < _slots.Length; index++)
        {
            if (index != _activePreset && IsReady(index))
            {
                ready.Add(index);
            }
        }

        return ready.Count == 0 ? -1 : ready[_random.Next(ready.Count)];
    }

    private void StartTransition(int presetIndex, double now)
    {
        _fromPreset = _activePreset;
        _activePreset = presetIndex;
        _transitionStart = now;
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
            return (windowWidth, windowHeight);
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
        foreach (var slot in _slots)
        {
            slot.Shader?.Dispose();
            slot.Shader = null;
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

        public PresetSlot(PresetSource source) => Source = source;

        public PresetSource Source { get; }
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
