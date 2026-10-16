using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Draws the visualizer with Direct3D 11 on its own thread. Each frame the
/// current preset reads the previous frame (feedback), draws on top of it,
/// and the result is bloomed and tone-mapped onto the window.
/// </summary>
public sealed class Renderer : IDisposable
{
    private const Format FeedbackFormat = Format.R16G16B16A16_Float;
    private const Format BackBufferFormat = Format.B8G8R8A8_UNorm;
    private const int ConstantCount = 16;
    private const double TransitionSeconds = 3.0;
    private const double AutoCycleSeconds = 25.0;

    // Transition styles the engine's blend shader knows (see Engine.hlsl). The morphs (4-7)
    // look the most natural, so they come up twice as often as the reveals (1-3) and the crossfade (0).
    private static readonly int[] TransitionStyles = { 0, 1, 2, 3, 4, 4, 5, 5, 6, 6, 7, 7 };

    private readonly IntPtr _hwnd;
    private readonly AudioAnalyzer _analyzer;
    private readonly IReadOnlyList<PresetSource> _presetSources;
    private readonly Thread _thread;
    private readonly float[] _constants = new float[ConstantCount];
    private readonly List<(string Name, ID3D11PixelShader Shader)> _presets = new();
    private readonly Random _random = new();
    private volatile bool _running;
    private volatile int _requestedPreset;
    private volatile bool _autoCycle = true;
    private int _activePreset;
    private int _fromPreset = -1;
    private double _transitionStart;
    private double _lastSwitch;
    private int _transitionStyle;

    private IDXGIFactory2? _factory;
    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _backBufferView;
    private readonly FeedbackTarget?[] _feedback = new FeedbackTarget?[2];
    private readonly FeedbackTarget?[] _transitionTargets = new FeedbackTarget?[2];
    private ID3D11Texture2D? _audioTexture;
    private ID3D11ShaderResourceView? _audioView;
    private ID3D11Buffer? _constantBuffer;
    private ID3D11SamplerState? _sampler;
    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _presentShader;
    private ID3D11PixelShader? _blendShader;
    private int _width;
    private int _height;

    public Renderer(IntPtr hwnd, AudioAnalyzer analyzer)
    {
        _hwnd = hwnd;
        _analyzer = analyzer;
        _presetSources = PresetLibrary.LoadPresets();
        _thread = new Thread(Run) { IsBackground = true, Name = "Visualizer renderer" };
    }

    /// <summary>Raised (on the render thread) once the presets are compiled, with their names.</summary>
    public event Action<IReadOnlyList<string>>? PresetsReady;

    /// <summary>Raised (on the render thread) when a preset fails to compile; it is skipped.</summary>
    public event Action<string>? PresetFailed;

    /// <summary>Raised (on the render thread) when auto-cycle moves to a new preset.</summary>
    public event Action<int>? PresetChanged;

    /// <summary>Raised (on the render thread) if the GPU can't be used at all.</summary>
    public event Action<string>? Failed;

    /// <summary>Index into the list given by <see cref="PresetsReady"/>.</summary>
    public int PresetIndex
    {
        get => _requestedPreset;
        set => _requestedPreset = value;
    }

