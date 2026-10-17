using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Themes;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Windows;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

/// <summary>
/// The player window, and the app's controller: it owns the audio, the queue and the
/// library, and the windows around it (visuals, queue, library) call back into it.
/// </summary>
public partial class MainWindow : Window
{
    private const double MarqueeGap = 48;
    private const double MarqueeSpeed = 36;

    private readonly AudioPlayer _player = new();
    private readonly ObservableCollection<PlaylistEntry> _playlist = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _meterTimer;
    private readonly VisualizerHost _visualizer;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MusicLibrary _library = new();
    private readonly VisualizerWindow _visuals;
    private readonly QueueWindow _queue;
    private readonly LibraryWindow _libraryWindow;
    private readonly SpectrumMeter _meter;
    private readonly WindowSnapper _snapper;
    private readonly Random _random = new();
    // Shuffle remembers what it has played, so it plays everything once before repeating,
    // and so Previous can step back through it.
    private readonly List<PlaylistEntry> _shuffleHistory = new();
    private DateTime _optionsClosedAt;
    private DisplayInfo _display = DisplayInfo.Standard;
    private PlaylistEntry? _current;
    private bool _isDraggingSeek;
    private bool _updatingSeekBar;

    public MainWindow()
    {
        InitializeComponent();

        // The other windows are made after this one, so this stays the app's main window:
        // closing it closes Lucid.
        Application.Current.MainWindow = this;
        _visuals = new VisualizerWindow();
        _queue = new QueueWindow();
        _libraryWindow = new LibraryWindow();

        _snapper = new WindowSnapper(this);
        _snapper.MoveFinished += (_, _) => SavePlacements();

        _queue.PlaylistView.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
        {
            _queue.EmptyPlaylistHint.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _queue.QueueCount.Text = _playlist.Count switch
            {
                0 => "Empty",
                1 => "1 track",
                var count => $"{count} tracks",
            };
            _shuffleHistory.RemoveAll(entry => !_playlist.Contains(entry));
            ShowNowPlaying(_current?.Track);
        };

        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        _player.Volume = (float)VolumeSlider.Value;
        _player.TrackEnded += (_, _) => OnTrackEnded();
        _player.PlaybackFailed += (_, ex) => ShowError("Playback stopped because of an audio device error.", ex);

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) => UpdatePosition();
        _positionTimer.Start();

        _meter = new SpectrumMeter(_player.Capture) { Mode = _settings.Meter };
        MeterHost.Child = _meter;
        _meterTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        _meterTimer.Tick += (_, _) =>
        {
            if (WindowState != WindowState.Minimized)
            {
                _meter.Tick();
            }
        };
        _meterTimer.Start();

        _visualizer = new VisualizerHost(_player.Capture);
        _visualizer.DisplayChanged += (_, display) =>
        {
            _display = display;
            UpdateHdrHint();
        };
        _visualizer.StartPresetName = _settings.LastVisual;
        _visuals.Attach(_visualizer, _settings);
        ApplyVisualizerSettings();

        ThemeManager.Apply(ThemeManager.Find(_settings.Theme));
        BuildThemeSwatches();

        _libraryWindow.Attach(_library, _settings);
        _library.SetFolders(_settings.LibraryFolders);

        foreach (var window in ToolWindows)
        {
            window.Player = this;
            window.IsVisibleChanged += (_, _) => OnToolWindowVisibilityChanged();
        }

