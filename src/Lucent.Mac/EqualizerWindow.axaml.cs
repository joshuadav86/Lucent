using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// The graphic equalizer window: an on/off switch, a choice of band counts, a preamp, one slider
/// per band with the response curve drawn above, and presets. Open it with <see cref="Toggle"/>.
/// </summary>
public partial class EqualizerWindow : Window
{
    // The curve shows this many decibels above and below 0.
    private const double CurveRangeDb = 15;

    private static EqualizerWindow? _open;
    private static Bitmap? _icon;
    private static bool _iconLoaded;

    private readonly AudioPlayer _player;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<Slider> _bandSliders = new();
    private EqualizerBands _bands;
    private double _columnWidth;
    // The width it keeps when it opens from the player: the player's own, so it docks underneath
    // like Winamp's. Null for its natural width.
    private readonly double? _fitWidth;

    // The preamp column and the +12/0/−12 scale beside it, to the left of the band sliders.
    private const double PreampAndScaleWidth = 48 + 26 + 6;

    // Beyond this the sliders look scattered rather than spread out.
    private const double WidestColumn = 110;
    // How narrow columns may get to fit the player's width with 32 bands, and the knob they hold.
    private const double NarrowestFittedColumn = 12;
    private const double SliderKnobWidth = 30;
    // True while the sliders are being set from code (and during start-up), so their change
    // handlers stay quiet.
    private bool _updating = true;

    // ---- Glides: the sliders and the curve move to new settings instead of jumping ----

    // How long each column takes to settle, and the most the last column waits behind the first.
    private const double GlideSeconds = 0.34;
    private const double MostStagger = 0.22;
    private bool _gliding;
    private bool _glideFrameRequested;
    private DateTime _glideStart;
    private double _stagger;
    private double[] _glideFrom = Array.Empty<double>();
    private double[] _glideTo = Array.Empty<double>();
    private double _preampFrom;
    private double _preampTo;
    private EqualizerDesign? _curveFrom;   // the curve being left, or null to rise from flat
    private bool _revealColumns;
    private double _widthFrom;
    private double _widthTo;

    // The music's live spectrum, behind the response curve.
    private readonly EqualizerSpectrum _spectrum;

    // For the XAML previewer only.
    public EqualizerWindow()
        : this(new AudioPlayer(), new AppSettings(), null)
    {
    }

    private EqualizerWindow(AudioPlayer player, AppSettings settings, double? fitWidth)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        TitleIcon.Source = LucentIcon();
        _player = player;
        _settings = settings;
        _fitWidth = fitWidth;
        _bands = EqualizerBands.ForCount(Eq.BandCount);
        Eq.BandCount = _bands.Count;
        Eq.Gains = Equalizer.SliderGains(Eq, _bands).ToList();
        // The spectrum fills the response box under the curve, whatever size the box is.
        _spectrum = new EqualizerSpectrum(player.Capture);
        CurveFrame.Child = null;
        var layers = new Grid();
        layers.Children.Add(_spectrum);
        layers.Children.Add(CurveCanvas);
        CurveFrame.Child = layers;

        // Saving is held back until the sliders have been still for a moment.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };

        foreach (var count in EqualizerBands.Counts)
        {
            var button = new RadioButton
            {
                Theme = (ControlTheme)this.FindResource("SegmentButton")!,
                GroupName = "EqualizerBands",
                Content = count.ToString(CultureInfo.InvariantCulture),
                Tag = count,
                MinWidth = 30,
            };
            button.Click += OnBandCountChosen;
            BandCountBar.Children.Add(button);
        }

