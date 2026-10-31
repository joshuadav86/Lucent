using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visuals;
using SharpMetal.Metal;
using Lucent.Audio;
using Lucent.Visualizer;

namespace Lucent.Mac.Visualizer;

/// <summary>
/// Hosts the native view Metal draws the visuals into inside the Avalonia layout, and owns the engine
/// and renderer that draw there. The Mac version of the Windows app's VisualizerHost, with the same
/// members, so the windows around it use it the same way.
/// </summary>
public sealed class VisualizerHost : Border, IDisposable
{
    private readonly AudioAnalyzer _analyzer;
    private readonly DispatcherTimer _displayTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private VisualEngine? _engine;
    private MetalView? _view;
    private MetalRenderer? _renderer;
    private bool _started;
    private bool _disposed;
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
    private IReadOnlyList<PresetSource> _presets = Array.Empty<PresetSource>();
    private DisplayInfo _display = DisplayInfo.Standard;

    public VisualizerHost(AudioCapture capture)
    {
        _analyzer = new AudioAnalyzer(capture);
        Background = Brushes.Black;
        Focusable = false;
        _displayTimer.Tick += (_, _) => CheckDisplay();
        AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        AddHandler(PointerExitedEvent, (_, _) => Pointer.Left(), handledEventsToo: true);
    }

    /// <summary>The song that's playing and its bars, which Auto uses to change visual on a downbeat.</summary>
    public SongClock SongClock { get; } = new();

    /// <summary>The mouse over the visuals, which pushes a Particles visual's dots aside.</summary>
    public PointerStir Pointer { get; } = new();

    /// <summary>Raised on the UI thread with the names of the visuals that loaded.</summary>
    public event EventHandler<IReadOnlyList<string>>? PresetsLoaded;

    /// <summary>Raised on the UI thread with a message when something goes wrong.</summary>
    public event EventHandler<string>? ProblemReported;

    public event EventHandler? DoubleClicked;

