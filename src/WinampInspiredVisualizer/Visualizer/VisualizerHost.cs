using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Hosts a native child window inside the WPF layout for Direct3D to draw into,
/// and owns the renderer that draws there.
/// </summary>
public sealed class VisualizerHost : HwndHost
{
    private const string WindowClassName = "WinampInspiredVisualizerSurface";
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int CS_DBLCLKS = 0x0008;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int IDC_ARROW = 32512;

    private static bool _classRegistered;

    private readonly AudioAnalyzer _analyzer;
    private Renderer? _renderer;
    private int _presetIndex;
    private bool _autoCycle = true;
    private int _targetPixels;
    private bool _autoResolution;
    private float _quality = 1f;
    private float _bloomStrength = 1f;
    private float _transitionSeconds = 3f;
    private float _autoCycleSeconds = 25f;
    private bool _morphTransitionsOnly;
    private bool _hdr;
    private IReadOnlyCollection<string>? _autoCycleNames;

    public VisualizerHost(AudioCapture capture)
    {
        _analyzer = new AudioAnalyzer(capture);
    }

    /// <summary>The song that's playing and its bars, which Auto uses to change visual on a downbeat.</summary>
    public SongClock SongClock { get; } = new();

    /// <summary>Raised on the UI thread with the names of the visuals that loaded.</summary>
    public event EventHandler<IReadOnlyList<string>>? PresetsLoaded;

    /// <summary>Raised on the UI thread with a message when something goes wrong.</summary>
    public event EventHandler<string>? ProblemReported;

    public event EventHandler? DoubleClicked;

    /// <summary>Raised when the left mouse button is let go over the visuals, including the second click of a double-click.</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the visuals are right-clicked, for their menu.</summary>
    public event EventHandler? RightClicked;

    /// <summary>Raised on the UI thread when auto-cycle switches to another preset.</summary>
    public event EventHandler<int>? PresetChanged;

    /// <summary>Raised on the UI thread about once a second with the frame rate and drawing size.</summary>
    public event EventHandler<FrameStats>? FrameRateMeasured;

    /// <summary>Raised on the UI thread when the screen's HDR support is found or changes.</summary>
    public event EventHandler<DisplayInfo>? DisplayChanged;

    /// <summary>The visuals Auto picks from (by name), or null for all of them.</summary>
    public IReadOnlyCollection<string>? AutoCycleNames
    {
        get => _autoCycleNames;
        set
        {
            _autoCycleNames = value;
            if (_renderer is not null)
            {
                _renderer.AutoCycleChoices = IndicesOf(_renderer.PresetNames, value);
            }
        }
    }

    private static IReadOnlyCollection<int>? IndicesOf(IReadOnlyList<string> names, IReadOnlyCollection<string>? chosen)
    {
        if (chosen is null)
        {
            return null;
        }

        var wanted = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
        return Enumerable.Range(0, names.Count).Where(index => wanted.Contains(names[index])).ToList();
    }

    /// <summary>Every visual (with its family and tags), in the order <see cref="PresetIndex"/> counts them; empty until they load.</summary>
    public IReadOnlyList<PresetSource> Presets => _renderer?.Presets ?? Array.Empty<PresetSource>();

    /// <summary>The visual to start with (by name), usually the one showing when the app last closed.</summary>
    public string? StartPresetName { get; set; }

    /// <summary>How many pixels to draw, e.g. 3840 x 2160 for 4K; 0 matches the window.</summary>
    public int TargetPixels
    {
        get => _targetPixels;
        set
        {
            _targetPixels = value;
            if (_renderer is not null)
            {
                _renderer.TargetPixels = value;
            }
        }
    }

    /// <summary>With <see cref="TargetPixels"/> at 0, draw visuals that can't keep up at fewer pixels until they can.</summary>
    public bool AutoResolution
    {
        get => _autoResolution;
        set
        {
            _autoResolution = value;
            if (_renderer is not null)
            {
                _renderer.AutoResolution = value;
            }
        }
    }

    /// <summary>Detail level for visuals: 0 low, 1 normal, 2 high, 3 ultra.</summary>
    public float Quality
    {
        get => _quality;
        set
        {
            _quality = value;
            if (_renderer is not null)
            {
                _renderer.Quality = value;
            }
        }
    }

    public float BloomStrength
    {
        get => _bloomStrength;
        set
        {
            _bloomStrength = value;
            if (_renderer is not null)
            {
                _renderer.BloomStrength = value;
            }
        }
    }

    public float TransitionSeconds
    {
        get => _transitionSeconds;
        set
        {
            _transitionSeconds = value;
            if (_renderer is not null)
            {
                _renderer.TransitionSeconds = value;
            }
        }
    }

    public float AutoCycleSeconds
    {
        get => _autoCycleSeconds;
        set
        {
            _autoCycleSeconds = value;
            if (_renderer is not null)
            {
                _renderer.AutoCycleSeconds = value;
            }
        }
    }

