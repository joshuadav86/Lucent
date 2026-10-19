using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Editor;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Editor.Stems;

namespace Lucent.Mac;

/// <summary>
/// The stems of a split, to listen to before keeping or saving them: one waveform per stem, each with mute and
/// solo, played together through the editor's kind of player. From here the stems being heard can be kept in
/// the editor, or all four saved as files.
/// </summary>
public partial class StemsWindow : Window
{
    private const int PeakBins = 800;

    private readonly StemMixer _mixer;
    private readonly EditorPlayer _player = new();
    private readonly DispatcherTimer _timer;
    private readonly Func<IReadOnlyList<string>, Task> _keep;
    private readonly Func<Task> _save;
    private readonly List<StemRow> _rows = new();
    private int _position;

    /// <summary>For the XAML previewer only.</summary>
    public StemsWindow()
    {
        InitializeComponent();
        _mixer = null!;
        _timer = new DispatcherTimer();
        _keep = _ => Task.CompletedTask;
        _save = () => Task.CompletedTask;
    }

    public StemsWindow(StemSet stems, string song, string note, Func<IReadOnlyList<string>, Task> keep, Func<Task> save)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        TitleIcon.Source = EqualizerWindow.LucentIcon();
        _mixer = new StemMixer(stems);
        _keep = keep;
        _save = save;
        SongText.Text = song;
        NoteText.Text = note;
        Title = $"Stems of {song} - Lucent";

