using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// The graphic equalizer window: an on/off switch, a choice of band counts, a preamp, one slider
/// per band with the response curve drawn above, and presets. Open it with <see cref="Toggle"/>.
/// </summary>
public partial class EqualizerWindow : Window
{
    // The curve shows this many decibels above and below 0.
    private const double CurveRangeDb = 15;

    private static EqualizerWindow? _open;

    private readonly AudioPlayer _player;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _saveTimer;
    private readonly List<Slider> _bandSliders = new();
    private EqualizerBands _bands;
    private double _columnWidth;

    // The preamp column and the +12/0/−12 scale beside it, to the left of the band sliders.
    private const double PreampAndScaleWidth = 48 + 26 + 6;

    // Beyond this the sliders look scattered rather than spread out.
    private const double WidestColumn = 110;
    // True while the sliders are being set from code (and during start-up), so their change
    // handlers stay quiet.
    private bool _updating = true;

    // ---- Glides: the sliders and the curve move to new settings instead of jumping ----

    // How long each column takes to settle, and the most the last column waits behind the first.
    private const double GlideSeconds = 0.34;
    private const double MostStagger = 0.22;
    private bool _gliding;
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

    private EqualizerWindow(AudioPlayer player, AppSettings settings)
    {
        InitializeComponent();
        _player = player;
        _settings = settings;
        _bands = EqualizerBands.ForCount(Eq.BandCount);
        Eq.BandCount = _bands.Count;
        Eq.Gains = Equalizer.SliderGains(Eq, _bands).ToList();

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
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "EqualizerBands",
                Content = count.ToString(CultureInfo.InvariantCulture),
                Tag = count,
                MinWidth = 30,
            };
            button.Click += OnBandCountChosen;
            BandCountBar.Children.Add(button);
        }

        PreampSlider.PreviewMouseWheel += (_, e) => Nudge(PreampSlider, e);
        LocationChanged += (_, _) => RememberPosition();
        Loaded += (_, _) => RedrawCurve();

        BuildBands();
        ShowSettings();
    }

    private EqualizerSettings Eq => _settings.Equalizer;

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

        var window = new EqualizerWindow(player, settings) { Owner = owner };
        if (owner is MainWindow main)
        {
            main.DockNewWindow(window);
        }
        else
        {
            window.PlaceOnScreen(owner);
        }

        window.Closed += (_, _) => openChanged?.Invoke(false);
        _open = window;
        window.Show();
        openChanged?.Invoke(true);
        return window;
    }

    // ---- Layout ----

    private void BuildBands()
    {
        _bands = EqualizerBands.ForCount(Eq.BandCount);
        // Narrower columns for more bands, so even 31 sliders fit a normal screen.
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
        HeaderRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var room = HeaderRow.DesiredSize.Width - SliderArea.Margin.Left - SliderArea.Margin.Right - PreampAndScaleWidth;
        _columnWidth = Math.Clamp(Math.Floor(room / _bands.Count), narrowest, Math.Max(narrowest, WidestColumn));

        BandPanel.Children.Clear();
        _bandSliders.Clear();
        for (var band = 0; band < _bands.Count; band++)
        {
            var label = _bands.Labels[band];
            var slider = new Slider
            {
                Style = (Style)Resources["BandSlider"],
                Tag = band,
                ToolTip = $"{SpokenFrequency(label)}. Double-click to reset.",
            };
            slider.ValueChanged += OnBandChanged;
            slider.MouseDoubleClick += (sender, _) => ((Slider)sender!).Value = 0;
            slider.PreviewMouseWheel += (sender, e) => Nudge((Slider)sender!, e);

            var text = new TextBlock { Style = (Style)Resources["BandLabel"], Text = label };
            if (_bands.Count >= 24)
            {
                text.FontSize = 9;
            }

            var column = new StackPanel
            {
                Width = _columnWidth,
                Background = Brushes.Transparent,
                Tag = band,
                RenderTransform = new TranslateTransform(),
            };
            column.Children.Add(slider);
            column.Children.Add(text);
            var index = band;
            column.MouseEnter += (_, _) => ShowBandStatus(index);
            column.MouseLeave += (_, _) => ShowHint();
            BandPanel.Children.Add(column);
            _bandSliders.Add(slider);
        }

        CurveFrame.Width = _columnWidth * _bands.Count;
        FitToContent();
    }

    // Sized by hand rather than with SizeToContent, which leaves a stray strip with custom chrome.
    private void FitToContent()
    {
        Root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Width = Math.Ceiling(Root.DesiredSize.Width) + BorderThickness.Left + BorderThickness.Right;
        Height = Math.Ceiling(Root.DesiredSize.Height) + BorderThickness.Top + BorderThickness.Bottom;
    }

    private void PlaceOnScreen(Window owner)
    {
        if (Eq.WindowLeft is { } left && Eq.WindowTop is { } top && IsOnScreen(left, top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    // Enough of the title bar has to be on a screen to drag the window back.
    private static bool IsOnScreen(double left, double top) =>
        left + 120 > SystemParameters.VirtualScreenLeft
        && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 60
        && top >= SystemParameters.VirtualScreenTop - 4
        && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 40;

    private void RememberPosition()
    {
        if (WindowState == WindowState.Normal && IsLoaded)
        {
            Eq.WindowLeft = Left;
            Eq.WindowTop = Top;
            SaveSoon();
        }
    }

    // ---- Showing the settings ----

    private void ShowSettings()
    {
        _updating = true;
        EnabledSwitch.IsChecked = Eq.Enabled;
        foreach (RadioButton button in BandCountBar.Children)
        {
            button.IsChecked = (int)button.Tag == _bands.Count;
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
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        var fill = new StreamGeometry();
        using (var context = fill.Open())
        {
            context.BeginFigure(new Point(0, zero), true, true);
            context.PolyLineTo(points.Append(new Point(points[^1].X, zero)).ToList(), false, true);
        }

        var zeroLine = new StreamGeometry();
        using (var context = zeroLine.Open())
        {
            context.BeginFigure(new Point(0, zero), false, false);
            context.LineTo(new Point(width, zero), true, false);
        }

        line.Freeze();
        fill.Freeze();
        zeroLine.Freeze();
        CurveLine.Data = line;
        CurveFill.Data = fill;
        ZeroLine.Data = zeroLine;
        CurveLine.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, Eq.Enabled ? "AccentBrush" : "FaintTextBrush");
        CurveFill.Visibility = Eq.Enabled ? Visibility.Visible : Visibility.Collapsed;
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

        // Nothing moves for people who've turned animations off in Windows.
        if (!SystemParameters.ClientAreaAnimation || !IsLoaded)
        {
            FinishGlide();
            return;
        }

        _glideStart = DateTime.UtcNow;
        if (!_gliding)
        {
            _gliding = true;
            CompositionTarget.Rendering += OnGlideFrame;
        }

        OnGlideFrame(null, EventArgs.Empty);
    }

    // How far along a column is, 0 to 1, easing out: each column starts a moment after the one to its left.
    private double GlideProgress(double column, double elapsed)
    {
        var t = Math.Clamp((elapsed - Math.Max(0, column) * _stagger) / GlideSeconds, 0, 1);
        return 1 - Math.Pow(1 - t, 3);
    }

    private void OnGlideFrame(object? sender, EventArgs e)
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
            if (_revealColumns && BandPanel.Children[band] is FrameworkElement column)
            {
                column.Opacity = progress;
                ((TranslateTransform)column.RenderTransform).Y = (1 - progress) * 10;
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

        if (_gliding)
        {
            CompositionTarget.Rendering -= OnGlideFrame;
            _gliding = false;
        }

        _updating = true;
        PreampSlider.Value = Eq.PreampDb;
        for (var band = 0; band < _bandSliders.Count; band++)
        {
            _bandSliders[band].Value = Eq.Gains[band];
            if (BandPanel.Children[band] is FrameworkElement column)
            {
                column.Opacity = 1;
                ((TranslateTransform)column.RenderTransform).Y = 0;
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

    // Grabbing a slider mid-glide finishes the glide first, so the slider is where it looks.
    private void OnSliderAreaMouseDown(object sender, MouseButtonEventArgs e) => FinishGlide();

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

    private void OnBandChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating)
        {
            return;
        }

        var band = (int)((Slider)sender).Tag;
        Eq.Gains[band] = e.NewValue;
        Eq.PresetName = null;
        TurnOnIfOff();
        Apply();
        ShowBandStatus(band);
    }

    private void OnPreampChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
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

    private void OnPreampDoubleClick(object sender, MouseButtonEventArgs e) => PreampSlider.Value = 0;

    // Moving a slider while the equalizer is off would otherwise seem to do nothing.
    private void TurnOnIfOff()
    {
        if (!Eq.Enabled)
        {
            Eq.Enabled = true;
            EnabledSwitch.IsChecked = true;
        }
    }

    private static void Nudge(Slider slider, MouseWheelEventArgs e)
    {
        slider.Value = Math.Clamp(slider.Value + (e.Delta > 0 ? 0.5 : -0.5), slider.Minimum, slider.Maximum);
        e.Handled = true;
    }

    private void OnEnabledClicked(object sender, RoutedEventArgs e)
    {
        Eq.Enabled = EnabledSwitch.IsChecked == true;
        Apply();
    }

    private void OnBandCountChosen(object sender, RoutedEventArgs e)
    {
        var count = (int)((RadioButton)sender).Tag;
        if (count == _bands.Count)
        {
            return;
        }

        // Keep the same sound: a preset is redrawn at the new band count, and a hand-made curve is
        // traced through its sliders and sampled at the new bands.
        var curve = FindCurve(Eq.PresetName) ?? EqualizerCurve.FromBands(_bands, Eq.Gains);
        var oldWidth = ActualWidth;
        Eq.BandCount = count;
        Eq.Gains = curve.Sample(EqualizerBands.ForCount(count)).ToList();
        FinishGlide();
        BuildBands();
        ShowSettings();
        Apply();

        // The new columns rise into place one after another, their sliders gliding up from 0 with
        // the curve, while the window eases to its new width.
        StartGlide(new double[_bands.Count], Eq.PreampDb, curveFrom: null, revealColumns: true, oldWidth);
    }

    private void OnResetClicked(object sender, RoutedEventArgs e)
    {
        var (from, preamp, curve) = SnapshotForGlide();
        Eq.Gains = new double[_bands.Count].ToList();
        Eq.PreampDb = 0;
        Eq.PresetName = EqualizerPresets.FlatName;
        ShowSettings();
        Apply();
        StartGlide(from, preamp, curve, revealColumns: false, ActualWidth);
    }

    // ---- Presets ----

    private void OnPresetsClicked(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = PresetsButton, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
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
            delete.Click += (_, _) => Dispatcher.BeginInvoke(ShowDeleteMenu);
            menu.Items.Add(delete);
        }

        menu.IsOpen = true;
    }

    private MenuItem PresetItem(string name, EqualizerCurve curve)
    {
        var item = new MenuItem
        {
            Header = name,
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
            StartGlide(from, preamp, oldCurve, revealColumns: false, ActualWidth);
        };
        return item;
    }

    private void ShowDeleteMenu()
    {
        var menu = new ContextMenu { PlacementTarget = PresetsButton, Placement = PlacementMode.Bottom, VerticalOffset = 4 };
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

        menu.IsOpen = true;
    }

    private void StartSaving()
    {
        SaveRow.Visibility = Visibility.Visible;
        PresetNameBox.Text = FindCurve(Eq.PresetName) is not null && EqualizerPresets.Find(Eq.PresetName) is null
            ? Eq.PresetName ?? ""
            : "";
        FitToContent();
        PresetNameBox.Focus();
        PresetNameBox.SelectAll();
    }

    private void StopSaving()
    {
        SaveRow.Visibility = Visibility.Collapsed;
        FitToContent();
    }

    private void OnSaveConfirmed(object sender, RoutedEventArgs e) => SavePreset();

    private void OnSaveCancelled(object sender, RoutedEventArgs e) => StopSaving();

    private void SavePreset()
    {
        var name = PresetNameBox.Text.Trim();
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox)
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
        if (Owner is not null && e.InputSource is not null)
        {
            var forwarded = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            Owner.RaiseEvent(forwarded);
            e.Handled = forwarded.Handled;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_gliding)
        {
            CompositionTarget.Rendering -= OnGlideFrame;
            _gliding = false;
        }

        _open = null;
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
