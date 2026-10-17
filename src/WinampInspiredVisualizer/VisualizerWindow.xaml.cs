using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>The visuals, in their own window with the visual picker and Auto controls underneath.</summary>
public partial class VisualizerWindow : ToolWindow
{
    // How often Auto can change visual, offered in the menu next to the AUTO pill (and in Options).
    internal static readonly int[] AutoCycleChoices = { 10, 15, 25, 45, 90, 180 };

    private VisualizerHost? _host;
    private AppSettings _settings = new();
    private IReadOnlyList<string> _presetNames = Array.Empty<string>();
    private DateTime _presetPopupClosedAt;
    private bool _isFullscreen;
    private WindowState _stateBeforeFullscreen;
    private Rect _boundsBeforeFullscreen;

    public VisualizerWindow()
    {
        InitializeComponent();
        PlacementName = "Visuals";
    }

    public bool IsFullscreen => _isFullscreen;

    /// <summary>Puts the visualizer in this window. Called once, before the window is first shown.</summary>
    public void Attach(VisualizerHost host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        host.PresetsLoaded += (_, names) =>
        {
            _presetNames = names;
            PresetList.ItemsSource = names;
            ShowPreset();
        };
        host.ProblemReported += (_, message) =>
        {
            VisualizerStatus.Text = message;
            VisualizerStatus.ToolTip = message;
        };
        host.DoubleClicked += (_, _) => ToggleFullscreen();
        host.PresetChanged += (_, _) => ShowPreset();
        host.FrameRateMeasured += (_, stats) =>
            FrameRateText.Text = $"{stats.FramesPerSecond:0} fps · {stats.Width} × {stats.Height}" + (stats.Hdr ? " · HDR" : string.Empty);
        VisualizerFrame.Child = host;
        UpdateAutoCycleButton();
    }

    /// <summary>Shows or hides the frame rate readout, following the setting in Options.</summary>
    public void ShowFrameRate(bool show) => FrameRateText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    // ---- Picking visuals ----

    private void OnNextPreset(object sender, RoutedEventArgs e) => ChangePreset(+1);

    private void OnPreviousPreset(object sender, RoutedEventArgs e) => ChangePreset(-1);

    public void ChangePreset(int step)
    {
        if (_host is null || _presetNames.Count == 0)
        {
            return;
        }

        _host.PresetIndex = ((_host.PresetIndex + step) % _presetNames.Count + _presetNames.Count) % _presetNames.Count;
        ShowPreset();
    }

    private void ShowPreset()
    {
        if (_host is null || _presetNames.Count == 0)
        {
            PresetName.Text = "No visuals found";
            return;
        }

        var index = Math.Clamp(_host.PresetIndex, 0, _presetNames.Count - 1);
        PresetName.Text = _presetNames[index];
        PresetList.SelectedIndex = index;
        _settings.LastVisual = _presetNames[index];
    }

    private void OnTogglePresetPicker(object sender, RoutedEventArgs e) => TogglePresetPicker();

    public void TogglePresetPicker()
    {
        // Clicking the name while the picker is open closes it (the click outside already did),
        // so don't immediately reopen it.
        if (PresetPopup.IsOpen || DateTime.UtcNow - _presetPopupClosedAt < TimeSpan.FromMilliseconds(250))
        {
            PresetPopup.IsOpen = false;
            return;
        }

        if (_presetNames.Count == 0)
        {
            return;
        }

        if (!IsVisible)
        {
            Show();
        }

        PresetPopup.IsOpen = true;
        PresetList.ScrollIntoView(PresetList.SelectedItem);
        PresetList.Focus();
    }

    private void OnPresetPopupClosed(object? sender, EventArgs e) => _presetPopupClosedAt = DateTime.UtcNow;

    private void OnPresetListClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(PresetList, (DependencyObject)e.OriginalSource) is ListBoxItem item)
        {
            PickPreset(PresetList.ItemContainerGenerator.IndexFromContainer(item));
        }
    }

    private void OnPresetListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PickPreset(PresetList.SelectedIndex);
            e.Handled = true;
        }
    }

    private void PickPreset(int index)
    {
        if (_host is not null && index >= 0 && index < _presetNames.Count)
        {
            _host.PresetIndex = index;
            ShowPreset();
        }

        PresetPopup.IsOpen = false;
    }

    // ---- Auto ----

    private void OnToggleAutoCycle(object sender, RoutedEventArgs e) => ToggleAutoCycle();

    public void ToggleAutoCycle()
    {
        _settings.AutoCycle = !_settings.AutoCycle;
        Player?.ApplyVisualizerSettings();
        UpdateAutoCycleButton();
        _settings.Save();
    }

    public void UpdateAutoCycleButton()
    {
        var seconds = _settings.AutoCycleSeconds;
        AutoCycleButton.Content = $"AUTO · {ShortIntervalText(seconds)}";
        AutoCycleButton.SetResourceReference(ForegroundProperty, _settings.AutoCycle ? "AccentBrush" : "FaintTextBrush");
        AutoCycleButton.ToolTip = _settings.AutoCycle
            ? $"Moving to a new visual on the beat every {IntervalText(seconds)} or so. Click to stop (A), right-click to change how often"
            : $"Auto is off. Click to move to a new visual on the beat every {IntervalText(seconds)} or so (A), right-click to change how often";
    }

    // "25 seconds" / "3 minutes" for menus and tooltips, "25s" / "3m" for the pill.
    internal static string IntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60} minutes" : $"{seconds} seconds";

    private static string ShortIntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds}s";

    private void OnAutoCycleIntervalMenu(object sender, RoutedEventArgs e) => ShowAutoCycleMenu();

    private void OnAutoCycleRightClick(object sender, MouseButtonEventArgs e)
    {
        ShowAutoCycleMenu();
        e.Handled = true;
    }

    /// <summary>The menu above the AUTO pill for choosing how often the visuals change.</summary>
    private void ShowAutoCycleMenu()
    {
        var menu = new ContextMenu { PlacementTarget = AutoCycleButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        menu.Items.Add(new MenuItem { Header = "Change visual about every", IsEnabled = false });
        foreach (var seconds in AutoCycleChoices)
        {
            var item = new MenuItem { Header = IntervalText(seconds), IsChecked = seconds == _settings.AutoCycleSeconds };
            item.Click += (_, _) =>
            {
                // Choosing how often only makes sense with Auto on, so this turns it on too.
                _settings.AutoCycleSeconds = seconds;
                _settings.AutoCycle = true;
                Player?.ApplyVisualizerSettings();
                UpdateAutoCycleButton();
                _settings.Save();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    // ---- Window ----

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    /// <summary>Fills the screen with the visuals (Esc or double-click to leave).</summary>
    public void ToggleFullscreen()
    {
        if (!_isFullscreen && !IsVisible)
        {
            Show();
        }

        _isFullscreen = !_isFullscreen;
        var chrome = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        TitleBar.Visibility = chrome;
        BottomBar.Visibility = chrome;
        Frame.BorderThickness = new Thickness(_isFullscreen ? 0 : 1);
        PresetPopup.IsOpen = false;

        if (_isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            _boundsBeforeFullscreen = RestoreBounds;
            // Leave Maximized first so Windows recalculates the size without the title bar.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Activate();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
            if (_stateBeforeFullscreen == WindowState.Normal && !_boundsBeforeFullscreen.IsEmpty)
            {
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
            }
        }

        OnWindowStateChanged(this, EventArgs.Empty);
    }

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "RestoreIcon" : "MaximizeIcon");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = maximized && !_isFullscreen ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
