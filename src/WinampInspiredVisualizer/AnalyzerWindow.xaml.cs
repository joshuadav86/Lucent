using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>
/// The analyzer: a scrolling heatmap of the music's frequencies over time with its waveform
/// underneath, like the Vision plugin. It opens docked to the player's group and snaps like the
/// other windows; its frame and the Theme colours follow the skin and theme.
/// </summary>
public partial class AnalyzerWindow : Window
{
    private static AnalyzerWindow? _instance;

    private readonly AppSettings _settings;
    private readonly Spectrogram _spectrogram;

    private AnalyzerWindow(AudioPlayer player, AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Width = Math.Max(MinWidth, settings.AnalyzerWindowWidth);
        Height = Math.Max(MinHeight, settings.AnalyzerWindowHeight);

        _spectrogram = new Spectrogram(player.Capture)
        {
            Palette = settings.AnalyzerPalette,
            ColumnsPerSecond = ColumnsPerSecond(settings.AnalyzerSpeed),
        };
        Display.Child = _spectrogram;

        foreach (var palette in Enum.GetValues<SpectrogramPalette>())
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "AnalyzerPalette",
                Content = palette.ToString(),
                Tag = palette,
                FontSize = 11,
                Height = 24,
                IsChecked = palette == settings.AnalyzerPalette,
            };
            button.Click += OnPaletteChosen;
            PaletteBar.Children.Add(button);
        }
    }

    /// <summary>Raised when the analyzer opens or closes, so a toggle button can follow it.</summary>
    public static event EventHandler? OpenChanged;

    public static bool IsOpen => _instance is not null;

    /// <summary>Opens the analyzer docked beside the player's windows, or closes it if it's open. Returns whether it's now open.</summary>
    public static bool Toggle(Window owner, AudioPlayer player, AppSettings settings)
    {
        if (_instance is not null)
        {
            _instance.Close();
            return false;
        }

        var window = new AnalyzerWindow(player, settings) { Owner = owner };
        if (owner is MainWindow main)
        {
            main.DockNewWindow(window);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _instance = window;
        window.Show();
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    private static double ColumnsPerSecond(AnalyzerSpeed speed) => speed switch
    {
        AnalyzerSpeed.Slow => 25,
        AnalyzerSpeed.Fast => 100,
        _ => 50,
    };

    // ---- Choices ----

    private void OnPaletteChosen(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: SpectrogramPalette palette })
        {
            _spectrogram.Palette = palette;
            _settings.AnalyzerPalette = palette;
            _settings.Save();
        }
    }

    private void OnDisplayMenuOpening(object sender, ContextMenuEventArgs e)
    {
        SlowMenuItem.IsChecked = _settings.AnalyzerSpeed == AnalyzerSpeed.Slow;
        NormalMenuItem.IsChecked = _settings.AnalyzerSpeed == AnalyzerSpeed.Normal;
        FastMenuItem.IsChecked = _settings.AnalyzerSpeed == AnalyzerSpeed.Fast;
        OnTopMenuItem.IsChecked = Topmost;
    }

    private void OnSpeedChosen(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<AnalyzerSpeed>(tag, out var speed))
        {
            _spectrogram.ColumnsPerSecond = ColumnsPerSecond(speed);
            _settings.AnalyzerSpeed = speed;
            _settings.Save();
        }
    }

    private void OnToggleOnTop(object sender, RoutedEventArgs e) => Topmost = !Topmost;

    // ---- The frequency under the mouse ----

    private void OnDisplayMouseMove(object sender, MouseEventArgs e)
    {
        ReadoutText.Text = _spectrogram.FrequencyAt(e.GetPosition(_spectrogram).Y) is { } hertz
            ? hertz >= 1000
                ? (hertz / 1000).ToString(hertz >= 10000 ? "0.0" : "0.00", CultureInfo.InvariantCulture) + " kHz"
                : Math.Round(hertz).ToString(CultureInfo.InvariantCulture) + " Hz"
            : string.Empty;
    }

    private void OnDisplayMouseLeave(object sender, MouseEventArgs e) => ReadoutText.Text = string.Empty;

    // ---- Window ----

    private void OnDisplayMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Drag the picture to move the window, as with the artwork.
        if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
            return;
        }

        // Everything else goes to the player, so its shortcuts (Space, and Y to close this) keep
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

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.AnalyzerWindowWidth = bounds.Width;
            _settings.AnalyzerWindowHeight = bounds.Height;
            _settings.Save();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_instance == this)
        {
            _instance = null;
        }

        OpenChanged?.Invoke(null, EventArgs.Empty);
    }
}