        for (var s = 0; s < _mixer.Sources.Count; s++)
        {
            Rows.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            var row = new StemRow(this, s);
            Grid.SetRow(row.Root, s);
            Rows.Children.Add(row.Root);
            _rows.Add(row);
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _timer.Tick += (_, _) => ShowPosition();
        _player.Stopped += (_, error) =>
        {
            // It got to the end (or the audio device failed): the next play starts from the top.
            _position = 0;
            ShowStopped();
            if (error is not null)
            {
                TimeText.Text = "Playback stopped because of a problem with the audio device.";
            }
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Closed += (_, _) =>
        {
            _timer.Stop();
            _player.Dispose();
        };

        ShowMix();
        ShowPosition();
    }

    /// <summary>For the screenshots: mutes a stem as its M button would, partway through playing.</summary>
    internal void PreviewMuted(int index)
    {
        _mixer.SetMuted(index, true);
        _rows[index].Mute.IsChecked = true;
        _position = Frames / 3;
        ShowMix();
        ShowPosition();
    }

    private int Frames => _mixer.First.Frames;

    private int SampleRate => _mixer.First.SampleRate;

    private void Play(int from)
    {
        _position = Math.Clamp(from, 0, Math.Max(0, Frames - 1));
        try
        {
            _player.Play(_mixer.First, _position, Frames, range => _mixer.Through(range, _position));
        }
        catch (Exception)
        {
            ShowStopped();
            TimeText.Text = "Couldn't play: there's a problem with the audio device.";
            return;
        }

        PlayButton.Content = "PAUSE";
        _timer.Start();
    }

    private void Pause()
    {
        _position = _player.PositionFrame;
        _player.Stop();
        ShowStopped();
    }

    private void ShowStopped()
    {
        _timer.Stop();
        PlayButton.Content = "PLAY";
        ShowPosition();
    }

    private void ShowPosition()
    {
        var frame = _player.IsPlaying ? _player.PositionFrame : _position;
        var fraction = Frames > 0 ? frame / (double)Frames : 0;
        foreach (var row in _rows)
        {
            row.Waveform.Playhead = fraction;
        }

        TimeText.Text = $"{Time(frame)} / {Time(Frames)}";
    }

    private string Time(int frame)
    {
        var time = TimeSpan.FromSeconds(frame / (double)SampleRate);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    /// <summary>Shows which stems are heard, and what keeping them would do.</summary>
    private void ShowMix()
    {
        foreach (var row in _rows)
        {
            row.ShowState();
        }

        var audible = _mixer.Audible;
        var some = audible.Count > 0 && audible.Count < _mixer.Sources.Count;
        KeepButton.IsEnabled = some;
        KeepButton.Content = some ? new StemsEffect(audible).Description.ToUpperInvariant() + " IN THE EDITOR" : "KEEP WHAT'S HEARD";
    }

    private void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (_player.IsPlaying)
        {
            Pause();
        }
        else
        {
            Play(_position);
        }
    }

    private void OnStop(object? sender, RoutedEventArgs e)
    {
        _player.Stop();
        _position = 0;
        ShowStopped();
    }

    private async void OnKeep(object? sender, RoutedEventArgs e)
    {
        var audible = _mixer.Audible;
        _player.Stop();
        ShowStopped();
        await _keep(audible);
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await _save();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            OnPlayPause(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    private T? Resource<T>(string key) where T : class =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as T : null;

    /// <summary>One stem's row: its name, mute and solo, and its waveform.</summary>
    private sealed class StemRow
    {
        private readonly StemsWindow _window;
        private readonly int _index;

        public StemRow(StemsWindow window, int index)
        {
            _window = window;
            _index = index;
            var source = window._mixer.Sources[index];

            Root = new Grid { Margin = new Thickness(0, 0, 0, 6), ColumnDefinitions = new ColumnDefinitions("150,*") };

            var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
            controls.Children.Add(new TextBlock
            {
                Text = StemNames.Title(source),
                FontSize = 13,
                FontWeight = FontWeight.SemiBold,
                Foreground = window.Resource<IBrush>("TextBrush"),
                Margin = new Thickness(0, 0, 0, 6),
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            var mute = Mute = Toggle(window, "M", $"Mute the {StemNames.List(new[] { source })}");
            var solo = Toggle(window, "S", $"Hear only the {StemNames.List(new[] { source })} (solo)");
            solo.Margin = new Thickness(4, 0, 0, 0);
            mute.Click += (_, _) =>
            {
                window._mixer.SetMuted(index, mute.IsChecked == true);
                window.ShowMix();
            };
            solo.Click += (_, _) =>
            {
                window._mixer.SetSoloed(index, solo.IsChecked == true);
                window.ShowMix();
            };
            buttons.Children.Add(mute);
            buttons.Children.Add(solo);
            controls.Children.Add(buttons);
            Root.Children.Add(controls);

            Waveform = new StemWaveform(StemMixer.Peaks(window._mixer[index], PeakBins))
            {
                Margin = new Thickness(6),
                PlayheadBrush = window.Resource<IBrush>("AccentBrush") ?? Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            Waveform.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(Waveform).Properties.IsLeftButtonPressed)
                {
                    return;
                }

                var fraction = Math.Clamp(e.GetPosition(Waveform).X / Math.Max(1, Waveform.Bounds.Width), 0, 1);
                window.Play((int)(fraction * window.Frames));
            };
            var border = new Border
            {
                Child = Waveform,
                ClipToBounds = true,
                Background = window.Resource<IBrush>("BackgroundBrush"),
                BorderBrush = window.Resource<IBrush>("HairlineBrush"),
                BorderThickness = new Thickness(1),
            };
            if (window.TryFindResource("ControlCorner", window.ActualThemeVariant, out var corner) && corner is CornerRadius radius)
            {
                border.CornerRadius = radius;
            }

            ToolTip.SetTip(border, "Click to play from here");
            Grid.SetColumn(border, 1);
            Root.Children.Add(border);
        }

        public Grid Root { get; }

        public ToggleButton Mute { get; }

        public StemWaveform Waveform { get; }

        public void ShowState()
        {
            var audible = _window._mixer.IsAudible(_index);
            Waveform.Fill = _window.Resource<IBrush>(audible ? "AccentBrush" : "FaintTextBrush") ?? Brushes.Gray;
            Waveform.FillOpacity = audible ? 0.85 : 0.35;
            Waveform.InvalidateVisual();
        }

        private static ToggleButton Toggle(StemsWindow window, string text, string tip)
        {
            var toggle = new ToggleButton
            {
                Theme = window.Resource<ControlTheme>("PanelToggle"),
                Content = text,
                Width = 30,
                Height = 26,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTip.SetTip(toggle, tip);
            return toggle;
        }
    }

    /// <summary>A stem's waveform, mirrored above and below the middle, with the playhead.</summary>
    private sealed class StemWaveform : Control
    {
        private readonly float[] _peaks;
        private double _playhead;

        public StemWaveform(float[] peaks) => _peaks = peaks;

        public IBrush Fill { get; set; } = Brushes.Gray;

        public double FillOpacity { get; set; } = 1;

        public IBrush PlayheadBrush { get; set; } = Brushes.White;

        /// <summary>Where the playhead is, from 0 to 1.</summary>
        public double Playhead
        {
            get => _playhead;
            set
            {
                if (_playhead != value)
                {
                    _playhead = value;
                    InvalidateVisual();
                }
            }
        }

        public override void Render(DrawingContext context)
        {
            var width = Bounds.Width;
            var height = Bounds.Height;
            // Something to hit, so a click anywhere on the waveform plays from there.
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            if (_peaks.Length > 0 && width > 0 && height > 0)
            {
                var middle = height / 2;
                var geometry = new StreamGeometry();
                using (var figure = geometry.Open())
                {
                    figure.BeginFigure(new Point(0, middle), true);
                    for (var i = 0; i < _peaks.Length; i++)
                    {
                        figure.LineTo(new Point((i + 0.5) * width / _peaks.Length, middle - _peaks[i] * middle));
                    }

                    for (var i = _peaks.Length - 1; i >= 0; i--)
                    {
                        figure.LineTo(new Point((i + 0.5) * width / _peaks.Length, middle + _peaks[i] * middle));
                    }

                    figure.EndFigure(true);
                }

                using (context.PushOpacity(FillOpacity))
                {
                    context.DrawGeometry(Fill, null, geometry);
                }
            }

            var x = Math.Round(_playhead * Math.Max(0, width - 2));
            context.FillRectangle(PlayheadBrush, new Rect(x, 0, 2, height));
        }
    }
}
