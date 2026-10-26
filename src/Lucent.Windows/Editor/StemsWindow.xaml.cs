using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Lucent.Editor;
using Lucent.Editor.Stems;
using Path = System.Windows.Shapes.Path;

namespace Lucent;

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
    private string? _savedFile;

    public StemsWindow(StemSet stems, string song, string note, Func<IReadOnlyList<string>, Task> keep, Func<Task> save)
    {
        InitializeComponent();
        _mixer = new StemMixer(stems);
        _keep = keep;
        _save = save;
        SongText.Text = song;
        NoteText.Text = note;
        Title = $"Stems of {song} - Lucent";

        for (var s = 0; s < _mixer.Sources.Count; s++)
        {
            Rows.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
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
            row.ShowPlayhead(fraction);
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

    private void OnPlayPause(object sender, RoutedEventArgs e)
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

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        _position = 0;
        ShowStopped();
    }

    private async void OnKeep(object sender, RoutedEventArgs e)
    {
        var audible = _mixer.Audible;
        _player.Stop();
        ShowStopped();
        await _keep(audible);
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await _save();

    /// <summary>The stems were saved as files, <paramref name="file"/> among them: offers to open their folder.</summary>
    public void ShowSaved(string file)
    {
        _savedFile = file;
        ShowFolderButton.Visibility = Visibility.Visible;
    }

    private void OnShowFolder(object sender, RoutedEventArgs e)
    {
        if (_savedFile is not null)
        {
            try
            {
                // Opens File Explorer with the first stem selected.
                Process.Start("explorer.exe", $"/select,\"{_savedFile}\"");
            }
            catch (Exception)
            {
                // Nothing useful to add; File Explorer usually opens.
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
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

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _player.Dispose();
    }

    /// <summary>One stem's row: its name, mute and solo, and its waveform.</summary>
    private sealed class StemRow
    {
        private readonly StemsWindow _window;
        private readonly int _index;
        private readonly ToggleButton _mute;
        private readonly ToggleButton _solo;
        private readonly Path _wave;
        private readonly Rectangle _playhead;
        private readonly Grid _display;

        public StemRow(StemsWindow window, int index)
        {
            _window = window;
            _index = index;
            var source = window._mixer.Sources[index];

            Root = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 0) };
            controls.Children.Add(new TextBlock
            {
                Text = StemNames.Title(source),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)window.FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 6),
            });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            _mute = Toggle(window, "M", $"Mute the {StemNames.List(new[] { source })}");
            _solo = Toggle(window, "S", $"Hear only the {StemNames.List(new[] { source })} (solo)");
            _solo.Margin = new Thickness(4, 0, 0, 0);
            _mute.Click += (_, _) =>
            {
                window._mixer.SetMuted(index, _mute.IsChecked == true);
                window.ShowMix();
            };
            _solo.Click += (_, _) =>
            {
                window._mixer.SetSoloed(index, _solo.IsChecked == true);
                window.ShowMix();
            };
            buttons.Children.Add(_mute);
            buttons.Children.Add(_solo);
            controls.Children.Add(buttons);
            Root.Children.Add(controls);

            _wave = new Path { Data = Outline(StemMixer.Peaks(window._mixer[index], PeakBins)), Stretch = Stretch.Fill };
            _playhead = new Rectangle
            {
                Width = 2,
                HorizontalAlignment = HorizontalAlignment.Left,
                Fill = (Brush)window.FindResource("AccentBrush"),
                RenderTransform = new TranslateTransform(),
            };
            _display = new Grid { Margin = new Thickness(6), ClipToBounds = true, Background = Brushes.Transparent, Cursor = Cursors.Hand };
            _display.Children.Add(_wave);
            _display.Children.Add(_playhead);
            _display.SizeChanged += (_, _) => window.ShowPosition();
            _display.MouseLeftButtonDown += (_, e) =>
            {
                var fraction = Math.Clamp(e.GetPosition(_display).X / Math.Max(1, _display.ActualWidth), 0, 1);
                window.Play((int)(fraction * window.Frames));
            };
            var border = new Border
            {
                Child = _display,
                CornerRadius = (CornerRadius)window.FindResource("ControlCorner"),
                Background = (Brush)window.FindResource("BackgroundBrush"),
                BorderBrush = (Brush)window.FindResource("HairlineBrush"),
                BorderThickness = new Thickness(1),
                ToolTip = "Click to play from here",
            };
            Grid.SetColumn(border, 1);
            Root.Children.Add(border);
        }

        public Grid Root { get; }

        public void ShowState()
        {
            var audible = _window._mixer.IsAudible(_index);
            _wave.Fill = (Brush)_window.FindResource(audible ? "AccentBrush" : "FaintTextBrush");
            _wave.Opacity = audible ? 0.85 : 0.35;
        }

        public void ShowPlayhead(double fraction) =>
            ((TranslateTransform)_playhead.RenderTransform).X = fraction * Math.Max(0, _display.ActualWidth - _playhead.Width);

        private static ToggleButton Toggle(Window window, string text, string tip) => new()
        {
            Style = (Style)window.FindResource("PanelToggle"),
            Content = text,
            ToolTip = tip,
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
        };

        /// <summary>
        /// The waveform's outline, mirrored above and below the middle. An invisible box from -1 to 1 keeps
        /// the scale true when the path is stretched to fit, so a quiet stem looks quiet.
        /// </summary>
        private static Geometry Outline(float[] peaks)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(new Point(0, -1), false, false);
                context.LineTo(new Point(peaks.Length, 1), false, false);

                context.BeginFigure(new Point(0, 0), true, true);
                for (var i = 0; i < peaks.Length; i++)
                {
                    context.LineTo(new Point(i + 0.5, -peaks[i]), true, false);
                }

                for (var i = peaks.Length - 1; i >= 0; i--)
                {
                    context.LineTo(new Point(i + 0.5, peaks[i]), true, false);
                }
            }

            geometry.Freeze();
            return geometry;
        }
    }
}