    /// <summary>When on, moves to a random preset every so often, on a beat.</summary>
    public bool AutoCycle
    {
        get => _autoCycle;
        set => _autoCycle = value;
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
        try
        {
            CreateDevice();
            CreateResources();
            CompileShaders();
            RenderLoop();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(ex.Message);
        }
        finally
        {
            ReleaseAll();
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

        var (width, height) = GetClientSize();
        _width = width;
        _height = height;
        var description = new SwapChainDescription1(
            (uint)_width, (uint)_height, BackBufferFormat, false, Usage.RenderTargetOutput, 2,
            Scaling.Stretch, SwapEffect.FlipDiscard, AlphaMode.Ignore, SwapChainFlags.None);
        _swapChain = _factory.CreateSwapChainForHwnd(_device!, _hwnd, description, null, null);
        _factory.MakeWindowAssociation(_hwnd, WindowAssociationFlags.IgnoreAll);
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

        CreateSizeDependentResources();
    }

    private void CreateSizeDependentResources()
    {
        var device = _device!;
        using (var backBuffer = _swapChain!.GetBuffer<ID3D11Texture2D>(0))
        {
            _backBufferView = device.CreateRenderTargetView(backBuffer, null);
        }

        for (var i = 0; i < _feedback.Length; i++)
        {
            _feedback[i] = new FeedbackTarget(device, _context!, _width, _height);
            _transitionTargets[i] = new FeedbackTarget(device, _context!, _width, _height);
        }
    }

    private void ReleaseSizeDependentResources()
    {
        _context?.ClearState();
        _backBufferView?.Dispose();
        _backBufferView = null;
        for (var i = 0; i < _feedback.Length; i++)
        {
            _feedback[i]?.Dispose();
            _feedback[i] = null;
            _transitionTargets[i]?.Dispose();
            _transitionTargets[i] = null;
        }

        _context?.Flush();
    }

    private void CompileShaders()
    {
        var device = _device!;
        var common = PresetLibrary.ReadShader("Common.hlsli");
        var engine = common + "\n" + PresetLibrary.ReadShader("Engine.hlsl");
        _vertexShader = device.CreateVertexShader(Compile(engine, "VSMain", "Engine.hlsl", "vs_5_0"), null);
        _presentShader = device.CreatePixelShader(Compile(engine, "PSPresent", "Engine.hlsl", "ps_5_0"), null);
        _blendShader = device.CreatePixelShader(Compile(engine, "PSBlend", "Engine.hlsl", "ps_5_0"), null);

        var presetMain = PresetLibrary.ReadShader("PresetMain.hlsli");
        foreach (var preset in _presetSources)
        {
            try
            {
                var source = common + "\n#line 1\n" + preset.Code + "\n" + presetMain;
                var bytecode = Compile(source, "PSMain", Path.GetFileName(preset.FilePath), "ps_5_0");
                _presets.Add((preset.Name, device.CreatePixelShader(bytecode, null)));
            }
            catch (Exception ex)
            {
                PresetFailed?.Invoke($"{preset.Name}: {ex.Message}");
            }
        }

        PresetsReady?.Invoke(_presets.Select(p => p.Name).ToList());
        if (_presets.Count == 0)
        {
            throw new InvalidOperationException("No visual presets could be loaded.");
        }
    }

    private static byte[] Compile(string source, string entryPoint, string sourceName, string profile)
    {
        var result = Compiler.Compile(source, entryPoint, sourceName, profile, out Blob blob, out Blob errors);
        try
        {
            if (result.Failure || blob is null)
            {
                throw new InvalidOperationException(errors?.AsString()?.Trim() ?? $"Shader compile failed ({result.Code:X8}).");
            }

            return blob.AsBytes();
        }
        finally
        {
            blob?.Dispose();
            errors?.Dispose();
        }
    }

    private void RenderLoop()
    {
        var context = _context!;
        var clock = Stopwatch.StartNew();
        var lastTime = 0.0;
        var current = 0;

        while (_running)
        {
            var now = clock.Elapsed.TotalSeconds;
            var deltaTime = (float)Math.Clamp(now - lastTime, 0.0, 0.1);
            lastTime = now;

            var (width, height) = GetClientSize();
            if (width <= 1 || height <= 1)
            {
                // Minimized or hidden: nothing to draw.
                Thread.Sleep(50);
                continue;
            }

            if (width != _width || height != _height)
            {
                Resize(width, height);
            }

            _analyzer.Update(deltaTime);
            var progress = UpdatePresetChoice(now);

            _constants[0] = _width;
            _constants[1] = _height;
            _constants[2] = (float)now;
            _constants[3] = deltaTime;
            _constants[4] = _analyzer.Bass;
            _constants[5] = _analyzer.Mid;
            _constants[6] = _analyzer.Treble;
            _constants[7] = _analyzer.Beat;
            _constants[8] = _analyzer.Level;
            _constants[9] = _analyzer.BeatCount;
            _constants[10] = (float)_width / _height;
            _constants[12] = progress;
            _constants[13] = _transitionStyle;
            context.UpdateSubresource(_constants, _constantBuffer!, 0, 0, 0, null);
            context.UpdateSubresource(_analyzer.TextureData, _audioTexture!, 0, AudioAnalyzer.TextureWidth * sizeof(float), 0, null);

            context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            context.VSSetShader(_vertexShader);
            context.PSSetConstantBuffer(0, _constantBuffer);
            context.PSSetSampler(0, _sampler);
            context.PSSetShaderResource(1, _audioView);
            context.RSSetViewport(0, 0, _width, _height, 0, 1);

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
                context.PSSetShaderResource(0, null!);
                context.OMSetRenderTargets(target.RenderTarget, null);
                context.PSSetShaderResource(0, _transitionTargets[0]!.ShaderResource);
                context.PSSetShaderResource(2, _transitionTargets[1]!.ShaderResource);
                context.PSSetShader(_blendShader);
                context.Draw(3, 0);
                context.PSSetShaderResource(2, null!);
            }

            // Pass 2: bloom and tone-map the result onto the window.
            context.PSSetShaderResource(0, null!);
            context.OMSetRenderTargets(_backBufferView!, null);
            context.PSSetShaderResource(0, target.ShaderResource);
            context.PSSetShader(_presentShader);
            context.Draw(3, 0);
            context.PSSetShaderResource(0, null!);

            _swapChain!.Present(1, PresentFlags.None).CheckError();
            current = 1 - current;
        }
    }