        PreviewMouseWheel += OnPreviewMouseWheel;
        if (Saved("Player") is { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Loaded += OnLoaded;
        UpdateShuffleRepeatButtons();
        UpdatePlayPauseButton();
        ShowNowPlaying(null);
    }

    /// <summary>True once the player is closing, so the other windows close for real instead of hiding.</summary>
    public bool IsShuttingDown { get; private set; }

    private IEnumerable<ToolWindow> ToolWindows => new ToolWindow[] { _visuals, _queue, _libraryWindow };

    // ---- Windows ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Owned windows minimize with the player and stay in front of it; this needs the player shown first.
        foreach (var window in ToolWindows)
        {
            window.Owner = this;
            _snapper.Add(window);
        }

        if (Saved("Player") is null)
        {
            ArrangeFirstTime();
        }
        else
        {
            foreach (var window in ToolWindows)
            {
                RestorePlacement(window);
            }
        }

        // The visuals open with the player, unless they were closed last time.
        if (_settings.ShowVisuals)
        {
            _visuals.Show();
        }

        if (_settings.ShowQueue)
        {
            _queue.Show();
        }

        if (_settings.ShowLibrary)
        {
            _libraryWindow.Show();
        }

        UpdateWindowToggles();
        Activate();
    }

    /// <summary>
    /// The first layout, like Winamp's: the queue under the player, the visuals beside them both,
    /// and the library (if it's open) on the other side, centred on the screen.
    /// </summary>
    private void ArrangeFirstTime()
    {
        var work = SystemParameters.WorkArea;
        var playerWidth = ActualWidth;
        var playerHeight = ActualHeight;
        var queueHeight = Math.Clamp(work.Height * 0.36, 220, 360);
        var stackHeight = playerHeight + queueHeight;
        var libraryWidth = _settings.ShowLibrary ? 360.0 : 0.0;
        var visualsWidth = Math.Clamp(work.Width - playerWidth - libraryWidth - 40, 420, 860);
        var groupWidth = libraryWidth + playerWidth + visualsWidth;
        var left = work.Left + Math.Max(0, (work.Width - groupWidth) / 2);
        var top = work.Top + Math.Max(0, (work.Height - stackHeight) / 2);

        Left = left + libraryWidth;
        Top = top;
        Place(_queue, Left, top + playerHeight, playerWidth, queueHeight);
        Place(_visuals, Left + playerWidth, top, visualsWidth, stackHeight);
        if (_settings.ShowLibrary)
        {
            Place(_libraryWindow, left, top, libraryWidth, stackHeight);
        }
    }

    private static void Place(Window window, double left, double top, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
    }

    private void RestorePlacement(ToolWindow window)
    {
        if (Saved(window.PlacementName) is { } placement)
        {
            Place(window, placement.Left, placement.Top, placement.Width, placement.Height);
            return;
        }

        // Never placed before (the library, usually): beside the player where there's room.
        var work = SystemParameters.WorkArea;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var height = Math.Max(window.Height, ActualHeight + (_queue.IsVisible ? _queue.ActualHeight : 0));
        window.Height = Math.Min(height, work.Height);
        window.Left = Left - window.Width >= work.Left ? Left - window.Width : Math.Min(Left + ActualWidth, work.Right - window.Width);
        window.Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
    }

    /// <summary>A saved position, if it's still on one of the screens.</summary>
    private WindowBounds? Saved(string name)
    {
        if (!_settings.Windows.TryGetValue(name, out var bounds) || bounds.Width < 50 || bounds.Height < 50)
        {
            return null;
        }

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
        return visible.IsEmpty || visible.Width < 100 || visible.Height < 40 ? null : bounds;
    }

    private void SavePlacements()
    {
        Remember("Player", this);
        foreach (var window in ToolWindows)
        {
            // While fullscreen, keep the size the visuals will go back to.
            if (window != _visuals || !_visuals.IsFullscreen)
            {
                Remember(window.PlacementName, window);
            }
        }

        _settings.Save();
    }

