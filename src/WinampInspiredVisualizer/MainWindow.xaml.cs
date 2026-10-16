using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Visualizer;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

public partial class MainWindow : Window
{
    private readonly AudioPlayer _player = new();
    private readonly ObservableCollection<PlaylistEntry> _playlist = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly VisualizerHost _visualizer;
    private IReadOnlyList<string> _presetNames = Array.Empty<string>();
    private bool _isFullscreen;
    private WindowState _windowStateBeforeFullscreen;
    private PlaylistEntry? _current;
    private bool _isDraggingSeek;
    private bool _updatingSeekBar;

    public MainWindow()
    {
        InitializeComponent();

        PlaylistView.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
            EmptyPlaylistHint.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        _player.Volume = (float)VolumeSlider.Value;
        _player.TrackEnded += (_, _) => PlayNext(wrap: false);
        _player.PlaybackFailed += (_, ex) => ShowError("Playback stopped because of an audio device error.", ex);

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _positionTimer.Start();

        _visualizer = new VisualizerHost(_player.Capture);
        _visualizer.PresetsLoaded += (_, names) =>
        {
            _presetNames = names;
            ShowPreset();
        };
        _visualizer.ProblemReported += (_, message) =>
        {
            VisualizerStatus.Text = message;
            VisualizerStatus.ToolTip = message;
        };
        _visualizer.DoubleClicked += (_, _) => ToggleFullscreen();
        VisualizerFrame.Child = _visualizer;
    }

    // ---- Visuals ----

    private void OnNextPreset(object sender, RoutedEventArgs e) => ChangePreset(+1);

    private void OnPreviousPreset(object sender, RoutedEventArgs e) => ChangePreset(-1);

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ChangePreset(int step)
    {
        if (_presetNames.Count == 0)
        {
            return;
        }

        _visualizer.PresetIndex = ((_visualizer.PresetIndex + step) % _presetNames.Count + _presetNames.Count) % _presetNames.Count;
        ShowPreset();
    }

    private void ShowPreset()
    {
        PresetName.Text = _presetNames.Count == 0
            ? "No visuals found"
            : _presetNames[Math.Clamp(_visualizer.PresetIndex, 0, _presetNames.Count - 1)];
    }

    /// <summary>Fills the screen with the visuals, hiding everything else (Esc or double-click to leave).</summary>
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        var chrome = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        HeaderPanel.Visibility = chrome;
        VisualBar.Visibility = chrome;
        PlaylistPanel.Visibility = chrome;
        TransportPanel.Visibility = chrome;
        PlaylistRow.Height = _isFullscreen ? new GridLength(0) : new GridLength(230);
        RootGrid.Margin = _isFullscreen ? new Thickness(0) : new Thickness(20);
        VisualizerFrame.Margin = _isFullscreen ? new Thickness(0) : new Thickness(0, 16, 0, 0);
        VisualizerFrame.BorderThickness = _isFullscreen ? new Thickness(0) : new Thickness(1);