    /// <summary>Raised when the visuals are right-clicked (or Control-clicked), for their menu.</summary>
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
            if (_engine is not null)
            {
                _engine.AutoCycleChoices = IndicesOf(_engine.PresetNames, value);
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
    public IReadOnlyList<PresetSource> Presets => _presets;

    /// <summary>The visual to start with (by name), usually the one showing when the app last closed.</summary>
    public string? StartPresetName { get; set; }

    /// <summary>How many pixels to draw, e.g. 3840 x 2160 for 4K; 0 matches the window.</summary>
    public int TargetPixels
    {
        get => _targetPixels;
        set
        {
            _targetPixels = value;
            if (_engine is not null)
            {
                _engine.TargetPixels = value;
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
            if (_engine is not null)
            {
                _engine.AutoResolution = value;
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
            if (_engine is not null)
            {
                _engine.Quality = value;
            }
        }
    }

    public float BloomStrength
    {
        get => _bloomStrength;
        set
        {
            _bloomStrength = value;
            if (_engine is not null)
            {
                _engine.BloomStrength = value;
            }
        }
    }

    public float TransitionSeconds
    {
        get => _transitionSeconds;
        set
        {
            _transitionSeconds = value;
            if (_engine is not null)
            {
                _engine.TransitionSeconds = value;
            }
        }
    }

    public float AutoCycleSeconds
    {
        get => _autoCycleSeconds;
        set
        {
            _autoCycleSeconds = value;
            if (_engine is not null)
            {
                _engine.AutoCycleSeconds = value;
            }
        }
    }

    public bool MorphTransitionsOnly
    {
        get => _morphTransitionsOnly;
        set
        {
            _morphTransitionsOnly = value;
            if (_engine is not null)
            {
                _engine.MorphTransitionsOnly = value;
            }
        }
    }

    /// <summary>Draw in HDR (EDR) when the screen can show it.</summary>
    public bool Hdr
    {
        get => _hdr;
        set
        {
            _hdr = value;
            UpdateHdr();
        }
    }

    public bool AutoCycle
    {
        get => _autoCycle;
        set
        {
            _autoCycle = value;
            if (_engine is not null)
            {
                _engine.AutoCycle = value;
            }
        }
    }

    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            _presetIndex = value;
            if (_engine is not null)
            {
                _engine.PresetIndex = value;
            }
        }
    }

    /// <summary>What the screen under the visuals can do, as last checked.</summary>
    public DisplayInfo Display => _display;

    /// <summary>
    /// Starts the visuals the first time the host is put in a window, like the Windows host does when
    /// its native window is made: loads the visuals, starts compiling them and makes the view to draw in.
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!_started && !_disposed)
        {
            _started = true;
            Start();
        }

        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged += OnWindowPropertyChanged;
        }

        _displayTimer.Start();
        UpdateVisible();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            window.PropertyChanged -= OnWindowPropertyChanged;
        }

        _displayTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void Start()
    {
        var files = new MetalShaderFiles();
        var problem = (string?)null;
        if (MetalSupport.TryCreateDevice() is not { } device)
        {
            problem = "This Mac can't run Metal, so the visuals can't be shown. Music still plays.";
        }
        else
        {
            try
            {
                _engine = new VisualEngine(device, _analyzer, files, MTLPixelFormat.BGRA8Unorm) { SongClock = SongClock };
            }
            catch (Exception ex)
            {
                problem = ex.Message;
            }
        }

        // The picker lists the visuals even when they can't be drawn, as on Windows.
        _presets = _engine?.Presets ?? LoadPresets(files);
        var names = _presets.Select(preset => preset.Name).ToList();
        var startIndex = StartPresetName is null ? -1 : names.FindIndex(name => string.Equals(name, StartPresetName, StringComparison.OrdinalIgnoreCase));
        if (startIndex >= 0)
        {
            _presetIndex = startIndex;
        }

        Dispatcher.UIThread.Post(() => PresetsLoaded?.Invoke(this, names));
        if (_engine is null)
        {
            // (The screenshot tool runs where there's no Metal at all; it shows the window as it looks with visuals.)
            if (problem is not null && OperatingSystem.IsMacOS())
            {
                Dispatcher.UIThread.Post(() => ProblemReported?.Invoke(this, $"The GPU visualizer stopped: {problem}"));
            }

            return;
        }

        var engine = _engine;
        engine.PresetIndex = _presetIndex;
        engine.AutoCycle = _autoCycle;
        engine.TargetPixels = _targetPixels;
        engine.AutoResolution = _autoResolution;
        engine.Quality = _quality;
        engine.BloomStrength = _bloomStrength;
        engine.TransitionSeconds = _transitionSeconds;
        engine.AutoCycleSeconds = _autoCycleSeconds;
        engine.MorphTransitionsOnly = _morphTransitionsOnly;
        engine.AutoCycleChoices = IndicesOf(engine.PresetNames, _autoCycleNames);
        engine.Pointer = Pointer;
        engine.PresetChanged += index => Dispatcher.UIThread.Post(() =>
        {
            _presetIndex = index;
            PresetChanged?.Invoke(this, index);
        });
        engine.PresetFailed += message => Dispatcher.UIThread.Post(() => ProblemReported?.Invoke(this, $"A visual didn't load. {message}"));

        if (engine.Presets.Count == 0)
        {
            Dispatcher.UIThread.Post(() => ProblemReported?.Invoke(this, "The GPU visualizer stopped: No visual presets were found."));
            return;
        }

        _view = new MetalView(engine);
        _view.RendererStarted += (_, renderer) =>
        {
            _renderer = renderer;
            renderer.Failed += message => Dispatcher.UIThread.Post(() => ProblemReported?.Invoke(this, $"The GPU visualizer stopped: {message}"));
            renderer.FrameRateMeasured += stats => Dispatcher.UIThread.Post(() => FrameRateMeasured?.Invoke(this, stats));
            UpdateVisible();
            CheckDisplay();
            UpdateHdr();
        };
        Child = _view;
    }

    /// <summary>
    /// Loads the visuals again (after MilkDrop presets are imported, say) and carries on with the one
    /// showing, or with <paramref name="show"/> if it's given and loads. The engine starts over, so
    /// there's a moment of black.
    /// </summary>
    public void ReloadPresets(string? show = null)
    {
        if (!_started || _disposed)
        {
            return;
        }

        if (show is not null)
        {
            StartPresetName = show;
        }
        else if (_presetIndex >= 0 && _presetIndex < _presets.Count)
        {
            StartPresetName = _presets[_presetIndex].Name;
        }

        _view?.Renderer?.Dispose();
        _renderer = null;
        _view = null;
        Child = null;
        _engine?.Dispose();
        _engine = null;
        Start();
        UpdateVisible();
    }

    private static IReadOnlyList<PresetSource> LoadPresets(MetalShaderFiles files)
    {
        try
        {
            return files.LoadPresets();
        }
        catch (Exception)
        {
            return Array.Empty<PresetSource>();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Window.WindowStateProperty)
        {
            UpdateVisible();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            UpdateVisible();
        }
    }

    /// <summary>Nothing is drawn while the window is hidden or minimized.</summary>
    private void UpdateVisible()
    {
        if (_renderer is not null)
        {
            // Hidden in its window too while a video shows there instead.
            _renderer.Visible = IsVisible && TopLevel.GetTopLevel(this) is Window { IsVisible: true } window && window.WindowState != WindowState.Minimized;
        }
    }

    /// <summary>
    /// Once a second, checks whether the screen under the window can show HDR and how bright it can go
    /// (the window can move to another screen, and the brightness can change).
    /// </summary>
    private void CheckDisplay()
    {
        if (_view is null || _view.NativeView == IntPtr.Zero || !OperatingSystem.IsMacOS())
        {
            return;
        }

        DisplayInfo found;
        try
        {
            var window = Interop.ObjC.Send(_view.NativeView, "window");
            found = DisplayInfo.ForWindow(window);
        }
        catch (Exception)
        {
            // Asked again in a second.
            return;
        }

        if (found != _display)
        {
            _display = found;
            if (_engine is not null)
            {
                _engine.Display = found;
            }

            UpdateHdr();
            DisplayChanged?.Invoke(this, found);
        }
    }

    private void UpdateHdr()
    {
        if (_renderer is not null)
        {
            _renderer.Hdr = _hdr && _display.HdrOn;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed && e.ClickCount == 2 && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            DoubleClicked?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var point = e.GetPosition(this);
        Pointer.MovedTo(point.X / size.Width, point.Y / size.Height);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        switch (e.InitialPressMouseButton)
        {
            // On a Mac, Control-click is a right-click.
            case MouseButton.Left when e.KeyModifiers.HasFlag(KeyModifiers.Control):
            case MouseButton.Right:
                RightClicked?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _displayTimer.Stop();
        _view?.Renderer?.Dispose();
        _renderer = null;
        Child = null;
        _engine?.Dispose();
        _engine = null;
    }
}