    private void DrawPreset(FeedbackTarget destination, FeedbackTarget source, int presetIndex)
    {
        var context = _context!;
        context.PSSetShaderResource(0, null!);
        context.OMSetRenderTargets(destination.RenderTarget, null);
        context.PSSetShaderResource(0, source.ShaderResource);
        context.PSSetShader(_presets[presetIndex].Shader);
        context.Draw(3, 0);
    }

    /// <summary>
    /// Starts transitions for preset changes (from the UI or auto-cycle) and
    /// returns how far through the current transition we are, 0..1.
    /// </summary>
    private float UpdatePresetChoice(double now)
    {
        var requested = Math.Clamp(_requestedPreset, 0, _presets.Count - 1);
        if (requested != _activePreset)
        {
            StartTransition(requested, now);
        }
        else if (_autoCycle && _fromPreset < 0 && _presets.Count > 1)
        {
            // Switch on a beat once enough time has passed, or regardless a little later.
            var waited = now - _lastSwitch;
            if ((waited > AutoCycleSeconds && _analyzer.Beat > 0.9f) || waited > AutoCycleSeconds + 8)
            {
                var next = _random.Next(_presets.Count - 1);
                if (next >= _activePreset)
                {
                    next++;
                }

                _requestedPreset = next;
                StartTransition(next, now);
                PresetChanged?.Invoke(next);
            }
        }

        if (_fromPreset < 0)
        {
            return 1f;
        }

        var t = Math.Clamp((now - _transitionStart) / TransitionSeconds, 0.0, 1.0);
        if (t >= 1.0)
        {
            _fromPreset = -1;
            return 1f;
        }

        return (float)(t * t * (3 - 2 * t));
    }

    private void StartTransition(int presetIndex, double now)
    {
        _fromPreset = _activePreset;
        _activePreset = presetIndex;
        _transitionStart = now;
        _lastSwitch = now;
        _transitionStyle = TransitionStyles[_random.Next(TransitionStyles.Length)];
    }

    private void Resize(int width, int height)
    {
        ReleaseSizeDependentResources();
        _width = width;
        _height = height;
        _swapChain!.ResizeBuffers(2, (uint)width, (uint)height, BackBufferFormat, SwapChainFlags.None).CheckError();
        CreateSizeDependentResources();
    }

    private (int Width, int Height) GetClientSize()
    {
        return GetClientRect(_hwnd, out var rect)
            ? (Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top))
            : (1, 1);
    }

    private void ReleaseAll()
    {
        ReleaseSizeDependentResources();
        foreach (var (_, shader) in _presets)
        {
            shader.Dispose();
        }

        _presets.Clear();
        _presentShader?.Dispose();
        _blendShader?.Dispose();
        _vertexShader?.Dispose();
        _sampler?.Dispose();
        _constantBuffer?.Dispose();
        _audioView?.Dispose();
        _audioTexture?.Dispose();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _factory?.Dispose();
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

    /// <summary>One of the two ping-pong buffers the feedback effect reads from and writes to.</summary>
    private sealed class FeedbackTarget : IDisposable
    {
        public FeedbackTarget(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
        {
            Texture = device.CreateTexture2D(
                FeedbackFormat, (uint)width, (uint)height, 1, 1, null,
                BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceOptionFlags.None,
                ResourceUsage.Default, CpuAccessFlags.None);
            RenderTarget = device.CreateRenderTargetView(Texture, null);
            ShaderResource = device.CreateShaderResourceView(Texture, null);
            context.ClearRenderTargetView(RenderTarget, new Color4(0f, 0f, 0f, 1f));
        }

        public ID3D11Texture2D Texture { get; }

        public ID3D11RenderTargetView RenderTarget { get; }

        public ID3D11ShaderResourceView ShaderResource { get; }

        public void Dispose()
        {
            ShaderResource.Dispose();
            RenderTarget.Dispose();
            Texture.Dispose();
        }
    }
}