        if (_isFullscreen)
        {
            _windowStateBeforeFullscreen = WindowState;
            // Leave Maximized first so Windows recalculates the size without the title bar.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _windowStateBeforeFullscreen;
        }
    }

    // ---- Adding music ----

    private async void OnAddFiles(object sender, RoutedEventArgs e)
    {
        var patterns = string.Join(";", AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext));
        var dialog = new OpenFileDialog
        {
            Title = "Add music",
            Multiselect = true,
            Filter = $"Music files|{patterns}|All files|*.*",
        };

        if (dialog.ShowDialog(this) == true)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    private async void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Add a music folder", Multiselect = true };
        if (dialog.ShowDialog(this) == true)
        {
            await AddPathsAsync(dialog.FolderNames);
        }
    }

    private void OnClearPlaylist(object sender, RoutedEventArgs e)
    {
        _player.Close();
        _current = null;
        _playlist.Clear();
        ShowNowPlaying(null);
        UpdatePlayPauseButton();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            await AddPathsAsync(paths);
        }
    }

    /// <summary>Adds files and (recursively) folders, reading tags off the UI thread.</summary>
    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var input = paths.ToList();
        var tracks = await Task.Run(() => ExpandPaths(input).Select(Track.FromFile).ToList());

        var wasEmpty = _playlist.Count == 0;
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        // Start playing straight away when music is added to an empty, idle player.
        if (wasEmpty && _playlist.Count > 0 && !_player.HasTrack)
        {
            PlayEntry(_playlist[0]);
        }
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(AudioReaderFactory.IsSupported)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception)
                {
                    // A folder we can't read (permissions, disconnected drive) is skipped.
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
            else if (File.Exists(path) && AudioReaderFactory.IsSupported(path))
            {
                yield return path;
            }
        }
    }

    // ---- Playlist ----

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            PlayEntry(entry);
        }
    }

    private void OnPlaylistKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            PlayEntry(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            RemoveSelected();
            e.Handled = true;
        }
    }

    private void RemoveSelected()
    {
        foreach (var entry in PlaylistView.SelectedItems.Cast<PlaylistEntry>().ToList())
        {
            if (entry == _current)
            {
                _player.Close();
                _current = null;
                ShowNowPlaying(null);
            }

            _playlist.Remove(entry);
        }

        UpdatePlayPauseButton();
    }

    private void PlayEntry(PlaylistEntry entry)
    {
        try
        {
            _player.Load(entry.Track.Path);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't play \"{entry.Track.Title}\". The file may be damaged or in a format Windows can't decode.", ex);
            return;
        }

        if (_current is not null)
        {
            _current.IsCurrent = false;
        }

        _current = entry;
        _current.IsCurrent = true;
        PlaylistView.SelectedItem = entry;
        PlaylistView.ScrollIntoView(entry);

        _player.Play();
        ShowNowPlaying(entry.Track);
        UpdatePlayPauseButton();
    }

    private void PlayNext(bool wrap)
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        var index = _current is null ? -1 : _playlist.IndexOf(_current);
        var next = index + 1;
        if (next >= _playlist.Count)
        {
            if (!wrap)
            {
                // End of the playlist: rewind the last track and stop.
                _player.Stop();
                UpdatePlayPauseButton();
                return;
            }

            next = 0;
        }

        PlayEntry(_playlist[next]);
    }

    private void PlayPrevious()
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        // Like most players: restart the track unless we're right at its start.
        if (_player.HasTrack && _player.Position > TimeSpan.FromSeconds(3))
        {
            _player.Position = TimeSpan.Zero;
            return;
        }

        var index = _current is null ? 0 : _playlist.IndexOf(_current);
        PlayEntry(_playlist[Math.Max(0, index - 1)]);
    }

    // ---- Transport ----

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnStop(object sender, RoutedEventArgs e)
    {
        _player.Stop();
        UpdatePlayPauseButton();
        UpdatePosition();
    }

    private void OnNext(object sender, RoutedEventArgs e) => PlayNext(wrap: true);

    private void OnPrevious(object sender, RoutedEventArgs e) => PlayPrevious();

    private void TogglePlayPause()
    {
        if (!_player.HasTrack)
        {
            var entry = PlaylistView.SelectedItem as PlaylistEntry ?? _playlist.FirstOrDefault();
            if (entry is not null)
            {
                PlayEntry(entry);
            }

            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }

        UpdatePlayPauseButton();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                break;
            case Key.F11:
                ToggleFullscreen();
                break;
            case Key.Escape when _isFullscreen:
                ToggleFullscreen();
                break;
            case Key.N:
                ChangePreset(+1);
                break;
            case Key.B:
                ChangePreset(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        _player.Volume = (float)e.NewValue;

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _isDraggingSeek = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingSeek = false;
        _player.Position = TimeSpan.FromSeconds(SeekBar.Value);
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeekBar)
        {
            return;
        }

        if (_isDraggingSeek)
        {
            ElapsedText.Text = TimeFormat.Format(TimeSpan.FromSeconds(e.NewValue));
            return;
        }

        // A click on the bar (rather than a drag) jumps straight there.
        _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    // ---- Display ----

    private void UpdatePosition()
    {
        if (_isDraggingSeek)
        {
            return;
        }

        var duration = _player.Duration;
        var position = _player.Position;

        _updatingSeekBar = true;
        SeekBar.IsEnabled = _player.HasTrack && duration > TimeSpan.Zero;
        SeekBar.Maximum = Math.Max(1, duration.TotalSeconds);
        SeekBar.Value = Math.Min(position.TotalSeconds, SeekBar.Maximum);
        _updatingSeekBar = false;

        ElapsedText.Text = TimeFormat.Format(position);
        DurationText.Text = TimeFormat.Format(duration);
    }

    private void UpdatePlayPauseButton() =>
        PlayPauseButton.Content = _player.IsPlaying ? "⏸ Pause" : "▶ Play";

    private void ShowNowPlaying(Track? track)
    {
        if (track is null)
        {
            NowPlayingTitle.Text = "Nothing playing";
            NowPlayingArtist.Text = "Add files or drag music here";
            Title = "Winamp-Inspired Visualizer";
            return;
        }

        NowPlayingTitle.Text = track.Title;
        NowPlayingArtist.Text = string.IsNullOrEmpty(track.Artist) ? Path.GetFileName(track.Path) : track.Artist;
        Title = $"{track.Title} - Winamp-Inspired Visualizer";
    }

    private void ShowError(string message, Exception ex)
    {
        UpdatePlayPauseButton();
        MessageBox.Show(this, $"{message}\n\nDetails: {ex.Message}", "Winamp-Inspired Visualizer",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _positionTimer.Stop();
        _visualizer.Dispose();
        _player.Dispose();
    }
}