        PreampSlider.ValueChanged += OnPreampChanged;
        PreampSlider.AddHandler(PointerPressedEvent, OnPreampPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        PreampSlider.AddHandler(PointerWheelChangedEvent, (_, e) => Nudge(PreampSlider, e), RoutingStrategies.Tunnel);
        SliderArea.AddHandler(PointerPressedEvent, OnSliderAreaMouseDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PositionChanged += (_, _) => RememberPosition();
        Loaded += (_, _) => RedrawCurve();
        Closed += OnClosed;

        FitHeader();
        BuildBands();
        ShowSettings();
    }

    private EqualizerSettings Eq => _settings.Equalizer;

    /// <summary>The equalizer window, while it's open.</summary>
    public static EqualizerWindow? Current => _open;

    /// <summary>
    /// Opens the equalizer beside <paramref name="owner"/>, or closes it if it's already open.
    /// <paramref name="openChanged"/> hears whether it's open, now and whenever it's closed later,
    /// so a toggle button can stay in step.
    /// </summary>
    public static void Toggle(Window owner, AudioPlayer player, AppSettings settings, Action<bool>? openChanged = null)
    {
        if (_open is not null)
        {
            _open.Close();
            return;
        }

        Show(owner, player, settings, openChanged);
    }

    /// <summary>Opens the equalizer (or brings it to the front if it's already open).</summary>
    public static EqualizerWindow Show(Window owner, AudioPlayer player, AppSettings settings, Action<bool>? openChanged = null)
    {
        if (_open is not null)
        {
            _open.Activate();
            openChanged?.Invoke(true);
            return _open;
        }

        var main = owner as MainWindow;
        var window = Create(player, settings, main?.Bounds.Width);
        if (main is not null)
        {
            main.DockEqualizer(window);
        }
        else
        {
            window.PlaceOnScreen(owner);
        }

        window.Closed += (_, _) => openChanged?.Invoke(false);
        _open = window;
        window.Show(owner);
        openChanged?.Invoke(true);
        return window;
    }

    /// <summary>A new equalizer that isn't shown yet. (Show is the way to open one; the screenshots use this.)</summary>
    internal static EqualizerWindow Create(AudioPlayer player, AppSettings settings, double? fitWidth = null) =>
        new(player, settings, fitWidth);

    /// <summary>The music's spectrum behind the curve, for the screenshots to feed.</summary>
    internal EqualizerSpectrum Spectrum => _spectrum;

    /// <summary>Lucent's icon for a window's title bar, or null if it's missing from the app.</summary>
    internal static Bitmap? LucentIcon()
    {
        if (!_iconLoaded)
        {
            _iconLoaded = true;
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Lucent-64.png");
            try
            {
                _icon = File.Exists(path) ? new Bitmap(path) : null;
            }
            catch (Exception)
            {
                _icon = null;
            }
        }

        return _icon;
    }

    // ---- Layout ----

    private void BuildBands()
    {
        _bands = EqualizerBands.ForCount(Eq.BandCount);
        // The box spans the columns, each band's centre in the middle of its column.
        _spectrum.SetScale(_bands.Centres[0] / Math.Pow(2, _bands.SpacingOctaves / 2), _bands.SpacingOctaves * _bands.Count);
        // Narrower columns for more bands, so even 32 sliders fit a normal screen.
        var narrowest = _bands.Count switch
        {
            <= 10 => 40,
            <= 16 => 32,
            <= 24 => 26,
            _ => 22,
        };

        // With only a few bands the row of buttons along the top is the widest thing in the window,
        // so the sliders spread out to fill that width (up to a point; the rest is shared either side)
        // instead of bunching up on the left.
        if (_fitWidth is { } width)
        {
            // Docked under the player, the sliders share the player's width, however many there are.
            var fit = width - Frame.BorderThickness.Left - Frame.BorderThickness.Right - SliderArea.Margin.Left - SliderArea.Margin.Right - PreampAndScaleWidth;
            _columnWidth = Math.Clamp(Math.Floor(fit / _bands.Count), NarrowestFittedColumn, WidestColumn);
        }
        else
        {
            HeaderRow.Measure(Size.Infinity);
            var room = HeaderRow.DesiredSize.Width - SliderArea.Margin.Left - SliderArea.Margin.Right - PreampAndScaleWidth;
            _columnWidth = Math.Clamp(Math.Floor(room / _bands.Count), narrowest, Math.Max(narrowest, WidestColumn));
        }

        var sliderTheme = (ControlTheme)Resources["BandSlider"]!;
        var labelTheme = (ControlTheme)Resources["BandLabel"]!;
        BandPanel.Children.Clear();
        _bandSliders.Clear();
        for (var band = 0; band < _bands.Count; band++)
        {
            var label = _bands.Labels[band];
            var slider = new Slider
            {
                Theme = sliderTheme,
                Tag = band,
            };
            ToolTip.SetTip(slider, $"{SpokenFrequency(label)}. Double-click to reset.");
            slider.ValueChanged += OnBandChanged;
            slider.AddHandler(PointerPressedEvent, OnBandPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
            slider.AddHandler(PointerWheelChangedEvent, (sender, e) => Nudge((Slider)sender!, e), RoutingStrategies.Tunnel);

            // Sliders in very narrow columns are drawn thinner so their knobs don't touch.
            Control fader = slider;
            if (_columnWidth < SliderKnobWidth + 4)
            {
                fader = new LayoutTransformControl
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    LayoutTransform = new ScaleTransform(Math.Max(0.35, (_columnWidth - 4) / SliderKnobWidth), 1),
                    Child = slider,
                };
            }

            var text = new TextBlock { Theme = labelTheme, Text = label };
            if (_bands.Count >= 24 || _columnWidth < 30)
            {
                text.FontSize = 9;
            }

            // In very narrow columns every other label is left out so they don't run together; the
            // slider's tooltip still names its frequency.
            if (_columnWidth < 20 && band % 2 == 1)
            {
                text.Opacity = 0;
            }

            var column = new StackPanel
            {
                Width = _columnWidth,
                Background = Brushes.Transparent,
                Tag = band,
                RenderTransform = new TranslateTransform(),
            };
            column.Children.Add(fader);
            column.Children.Add(text);
            var index = band;
            column.PointerEntered += (_, _) => ShowBandStatus(index);
            column.PointerExited += (_, _) => ShowHint();
            BandPanel.Children.Add(column);
            _bandSliders.Add(slider);
        }

        CurveFrame.Width = _columnWidth * _bands.Count;
        FitToContent();
    }

    // Sized by hand rather than with SizeToContent, so the width can glide when the band count changes.
    private void FitToContent()
    {
        Frame.Measure(Size.Infinity);
        Width = _fitWidth ?? Math.Ceiling(Frame.DesiredSize.Width);
        Height = Math.Ceiling(Frame.DesiredSize.Height);
    }

    /// <summary>
    /// At the player's width the top row can be a little too wide, so the "Bands" label goes (the
    /// buttons keep their tooltip) and the band buttons move closer to the switch and presets.
    /// </summary>
    private void FitHeader()
    {
        if (_fitWidth is not { } width)
        {
            return;
        }

        var room = width - Frame.BorderThickness.Left - Frame.BorderThickness.Right - HeaderRow.Margin.Left - HeaderRow.Margin.Right;
        foreach (var step in new Action[] { () => BandsLabel.IsVisible = false, () => BandsGroup.Margin = new Thickness(8, 0, 8, 0) })
        {
            HeaderRow.Measure(Size.Infinity);
            if (HeaderRow.DesiredSize.Width <= room)
            {
                return;
            }

            step();
        }
    }

    private void PlaceOnScreen(Window owner)
    {
        if (Eq.WindowLeft is { } left && Eq.WindowTop is { } top && IsOnScreen(owner, left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    // Enough of the title bar has to be on a screen to drag the window back.
    private static bool IsOnScreen(Window owner, double left, double top)
    {
        var screens = owner.Screens.All;
        if (screens.Count == 0)
        {
            return true;
        }

        var all = screens.Select(screen => screen.Bounds).Aggregate((a, b) => a.Union(b));
        return left + 120 > all.X
            && left < all.Right - 60
            && top >= all.Y - 4
            && top < all.Bottom - 40;
    }

    private void RememberPosition()
    {
        if (WindowState == WindowState.Normal && IsLoaded)
        {
            Eq.WindowLeft = Position.X;
            Eq.WindowTop = Position.Y;
            SaveSoon();
        }
    }

    // ---- Showing the settings ----

    private void ShowSettings()
    {
        _updating = true;
        EnabledSwitch.IsChecked = Eq.Enabled;
        foreach (var button in BandCountBar.Children.OfType<RadioButton>())
        {
            button.IsChecked = (int)button.Tag! == _bands.Count;
        }

        PreampSlider.Value = Eq.PreampDb;
        for (var band = 0; band < _bandSliders.Count; band++)
        {
            _bandSliders[band].Value = Eq.Gains[band];
        }

        _updating = false;
        PresetLabel.Text = Eq.PresetName ?? "Custom";
        // The sliders still work while it's off (moving one turns it back on); they just look dimmer.
        SliderArea.Opacity = Eq.Enabled ? 1 : 0.55;
        ShowHint();
        RedrawCurve();
    }

    private void ShowHint()
    {
        StatusText.Text = "Double-click a slider to put it back to 0";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "FaintTextBrush");
    }

    private void ShowBandStatus(int band)
    {
        StatusText.Text = $"{SpokenFrequency(_bands.Labels[band])}: {FormatDb(Eq.Gains[band])}";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
    }

    private void ShowHeadroom(EqualizerDesign design)
    {
        HeadroomText.Text = Eq.Enabled && design.HeadroomDb >= 0.5
            ? $"Turned down {design.HeadroomDb:0} dB so boosts don't distort"
            : "";
    }

    private void RedrawCurve()
    {
        var design = _player.Equalizer.Preview;
        ShowHeadroom(design);

        var width = _columnWidth * _bands.Count;
        var height = CurveFrame.Height - 2;
        if (width <= 0)
        {
            return;
        }

        double Y(double db) => height / 2 - Math.Clamp(db, -CurveRangeDb, CurveRangeDb) * (height / 2 - 4) / CurveRangeDb;

        // Each band's centre sits right above its slider, so frequency runs evenly (in octaves)
        // across the columns.
        var ratio = Math.Pow(2, _bands.SpacingOctaves);
        var elapsed = _gliding ? (DateTime.UtcNow - _glideStart).TotalSeconds : double.MaxValue;
        var points = new List<Point>();
        for (var x = 0.0; x <= width; x += 2)
        {
            var hertz = _bands.Centres[0] * Math.Pow(ratio, x / _columnWidth - 0.5);
            var db = design.ResponseDb(hertz);
            if (_gliding)
            {
                // Each part of the curve moves with the slider under it.
                var from = _curveFrom?.ResponseDb(hertz) ?? 0;
                db = from + (db - from) * GlideProgress(x / _columnWidth - 0.5, elapsed);
            }

            points.Add(new Point(x, Y(db)));
        }

        var zero = Y(0);
        var line = new StreamGeometry();
        using (var context = line.Open())
        {
            context.BeginFigure(points[0], false);
            for (var i = 1; i < points.Count; i++)
            {
                context.LineTo(points[i]);
            }

            context.EndFigure(false);
        }

        var fill = new StreamGeometry();
        using (var context = fill.Open())
        {
            context.BeginFigure(new Point(0, zero), true);
            foreach (var point in points)
            {
                context.LineTo(point);
            }

            context.LineTo(new Point(points[^1].X, zero));
            context.EndFigure(true);
        }

        var zeroLine = new StreamGeometry();
        using (var context = zeroLine.Open())
        {
            context.BeginFigure(new Point(0, zero), false);
            context.LineTo(new Point(width, zero));
            context.EndFigure(false);
        }

        CurveLine.Data = line;
        CurveFill.Data = fill;
        ZeroLine.Data = zeroLine;
        CurveLine.SetResourceReference(Avalonia.Controls.Shapes.Shape.StrokeProperty, Eq.Enabled ? "AccentBrush" : "FaintTextBrush");
        CurveFill.IsVisible = Eq.Enabled;
    }

    // ---- Glides ----

    // The sliders' and curve's current look, for a glide away from it to begin from.
    private (double[] Gains, double Preamp, EqualizerDesign Curve) SnapshotForGlide()
    {
        var gains = _bandSliders.Select(slider => slider.Value).ToArray();
        var preamp = PreampSlider.Value;
        FinishGlide();
        return (gains, preamp, _player.Equalizer.Preview);
    }

    private void StartGlide(double[] from, double preampFrom, EqualizerDesign? curveFrom, bool revealColumns, double widthFrom)
    {
        _glideFrom = from;
        _glideTo = Eq.Gains.ToArray();
        _preampFrom = preampFrom;
        _preampTo = Eq.PreampDb;
        _curveFrom = curveFrom;
        _revealColumns = revealColumns;
        _widthTo = Width;
        _widthFrom = double.IsNaN(widthFrom) || widthFrom <= 0 ? _widthTo : widthFrom;
        _stagger = Math.Min(0.03, MostStagger / Math.Max(1, _bands.Count));

        // Nothing moves for people who've asked the Mac to reduce motion.
        if (ReduceMotion() || !IsLoaded)
        {
            FinishGlide();
            return;
        }

        _glideStart = DateTime.UtcNow;
        _gliding = true;
        OnGlideFrame();
        RequestGlideFrame();
    }

    // Once a screen frame while a glide runs, like WPF's CompositionTarget.Rendering.
    private void RequestGlideFrame()
    {
        if (_gliding && !_glideFrameRequested)
        {
            _glideFrameRequested = true;
            RequestAnimationFrame(_ =>
            {
                _glideFrameRequested = false;
                if (_gliding)
                {
                    OnGlideFrame();
                    RequestGlideFrame();
                }
            });
        }
    }

    // How far along a column is, 0 to 1, easing out: each column starts a moment after the one to its left.
    private double GlideProgress(double column, double elapsed)
    {
        var t = Math.Clamp((elapsed - Math.Max(0, column) * _stagger) / GlideSeconds, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    private void OnGlideFrame()
    {
        var elapsed = (DateTime.UtcNow - _glideStart).TotalSeconds;
        var total = GlideSeconds + Math.Max(0, _bands.Count - 1) * _stagger;
        if (elapsed >= total)
        {
            FinishGlide();
            return;
        }

        _updating = true;
        PreampSlider.Value = _preampFrom + (_preampTo - _preampFrom) * GlideProgress(0, elapsed);
        for (var band = 0; band < _bandSliders.Count && band < _glideTo.Length; band++)
        {
            var progress = GlideProgress(band, elapsed);
            var from = band < _glideFrom.Length ? _glideFrom[band] : 0;
            _bandSliders[band].Value = from + (_glideTo[band] - from) * progress;
            if (_revealColumns && BandPanel.Children[band] is Control column)
            {
                column.Opacity = progress;
                ((TranslateTransform)column.RenderTransform!).Y = (1 - progress) * 10;
            }
        }

        _updating = false;
        if (Math.Abs(_widthTo - _widthFrom) > 0.5)
        {
            var eased = 1 - Math.Pow(1 - Math.Clamp(elapsed / total, 0, 1), 3);
            Width = _widthFrom + (_widthTo - _widthFrom) * eased;
        }

        RedrawCurve();
    }

    // Jumps to the end of a glide (or does nothing if there isn't one).
    private void FinishGlide()
    {
        if (!_gliding && _glideTo.Length == 0)
        {
            return;
        }

        _gliding = false;
        _updating = true;
        PreampSlider.Value = Eq.PreampDb;
        for (var band = 0; band < _bandSliders.Count; band++)
        {
            _bandSliders[band].Value = Eq.Gains[band];
            if (BandPanel.Children[band] is Control column)
            {
                column.Opacity = 1;
                ((TranslateTransform)column.RenderTransform!).Y = 0;
            }
        }

        _updating = false;
        if (_widthTo > 0)
        {
            Width = _widthTo;
        }

        _glideTo = Array.Empty<double>();
        _widthTo = 0;
        RedrawCurve();
    }

    // Whether the Mac's "Reduce motion" accessibility setting is on (Windows' client area animation switch).
    private static bool ReduceMotion()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), "sharedWorkspace");
            return workspace != IntPtr.Zero && (ObjC.Send(workspace, "accessibilityDisplayShouldReduceMotion") & 0xFF) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Grabbing a slider mid-glide finishes the glide first, so the slider is where it looks.
    private void OnSliderAreaMouseDown(object? sender, PointerPressedEventArgs e) => FinishGlide();

    // ---- Changing the settings ----

    // Sends the settings to the equalizer, redraws, and saves shortly.
    private void Apply()
    {
        _player.Equalizer.Apply(Eq);
        PresetLabel.Text = Eq.PresetName ?? "Custom";
        SliderArea.Opacity = Eq.Enabled ? 1 : 0.55;
        RedrawCurve();
        SaveSoon();
    }

    private void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnBandChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        var band = (int)((Slider)sender!).Tag!;
        Eq.Gains[band] = e.NewValue;
        Eq.PresetName = null;
        TurnOnIfOff();
        Apply();
        ShowBandStatus(band);
    }

    private void OnPreampChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        Eq.PreampDb = e.NewValue;
        TurnOnIfOff();
        Apply();
        StatusText.Text = $"Preamp: {FormatDb(e.NewValue)}";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
    }

    // A double-click puts a slider back to 0.
    private void OnBandPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2 && sender is Slider slider)
        {
            Dispatcher.UIThread.Post(() => slider.Value = 0);
        }
    }

    private void OnPreampPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Dispatcher.UIThread.Post(() => PreampSlider.Value = 0);
        }
    }

    // Moving a slider while the equalizer is off would otherwise seem to do nothing.
    private void TurnOnIfOff()
    {
        if (!Eq.Enabled)
        {
            Eq.Enabled = true;
            EnabledSwitch.IsChecked = true;
        }
    }

    private static void Nudge(Slider slider, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
        {
            return;
        }

        slider.Value = Math.Clamp(slider.Value + (e.Delta.Y > 0 ? 0.5 : -0.5), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private void OnEnabledClicked(object? sender, RoutedEventArgs e)
    {
        Eq.Enabled = EnabledSwitch.IsChecked == true;
        Apply();
    }

    private void OnBandCountChosen(object? sender, RoutedEventArgs e)
    {
        var count = (int)((RadioButton)sender!).Tag!;
        if (count == _bands.Count)
        {
            return;
        }

        // A glide still running from the last change finishes first, while the sliders and the gains
        // still have the same number of bands.
        FinishGlide();

        // Keep the same sound: a preset is redrawn at the new band count, and a hand-made curve is
        // traced through its sliders and sampled at the new bands.
        var curve = FindCurve(Eq.PresetName) ?? EqualizerCurve.FromBands(_bands, Eq.Gains);
        var oldWidth = Bounds.Width;
        Eq.BandCount = count;
        Eq.Gains = curve.Sample(EqualizerBands.ForCount(count)).ToList();
        BuildBands();
        ShowSettings();
        Apply();

        // The new columns rise into place one after another, their sliders gliding up from 0 with
        // the curve, while the window eases to its new width.
        StartGlide(new double[_bands.Count], Eq.PreampDb, curveFrom: null, revealColumns: true, oldWidth);
    }

    private void OnResetClicked(object? sender, RoutedEventArgs e)
    {
        var (from, preamp, curve) = SnapshotForGlide();
        Eq.Gains = new double[_bands.Count].ToList();
        Eq.PreampDb = 0;
        Eq.PresetName = EqualizerPresets.FlatName;
        ShowSettings();
        Apply();
        StartGlide(from, preamp, curve, revealColumns: false, Bounds.Width);
    }

    // ---- Presets ----

    private void OnPresetsClicked(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        foreach (var preset in EqualizerPresets.BuiltIn)
        {
            menu.Items.Add(PresetItem(preset.Name, preset.Curve));
        }

        if (Eq.CustomPresets.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var saved in Eq.CustomPresets)
            {
                menu.Items.Add(PresetItem(saved.Name, CurveOf(saved)));
            }
        }

        menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save these settings as a preset…" };
        save.Click += (_, _) => StartSaving();
        menu.Items.Add(save);
        if (Eq.CustomPresets.Count > 0)
        {
            var delete = new MenuItem { Header = "Delete a saved preset…" };
            delete.Click += (_, _) => Dispatcher.UIThread.Post(ShowDeleteMenu);
            menu.Items.Add(delete);
        }

        menu.Open(PresetsButton);
    }

    private MenuItem PresetItem(string name, EqualizerCurve curve)
    {
        var item = new MenuItem
        {
            Header = name,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = string.Equals(name, Eq.PresetName, StringComparison.OrdinalIgnoreCase),
        };
        item.Click += (_, _) =>
        {
            var (from, preamp, oldCurve) = SnapshotForGlide();
            Eq.Gains = curve.Sample(_bands).ToList();
            Eq.PresetName = name;
            Eq.Enabled = true;
            ShowSettings();
            Apply();
            StartGlide(from, preamp, oldCurve, revealColumns: false, Bounds.Width);
        };
        return item;
    }

    private void ShowDeleteMenu()
    {
        var menu = new ContextMenu { Placement = PlacementMode.Bottom, VerticalOffset = 4 };
        foreach (var saved in Eq.CustomPresets)
        {
            var item = new MenuItem { Header = $"Delete “{saved.Name}”" };
            item.Click += (_, _) =>
            {
                Eq.CustomPresets.Remove(saved);
                if (string.Equals(saved.Name, Eq.PresetName, StringComparison.OrdinalIgnoreCase))
                {
                    Eq.PresetName = null;
                }

                Apply();
                StatusText.Text = $"Deleted “{saved.Name}”";
            };
            menu.Items.Add(item);
        }

        menu.Open(PresetsButton);
    }

    /// <summary>Shows the row for naming a new preset, with the box focused.</summary>
    internal void StartSaving()
    {
        SaveRow.IsVisible = true;
        PresetNameBox.Text = FindCurve(Eq.PresetName) is not null && EqualizerPresets.Find(Eq.PresetName) is null
            ? Eq.PresetName ?? ""
            : "";
        FitToContent();
        PresetNameBox.Focus();
        PresetNameBox.SelectAll();
    }

    private void StopSaving()
    {
        SaveRow.IsVisible = false;
        FitToContent();
    }

    private void OnSaveConfirmed(object? sender, RoutedEventArgs e) => SavePreset();

    private void OnSaveCancelled(object? sender, RoutedEventArgs e) => StopSaving();

    private void SavePreset()
    {
        var name = (PresetNameBox.Text ?? "").Trim();
        if (name.Length == 0)
        {
            StatusText.Text = "Type a name for your preset first";
            return;
        }

        if (EqualizerPresets.Find(name) is not null)
        {
            StatusText.Text = "That name belongs to a built-in preset. Try another one.";
            return;
        }

        // Saving under an existing name replaces that preset.
        Eq.CustomPresets.RemoveAll(saved => string.Equals(saved.Name, name, StringComparison.OrdinalIgnoreCase));
        Eq.CustomPresets.Add(new SavedEqualizerPreset
        {
            Name = name,
            Frequencies = _bands.Centres.ToList(),
            Gains = Eq.Gains.ToList(),
        });
        Eq.PresetName = name;
        StopSaving();
        Apply();
        StatusText.Text = $"Saved “{name}”";
    }

    private EqualizerCurve? FindCurve(string? name)
    {
        if (EqualizerPresets.Find(name) is { } preset)
        {
            return preset.Curve;
        }

        var saved = Eq.CustomPresets.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase));
        return saved is null ? null : CurveOf(saved);
    }

    private static EqualizerCurve CurveOf(SavedEqualizerPreset saved) =>
        new(saved.Frequencies.Zip(saved.Gains, (hertz, gain) => new EqualizerPoint(hertz, gain)));

    // ---- Keys and closing ----

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox || PresetNameBox.IsFocused)
        {
            if (e.Key == Key.Enter)
            {
                SavePreset();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                StopSaving();
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Everything else goes to the player, so its shortcuts (Space, E and the rest) keep
        // working while this window has the focus.
        if (Owner is MainWindow player)
        {
            player.HandleShortcut(e);
        }
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _gliding = false;
        if (_open == this)
        {
            _open = null;
        }

        _saveTimer.Stop();
        _settings.Save();
    }

    // ---- Text ----

    // "1k" reads as "1 kHz", "63" as "63 Hz".
    private static string SpokenFrequency(string label) =>
        label.EndsWith('k') ? label[..^1] + " kHz" : label + " Hz";

    private static string FormatDb(double db) =>
        db == 0 ? "0 dB" : db.ToString("+0.0;−0.0", CultureInfo.InvariantCulture) + " dB";
}