    public bool MorphTransitionsOnly
    {
        get => _morphTransitionsOnly;
        set
        {
            _morphTransitionsOnly = value;
            if (_renderer is not null)
            {
                _renderer.MorphTransitionsOnly = value;
            }
        }
    }

    /// <summary>Draw in HDR when the screen has HDR turned on in Windows.</summary>
    public bool Hdr
    {
        get => _hdr;
        set
        {
            _hdr = value;
            if (_renderer is not null)
            {
                _renderer.Hdr = value;
            }
        }
    }

    public bool AutoCycle
    {
        get => _autoCycle;
        set
        {
            _autoCycle = value;
            if (_renderer is not null)
            {
                _renderer.AutoCycle = value;
            }
        }
    }

    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            _presetIndex = value;
            if (_renderer is not null)
            {
                _renderer.PresetIndex = value;
            }
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        RegisterWindowClass();
        var hwnd = CreateWindowEx(
            0, WindowClassName, string.Empty, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Couldn't create the visualizer window.");
        }

        StartRenderer(hwnd);
        return new HandleRef(this, hwnd);
    }

    /// <summary>
    /// Loads the visuals again (after MilkDrop presets are imported, say) and carries on with the one
    /// showing, or with <paramref name="show"/> if it's given and loads. The renderer starts over, so
    /// there's a moment of black.
    /// </summary>
    public void ReloadPresets(string? show = null)
    {
        if (_renderer is not { } renderer)
        {
            return;
        }

        var names = renderer.PresetNames;
        if (show is not null)
        {
            StartPresetName = show;
        }
        else if (_presetIndex >= 0 && _presetIndex < names.Count)
        {
            StartPresetName = names[_presetIndex];
        }

        // The new renderer draws into the same window, so the old one has to have let go of it first.
        _renderer = null;
        renderer.Stop(TimeSpan.FromSeconds(10));
        StartRenderer(Handle);
    }

    private void StartRenderer(IntPtr hwnd)
    {
        var dispatcher = Dispatcher;
        _renderer = new Renderer(hwnd, _analyzer);
        var startIndex = StartPresetName is null ? -1 : IndexOfName(_renderer.PresetNames, StartPresetName);
        if (startIndex >= 0)
        {
            _presetIndex = startIndex;
        }

        _renderer.PresetIndex = _presetIndex;
        _renderer.AutoCycle = _autoCycle;
        _renderer.TargetPixels = _targetPixels;
        _renderer.AutoResolution = _autoResolution;
        _renderer.Quality = _quality;
        _renderer.BloomStrength = _bloomStrength;
        _renderer.TransitionSeconds = _transitionSeconds;
        _renderer.AutoCycleSeconds = _autoCycleSeconds;
        _renderer.MorphTransitionsOnly = _morphTransitionsOnly;
        _renderer.Hdr = _hdr;
        _renderer.AutoCycleChoices = IndicesOf(_renderer.PresetNames, _autoCycleNames);
        _renderer.SongClock = SongClock;
        _renderer.PresetChanged += index => dispatcher.InvokeAsync(() =>
        {
            _presetIndex = index;
            PresetChanged?.Invoke(this, index);
        });
        _renderer.PresetsReady += names => dispatcher.InvokeAsync(() => PresetsLoaded?.Invoke(this, names));
        _renderer.PresetFailed += message => dispatcher.InvokeAsync(() => ProblemReported?.Invoke(this, $"A visual didn't load. {message}"));
        _renderer.Failed += message => dispatcher.InvokeAsync(() => ProblemReported?.Invoke(this, $"The GPU visualizer stopped: {message}"));
        _renderer.DeviceReset += () => dispatcher.InvokeAsync(() => ProblemReported?.Invoke(this,
            "The graphics card reset, so the visuals restarted. If it keeps happening, lower the resolution or detail in Options."));
        _renderer.FrameRateMeasured += stats => dispatcher.InvokeAsync(() => FrameRateMeasured?.Invoke(this, stats));
        _renderer.DisplayChanged += display => dispatcher.InvokeAsync(() => DisplayChanged?.Invoke(this, display));
        _renderer.Start();
    }

    private static int IndexOfName(IReadOnlyList<string> names, string name)
    {
        for (var i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _renderer?.Dispose();
        _renderer = null;
        DestroyWindow(hwnd.Handle);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Direct3D covers the whole window; skipping the erase avoids flicker.
                handled = true;
                return new IntPtr(1);
            case WM_LBUTTONUP:
                Clicked?.Invoke(this, EventArgs.Empty);
                return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
            case WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                handled = true;
                return IntPtr.Zero;
            case WM_RBUTTONUP:
                RightClicked?.Invoke(this, EventArgs.Empty);
                handled = true;
                return IntPtr.Zero;
            default:
                return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }
    }

    private static void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        var windowClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            style = CS_DBLCLKS,
            // The window's own messages are handled by HwndHost.WndProc above,
            // so the class just needs the default handler.
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, new IntPtr(IDC_ARROW)),
            lpszClassName = WindowClassName,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Couldn't register the visualizer window class.");
        }

        _classRegistered = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