    private void Remember(string name, Window window)
    {
        if (!window.IsLoaded)
        {
            return;
        }

        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.Windows[name] = new WindowBounds { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height };
        }
    }

    private void OnToggleVisuals(object sender, RoutedEventArgs e) => ToggleWindow(_visuals);

    private void OnToggleQueue(object sender, RoutedEventArgs e) => ToggleWindow(_queue);

    private void OnToggleLibrary(object sender, RoutedEventArgs e) => ToggleWindow(_libraryWindow);

    private void ToggleWindow(ToolWindow window)
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        if (!window.IsLoaded && Saved(window.PlacementName) is null)
        {
            RestorePlacement(window);
        }

        window.Show();
    }

    private void OnToolWindowVisibilityChanged()
    {
        if (IsShuttingDown)
        {
            return;
        }

        _settings.ShowVisuals = _visuals.IsVisible;
        _settings.ShowQueue = _queue.IsVisible;
        _settings.ShowLibrary = _libraryWindow.IsVisible;
        UpdateWindowToggles();
        SavePlacements();
    }

    private void UpdateWindowToggles()
    {
        VisualsToggle.IsChecked = _visuals.IsVisible;
        QueueToggle.IsChecked = _queue.IsVisible;
        LibraryToggle.IsChecked = _libraryWindow.IsVisible;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    // ---- Options ----

    /// <summary>Hands the graphics and visual-change settings to the visualizer.</summary>
    public void ApplyVisualizerSettings()
    {
        _visualizer.TargetPixels = GraphicsOptions.Pixels(_settings.Resolution);
        _visualizer.Quality = GraphicsOptions.Quality(_settings.Detail);
        _visualizer.BloomStrength = GraphicsOptions.BloomStrength(_settings.Glow);
        _visualizer.Hdr = _settings.Hdr;
        _visualizer.AutoCycle = _settings.AutoCycle;
        _visualizer.AutoCycleSeconds = _settings.AutoCycleSeconds;
        _visualizer.TransitionSeconds = GraphicsOptions.Seconds(_settings.TransitionSpeed);
        _visualizer.MorphTransitionsOnly = _settings.MorphTransitionsOnly;
        _visuals.ShowFrameRate(_settings.ShowFrameRate);
        _visuals.UpdateAutoCycleButton();
        AutoCycleSwitch.IsChecked = _settings.AutoCycle;
    }

    private void OnToggleOptions(object sender, RoutedEventArgs e) => ToggleOptions();

    private void ToggleOptions()
    {
        if (OptionsPopup.IsOpen)
        {
            OptionsPopup.IsOpen = false;
            return;
        }

        // The click that closed the panel (by clicking outside it, on this button) shouldn't reopen it.
        if (_visuals.IsFullscreen || (DateTime.UtcNow - _optionsClosedAt).TotalMilliseconds < 250)
        {
            OptionsButton.IsChecked = false;
            return;
        }

        ShowOptionValues();
        UpdateHdrHint();

        // Line the panel's right edge up with the button's (the panel has a 16px margin for its shadow).
        OptionsPopup.HorizontalOffset = OptionsButton.ActualWidth - 356;
        OptionsPopup.IsOpen = true;
    }

    private void OnOptionsOpened(object? sender, EventArgs e) => OptionsButton.IsChecked = true;

    private void OnOptionsClosed(object? sender, EventArgs e)
    {
        OptionsButton.IsChecked = false;
        _optionsClosedAt = DateTime.UtcNow;
    }

    private void OnOptionsTab(object sender, RoutedEventArgs e)
    {
        var tab = (sender as RadioButton)?.Tag as string;
        GraphicsTab.Visibility = tab == "Graphics" ? Visibility.Visible : Visibility.Collapsed;
        VisualsTab.Visibility = tab == "Visuals" ? Visibility.Visible : Visibility.Collapsed;
        ThemeTab.Visibility = tab == "Theme" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A choice in one of the option rows (resolution, detail, glow, timing, theme).</summary>
    private void OnOptionChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { GroupName: var group, Tag: string value })
        {
            return;
        }

        switch (group)
        {
            case "Resolution" when Enum.TryParse<RenderResolution>(value, out var resolution):
                _settings.Resolution = resolution;
                break;
            case "Detail" when Enum.TryParse<DetailLevel>(value, out var detail):
                _settings.Detail = detail;
                break;
            case "Glow" when Enum.TryParse<GlowLevel>(value, out var glow):
                _settings.Glow = glow;
                break;
            case "AutoCycleSeconds" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds):
                _settings.AutoCycleSeconds = seconds;
                break;
            case "TransitionSpeed" when Enum.TryParse<TransitionSpeed>(value, out var speed):
                _settings.TransitionSpeed = speed;
                break;
            case "Theme":
                _settings.Theme = value;
                ThemeManager.Apply(ThemeManager.Find(value));
                break;
            default:
                return;
        }

        ApplyVisualizerSettings();
        _settings.Save();
    }

    /// <summary>One of the on/off switches in the options panel.</summary>
    private void OnOptionSwitched(object sender, RoutedEventArgs e)
    {
        var on = (sender as CheckBox)?.IsChecked == true;
        if (sender == HdrSwitch)
        {
            _settings.Hdr = on;
        }
        else if (sender == FrameRateSwitch)
        {
            _settings.ShowFrameRate = on;
        }
        else if (sender == AutoCycleSwitch)
        {
            _settings.AutoCycle = on;
        }
        else if (sender == MorphsOnlySwitch)
        {
            _settings.MorphTransitionsOnly = on;
        }

        ApplyVisualizerSettings();
        UpdateHdrHint();
        _settings.Save();
    }

    /// <summary>Marks the current settings in the options panel.</summary>
    private void ShowOptionValues()
    {
        foreach (var choice in LogicalDescendants<RadioButton>(OptionsPanel))
        {
            var current = choice.GroupName switch
            {
                "Resolution" => _settings.Resolution.ToString(),
                "Detail" => _settings.Detail.ToString(),
                "Glow" => _settings.Glow.ToString(),
                "AutoCycleSeconds" => _settings.AutoCycleSeconds.ToString(CultureInfo.InvariantCulture),
                "TransitionSpeed" => _settings.TransitionSpeed.ToString(),
                "Theme" => ThemeManager.Find(_settings.Theme).Name,
                _ => null,
            };
            if (current is not null)
            {
                choice.IsChecked = string.Equals(choice.Tag as string, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        HdrSwitch.IsChecked = _settings.Hdr;
        FrameRateSwitch.IsChecked = _settings.ShowFrameRate;
        AutoCycleSwitch.IsChecked = _settings.AutoCycle;
        MorphsOnlySwitch.IsChecked = _settings.MorphTransitionsOnly;
    }

    private void BuildThemeSwatches()
    {
        foreach (var theme in ThemeManager.Themes)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ThemeSwatch"),
                GroupName = "Theme",
                Tag = theme.Name,
                Content = ThemeManager.CreatePreview(theme),
            };
            swatch.Click += OnOptionChosen;
            ThemeSwatches.Children.Add(swatch);
        }
    }

    private void UpdateHdrHint()
    {
        HdrHint.Text = !_display.HdrOn
            ? "Your screen isn't in HDR mode. Turn on \"Use HDR\" in Windows display settings to use this."
            : _settings.Hdr
                ? $"On. Highlights can reach about {_display.PeakNits:0} nits on this screen."
                : "Your screen is in HDR mode, so highlights can shine far brighter than normal white.";
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in LogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    // ---- Adding music ----

    private void OnOpenMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = OpenButton, Placement = PlacementMode.Bottom };
        var files = new MenuItem { Header = "Add files…" };
        files.Click += (_, _) => AddFilesFromDialog(this);
        var folder = new MenuItem { Header = "Add a folder…" };
        folder.Click += (_, _) => AddFolderFromDialog(this);
        menu.Items.Add(files);
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    public async void AddFilesFromDialog(Window owner)
    {
        var patterns = string.Join(";", AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext));
        var dialog = new OpenFileDialog
        {
            Title = "Add music",
            Multiselect = true,
            Filter = $"Music files|{patterns}|All files|*.*",
        };

        if (dialog.ShowDialog(owner) == true)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    public async void AddFolderFromDialog(Window owner)
    {
        var dialog = new OpenFolderDialog { Title = "Add a music folder", Multiselect = true };
        if (dialog.ShowDialog(owner) == true)
        {
            await AddPathsAsync(dialog.FolderNames);
        }
    }

    public async void AddDroppedPaths(IEnumerable<string> paths) => await AddPathsAsync(paths);

    public void ClearQueue()
    {
        _player.Close();
        _current = null;
        _playlist.Clear();
        _shuffleHistory.Clear();
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
        AddToQueue(tracks);
    }

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        var wasEmpty = _playlist.Count == 0;
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        // Start playing straight away when music is added to an empty, idle player.
        if (wasEmpty && _playlist.Count > 0 && !_player.HasTrack)
        {
            PlayEntry(_settings.Shuffle ? _playlist[_random.Next(_playlist.Count)] : _playlist[0]);
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

    public void ShowInExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open File Explorer.", ex);
        }
    }

    /// <summary>Plays a library track now, queueing it right after the current track if it isn't queued yet.</summary>
    public void PlayFromLibrary(LibraryItem item)
    {
        var entry = _playlist.FirstOrDefault(e => string.Equals(e.Track.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new PlaylistEntry(item.ToTrack());
            var index = _current is null ? _playlist.Count : _playlist.IndexOf(_current) + 1;
            _playlist.Insert(index, entry);
        }

        PlayEntry(entry);
    }

    public void ReplaceQueue(IReadOnlyList<Track> tracks, int startIndex)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        ClearQueue();
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        PlayEntry(_playlist[Math.Clamp(startIndex, 0, _playlist.Count - 1)]);
    }

    // ---- Queue ----

    public void RemoveFromQueue(IEnumerable<PlaylistEntry> entries)
    {
        foreach (var entry in entries.ToList())
        {
            if (entry == _current)
            {
                _player.Close();
                _current = null;
            }

            _playlist.Remove(entry);
        }

        ShowNowPlaying(_current?.Track);
        UpdatePlayPauseButton();
    }

    public void PlayEntry(PlaylistEntry entry)
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
        if (!_shuffleHistory.Contains(entry))
        {
            _shuffleHistory.Add(entry);
        }

        _queue.PlaylistView.SelectedItem = entry;
        _queue.PlaylistView.ScrollIntoView(entry);

        _player.Play();
        ShowNowPlaying(entry.Track);
        UpdatePlayPauseButton();
        UpdatePosition();
    }

    private void OnTrackEnded()
    {
        if (_settings.Repeat == RepeatMode.One && _current is not null)
        {
            PlayEntry(_current);
            return;
        }

        PlayNext(wrap: _settings.Repeat == RepeatMode.All);
    }

    private void PlayNext(bool wrap)
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        if (_settings.Shuffle)
        {
            var unplayed = _playlist.Where(entry => !_shuffleHistory.Contains(entry)).ToList();
            if (unplayed.Count == 0)
            {
                if (!wrap)
                {
                    StopAtEnd();
                    return;
                }

                // Everything has played: start a fresh round, avoiding an instant repeat of this track.
                _shuffleHistory.Clear();
                unplayed = _playlist.Where(entry => entry != _current || _playlist.Count == 1).ToList();
            }

            PlayEntry(unplayed[_random.Next(unplayed.Count)]);
            return;
        }

        var index = _current is null ? -1 : _playlist.IndexOf(_current);
        var next = index + 1;
        if (next >= _playlist.Count)
        {
            if (!wrap)
            {
                StopAtEnd();
                return;
            }

            next = 0;
        }

        PlayEntry(_playlist[next]);
    }

    /// <summary>End of the queue: rewind the last track and stop.</summary>
    private void StopAtEnd()
    {
        _player.Stop();
        UpdatePlayPauseButton();
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

        if (_settings.Shuffle && _current is not null)
        {
            var at = _shuffleHistory.IndexOf(_current);
            if (at > 0)
            {
                var previous = _shuffleHistory[at - 1];
                _shuffleHistory.RemoveAt(at);
                PlayEntry(previous);
                return;
            }
        }

        var index = _current is null ? 0 : _playlist.IndexOf(_current);
        PlayEntry(_playlist[Math.Max(0, index - 1)]);
    }

    private void OnToggleShuffle(object sender, RoutedEventArgs e) => ToggleShuffle();

    private void ToggleShuffle()
    {
        _settings.Shuffle = !_settings.Shuffle;
        _shuffleHistory.Clear();
        if (_current is not null)
        {
            _shuffleHistory.Add(_current);
        }

        UpdateShuffleRepeatButtons();
        _settings.Save();
    }

    private void OnCycleRepeat(object sender, RoutedEventArgs e) => CycleRepeat();

    private void CycleRepeat()
    {
        _settings.Repeat = _settings.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        UpdateShuffleRepeatButtons();
        _settings.Save();
    }

    private void UpdateShuffleRepeatButtons()
    {
        ShuffleButton.IsChecked = _settings.Shuffle;
        ShuffleButton.ToolTip = _settings.Shuffle ? "Shuffle is on (S)" : "Shuffle (S)";
        RepeatButton.IsChecked = _settings.Repeat != RepeatMode.Off;
        RepeatOneBadge.Visibility = _settings.Repeat == RepeatMode.One ? Visibility.Visible : Visibility.Collapsed;
        RepeatButton.ToolTip = _settings.Repeat switch
        {
            RepeatMode.All => "Repeating the whole queue. Click to repeat just this track (R)",
            RepeatMode.One => "Repeating this track. Click to stop repeating (R)",
            _ => "Repeat is off. Click to repeat the whole queue (R)",
        };
    }

    // ---- Transport ----

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnStop(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
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
            var entry = _queue.PlaylistView.SelectedItem as PlaylistEntry ?? _playlist.FirstOrDefault();
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

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => HandleShortcut(e);

    /// <summary>The keyboard shortcuts, which work from any of Lucid's windows.</summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        // Typing in a text box (or using Ctrl/Alt shortcuts) isn't a shortcut for the player.
        if (e.OriginalSource is TextBoxBase || Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
            case Key.MediaPlayPause:
                TogglePlayPause();
                break;
            case Key.MediaStop:
                StopPlayback();
                break;
            case Key.Z:
            case Key.MediaPreviousTrack:
                PlayPrevious();
                break;
            case Key.X:
            case Key.MediaNextTrack:
                PlayNext(wrap: true);
                break;
            case Key.F11:
                _visuals.ToggleFullscreen();
                break;
            case Key.Escape when _visuals.IsFullscreen:
                _visuals.ToggleFullscreen();
                break;
            case Key.N:
                _visuals.ChangePreset(+1);
                break;
            case Key.B:
                _visuals.ChangePreset(-1);
                break;
            case Key.V:
                _visuals.TogglePresetPicker();
                break;
            case Key.A:
                _visuals.ToggleAutoCycle();
                AutoCycleSwitch.IsChecked = _settings.AutoCycle;
                break;
            case Key.W:
                ToggleWindow(_visuals);
                break;
            case Key.Q:
                ToggleWindow(_queue);
                break;
            case Key.L:
                ToggleWindow(_libraryWindow);
                break;
            case Key.S:
                ToggleShuffle();
                break;
            case Key.R:
                CycleRepeat();
                break;
            case Key.O:
                Activate();
                ToggleOptions();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The mouse wheel anywhere over the player changes the volume, like Winamp.
        if (OptionsPopup.IsOpen)
        {
            return;
        }

        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(e.Delta) * 0.05, 0, 1);
        e.Handled = true;
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _player.Volume = (float)e.NewValue;
        _settings.Volume = e.NewValue;
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}%";
        }
    }

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
            ShowTime(TimeSpan.FromSeconds(e.NewValue), _player.Duration);
            return;
        }

        // A click on the bar (rather than a drag) jumps straight there.
        _player.Position = TimeSpan.FromSeconds(e.NewValue);
    }

    // ---- Display ----

    private void OnTimeClick(object sender, MouseButtonEventArgs e)
    {
        _settings.ShowRemainingTime = !_settings.ShowRemainingTime;
        _settings.Save();
        UpdatePosition();
    }

    private void OnMeterClick(object sender, MouseButtonEventArgs e)
    {
        _settings.Meter = _settings.Meter switch
        {
            MeterMode.Bars => MeterMode.Scope,
            MeterMode.Scope => MeterMode.Off,
            _ => MeterMode.Bars,
        };
        _meter.Mode = _settings.Meter;
        _settings.Save();
    }

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

        ShowTime(position, duration);
        UpdatePlayState();
    }

    private void ShowTime(TimeSpan position, TimeSpan duration)
    {
        TimeText.Text = _settings.ShowRemainingTime && duration > TimeSpan.Zero
            ? "-" + TimeFormat.Format(duration - position)
            : TimeFormat.Format(position);
    }

    private void UpdatePlayPauseButton()
    {
        var playing = _player.IsPlaying;
        PlayPauseIcon.Data = (Geometry)FindResource(playing ? "PauseIcon" : "PlayIcon");
        // The play triangle looks centred when nudged slightly right.
        PlayPauseIcon.Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        UpdatePlayState();
    }

    /// <summary>The little play / pause / stop mark next to the time.</summary>
    private void UpdatePlayState()
    {
        var icon = _player.IsPlaying ? "PlaySmallIcon" : _player.HasTrack && _player.Position > TimeSpan.Zero ? "PauseIcon" : "StopIcon";
        PlayStateIcon.Data = (Geometry)FindResource(icon);
    }

    private void ShowNowPlaying(Track? track)
    {
        if (track is null)
        {
            SetMarquee("Nothing playing");
            TrackDetails.Text = _playlist.Count == 0 ? "Drop music on any Lucid window to begin" : $"{_playlist.Count} in the queue";
            Title = "Lucid";
            return;
        }

        var name = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} – {track.Title}";
        var number = _current is null ? 0 : _playlist.IndexOf(_current) + 1;
        SetMarquee(number > 0 ? $"{number}. {name}" + (track.Duration > TimeSpan.Zero ? $" ({track.DurationText})" : string.Empty) : name);
        var details = track.AudioDetails;
        TrackDetails.Text = string.IsNullOrEmpty(details) ? Path.GetFileName(track.Path) : details;
        TrackDetails.ToolTip = track.Path;
        Title = $"{name} - Lucid";
    }

    /// <summary>Shows the title, scrolling it sideways when it's too long to fit.</summary>
    private void SetMarquee(string text)
    {
        NowPlayingText.Text = text;
        NowPlayingRepeat.Text = text;
        UpdateMarquee();
    }

    private void OnMarqueeSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarquee();

    private void UpdateMarquee()
    {
        MarqueeStrip.BeginAnimation(Canvas.LeftProperty, null);
        Canvas.SetLeft(MarqueeStrip, 0);

        NowPlayingText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = NowPlayingText.DesiredSize.Width;
        var room = MarqueeArea.ActualWidth;
        if (room <= 0 || textWidth <= room)
        {
            NowPlayingRepeat.Visibility = Visibility.Collapsed;
            return;
        }

        // Two copies side by side: once the first has scrolled out, the second is exactly where it started.
        // Each lap pauses at the start so the beginning of the title can be read.
        NowPlayingRepeat.Visibility = Visibility.Visible;
        NowPlayingRepeat.Margin = new Thickness(MarqueeGap, 0, 0, 0);
        var distance = textWidth + MarqueeGap;
        var pause = TimeSpan.FromSeconds(2);
        var lap = pause + TimeSpan.FromSeconds(distance / MarqueeSpeed);
        var scroll = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = lap };
        scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(pause)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(lap)));
        MarqueeStrip.BeginAnimation(Canvas.LeftProperty, scroll);
    }

    private void ShowError(string message, Exception ex)
    {
        UpdatePlayPauseButton();
        MessageBox.Show(this, $"{message}\n\nDetails: {ex.Message}", "Lucid",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePlacements();
        IsShuttingDown = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settings.CollapsedLibraryFolders = _libraryWindow.CollapsedFolders.ToList();
        _settings.Save();
        _library.Dispose();
        _positionTimer.Stop();
        _meterTimer.Stop();
        _visualizer.Dispose();
        _player.Dispose();
    }
}
