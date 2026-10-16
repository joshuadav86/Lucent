using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

public partial class MainWindow : Window
{
    private readonly AudioPlayer _player = new();
    private readonly ObservableCollection<PlaylistEntry> _playlist = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly VisualizerHost _visualizer;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MusicLibrary _library = new();
    private readonly HashSet<string> _collapsedFolders;
    private ListCollectionView? _libraryView;
    private IReadOnlyList<string> _presetNames = Array.Empty<string>();
    private DateTime _presetPopupClosedAt;
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
        {
            EmptyPlaylistHint.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            QueueCount.Text = _playlist.Count switch
            {
                0 => "Empty",
                1 => "1 track",
                var count => $"{count} tracks",
            };
        };

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
            PresetList.ItemsSource = names;
            ShowPreset();
        };
        _visualizer.ProblemReported += (_, message) =>
        {
            VisualizerStatus.Text = message;
            VisualizerStatus.ToolTip = message;
        };
        _visualizer.DoubleClicked += (_, _) => ToggleFullscreen();
        _visualizer.PresetChanged += (_, _) => ShowPreset();
        VisualizerFrame.Child = _visualizer;

        _collapsedFolders = new HashSet<string>(_settings.CollapsedLibraryFolders, StringComparer.OrdinalIgnoreCase);
        _library.ItemsChanged += (_, _) => RebuildLibraryView();
        _library.TagsLoaded += (_, _) =>
        {
            // Titles and artists only become known once tags are read, so re-sort by them now.
            if (_settings.LibrarySort is LibrarySort.Title or LibrarySort.Artist)
            {
                _libraryView?.Refresh();
            }
        };
        _library.ScanningChanged += (_, _) => UpdateLibraryStatus();
        SortLabel.Text = LibrarySorts.Label(_settings.LibrarySort);
        UpdateGroupFoldersButton();
        ApplyPanels();
        _library.SetFolders(_settings.LibraryFolders);
        UpdateLibraryStatus();
    }

    // ---- Visuals ----

    private void OnNextPreset(object sender, RoutedEventArgs e) => ChangePreset(+1);

    private void OnPreviousPreset(object sender, RoutedEventArgs e) => ChangePreset(-1);

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void OnToggleAutoCycle(object sender, RoutedEventArgs e)
    {
        _visualizer.AutoCycle = !_visualizer.AutoCycle;
        AutoCycleButton.Foreground = (System.Windows.Media.Brush)FindResource(
            _visualizer.AutoCycle ? "AccentBrush" : "FaintTextBrush");
    }

    private void OnToggleQueue(object sender, RoutedEventArgs e) => ToggleQueue();

    private void ToggleQueue()
    {
        _settings.ShowQueue = !_settings.ShowQueue;
        ApplyPanels();
        _settings.Save();
    }

    private void OnToggleLibrary(object sender, RoutedEventArgs e) => ToggleLibrary();

    private void ToggleLibrary()
    {
        _settings.ShowLibrary = !_settings.ShowLibrary;
        ApplyPanels();
        _settings.Save();
    }

    /// <summary>Shows the library and queue drawers the user has open, unless the visuals are fullscreen.</summary>
    private void ApplyPanels()
    {
        LibraryPanel.Visibility = !_isFullscreen && _settings.ShowLibrary ? Visibility.Visible : Visibility.Collapsed;
        PlaylistPanel.Visibility = !_isFullscreen && _settings.ShowQueue ? Visibility.Visible : Visibility.Collapsed;
        LibraryButton.Foreground = (Brush)FindResource(_settings.ShowLibrary ? "MutedTextBrush" : "FaintTextBrush");
        QueueButton.Foreground = (Brush)FindResource(_settings.ShowQueue ? "MutedTextBrush" : "FaintTextBrush");
    }

    // ---- Window chrome ----

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

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

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "RestoreIcon" : "MaximizeIcon");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = maximized && !_isFullscreen ? new Thickness(7) : new Thickness(0);
    }

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
        if (_presetNames.Count == 0)
        {
            PresetName.Text = "No visuals found";
            return;
        }

        var index = Math.Clamp(_visualizer.PresetIndex, 0, _presetNames.Count - 1);
        PresetName.Text = _presetNames[index];
        PresetList.SelectedIndex = index;
    }

    private void OnTogglePresetPicker(object sender, RoutedEventArgs e) => TogglePresetPicker();

    private void TogglePresetPicker()
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
        if (index >= 0 && index < _presetNames.Count)
        {
            _visualizer.PresetIndex = index;
            ShowPreset();
        }

        PresetPopup.IsOpen = false;
    }

    /// <summary>Fills the screen with the visuals, hiding everything else (Esc or double-click to leave).</summary>
    private void ToggleFullscreen()
    {
        _isFullscreen = !_isFullscreen;
        var chrome = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        TitleBar.Visibility = chrome;
        BottomBar.Visibility = chrome;
        ApplyPanels();

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

        OnWindowStateChanged(this, EventArgs.Empty);
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

    private void OnClearPlaylist(object sender, RoutedEventArgs e) => ClearQueue();

    private void ClearQueue()
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
        AddToQueue(tracks);
    }

    private void AddToQueue(IReadOnlyList<Track> tracks)
    {
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

    // ---- Library ----

    private void RebuildLibraryView()
    {
        var view = new ListCollectionView(_library.Items.ToList())
        {
            CustomSort = new LibraryItemComparer(_settings.LibrarySort),
        };
        if (_settings.GroupLibraryByFolder)
        {
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryItem.Folder)));
        }

        _libraryView = view;
        LibraryView.ItemsSource = view;
        UpdateLibraryStatus();
    }

    private void UpdateLibraryStatus()
    {
        var count = _library.Items.Count;
        var linked = _library.Folders.Count > 0;
        LibraryCount.Text = !linked
            ? "No folder linked"
            : _library.IsScanning && count == 0
                ? "Looking for music…"
                : $"{count:N0} {(count == 1 ? "track" : "tracks")}{(_library.IsScanning ? "  ·  updating" : string.Empty)}";

        EmptyLibraryHint.Visibility = count == 0 && !_library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryTitle.Text = linked ? "No music found" : "Link a music folder";
        EmptyLibraryText.Text = linked
            ? "None of your library folders have music Lucid can play yet. New files appear here on their own."
            : "Every song inside it shows up here, newest first, and new files appear on their own.";
    }

    private void OnLinkLibraryFolder(object sender, RoutedEventArgs e) => LinkLibraryFolder();

    private void LinkLibraryFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a music folder for your library", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        foreach (var folder in dialog.FolderNames)
        {
            if (!_settings.LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                _settings.LibraryFolders.Add(folder);
            }
        }

        _settings.ShowLibrary = true;
        _settings.Save();
        ApplyPanels();
        _library.SetFolders(_settings.LibraryFolders);
        UpdateLibraryStatus();
    }

    private void OnLibraryFolders(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LibraryFoldersButton, Placement = PlacementMode.Bottom };
        foreach (var folder in _settings.LibraryFolders.ToList())
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            var remove = new MenuItem { Header = $"Remove {(string.IsNullOrEmpty(name) ? folder : name)}", ToolTip = folder };
            remove.Click += (_, _) =>
            {
                _settings.LibraryFolders.RemoveAll(f => string.Equals(f, folder, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                _library.SetFolders(_settings.LibraryFolders);
                UpdateLibraryStatus();
            };
            menu.Items.Add(remove);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var add = new MenuItem { Header = _settings.LibraryFolders.Count == 0 ? "Link a music folder…" : "Link another folder…" };
        add.Click += (_, _) => LinkLibraryFolder();
        menu.Items.Add(add);
        menu.IsOpen = true;
    }

    private async void OnRefreshLibrary(object sender, RoutedEventArgs e) => await _library.RescanAsync();

    private void OnSortMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var sort in LibrarySorts.All)
        {
            var item = new MenuItem { Header = LibrarySorts.Label(sort), IsChecked = sort == _settings.LibrarySort };
            item.Click += (_, _) =>
            {
                _settings.LibrarySort = sort;
                _settings.Save();
                SortLabel.Text = LibrarySorts.Label(sort);
                RebuildLibraryView();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnToggleGroupFolders(object sender, RoutedEventArgs e)
    {
        _settings.GroupLibraryByFolder = !_settings.GroupLibraryByFolder;
        _settings.Save();
        UpdateGroupFoldersButton();
        RebuildLibraryView();
    }

    private void UpdateGroupFoldersButton() =>
        GroupFoldersButton.Foreground = (Brush)FindResource(_settings.GroupLibraryByFolder ? "AccentBrush" : "FaintTextBrush");

    // Folder headings remember whether they were left open or closed.
    private void OnFolderGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } expander)
        {
            expander.IsExpanded = !_collapsedFolders.Contains(group.Name?.ToString() ?? string.Empty);
        }
    }

    private void OnFolderGroupExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Remove(group.Name?.ToString() ?? string.Empty);
        }
    }

    private void OnFolderGroupCollapsed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Add(group.Name?.ToString() ?? string.Empty);
        }
    }

    /// <summary>The library as it's listed on screen: folder by folder when grouped.</summary>
    private List<LibraryItem> LibraryInViewOrder()
    {
        if (_libraryView is null)
        {
            return new List<LibraryItem>();
        }

        if (_libraryView.Groups is { Count: > 0 } groups)
        {
            return groups.Cast<CollectionViewGroup>().SelectMany(group => group.Items.Cast<LibraryItem>()).ToList();
        }

        return _libraryView.Cast<LibraryItem>().ToList();
    }

    private List<LibraryItem> SelectedLibraryItems()
    {
        var selected = new HashSet<LibraryItem>(LibraryView.SelectedItems.Cast<LibraryItem>());
        return LibraryInViewOrder().Where(selected.Contains).ToList();
    }

    private void OnLibraryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows count; double-clicking a folder heading or the scroll bar does nothing.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(LibraryView, source) is ListBoxItem { DataContext: LibraryItem item })
        {
            PlayFromLibrary(item);
        }
    }

    private void OnLibraryKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            PlayFromLibrary(item);
            e.Handled = true;
        }
    }

    private void OnLibraryPlayNow(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            PlayFromLibrary(item);
        }
    }

    private async void OnLibraryAddToQueue(object sender, RoutedEventArgs e)
    {
        var items = SelectedLibraryItems();
        AddToQueue(await Task.Run(() => items.Select(item => item.ToTrack()).ToList()));
    }

    private async void OnLibraryPlayFolder(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is not { } chosen)
        {
            return;
        }

        var folder = LibraryInViewOrder().Where(item => item.Folder == chosen.Folder).ToList();
        var tracks = await Task.Run(() => folder.Select(item => item.ToTrack()).ToList());
        ReplaceQueue(tracks, folder.IndexOf(chosen));
    }

    private async void OnPlayLibrary(object sender, RoutedEventArgs e)
    {
        var items = LibraryInViewOrder();
        var tracks = await Task.Run(() => items.Select(item => item.ToTrack()).ToList());
        ReplaceQueue(tracks, 0);
    }

    private void OnLibraryShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{item.Path}\"");
            }
            catch (Exception ex)
            {
                ShowError("Couldn't open File Explorer.", ex);
            }
        }
    }

    /// <summary>Plays a library track now, queueing it right after the current track if it isn't queued yet.</summary>
    private void PlayFromLibrary(LibraryItem item)
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

    private void ReplaceQueue(IReadOnlyList<Track> tracks, int startIndex)
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
            case Key.Q:
                ToggleQueue();
                break;
            case Key.L:
                ToggleLibrary();
                break;
            case Key.V:
                TogglePresetPicker();
                break;
            case Key.A:
                OnToggleAutoCycle(this, new RoutedEventArgs());
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

    private void UpdatePlayPauseButton()
    {
        var playing = _player.IsPlaying;
        PlayPauseIcon.Data = (Geometry)FindResource(playing ? "PauseIcon" : "PlayIcon");
        // The play triangle looks centred when nudged slightly right.
        PlayPauseIcon.Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
    }

    private void ShowNowPlaying(Track? track)
    {
        if (track is null)
        {
            NowPlayingTitle.Text = "Nothing playing";
            NowPlayingArtist.Text = "Add music to the queue to begin";
            Title = "Lucid";
            return;
        }

        NowPlayingTitle.Text = track.Title;
        NowPlayingArtist.Text = string.IsNullOrEmpty(track.Artist) ? Path.GetFileName(track.Path) : track.Artist;
        Title = $"{track.Title} - Lucid";
    }

    private void ShowError(string message, Exception ex)
    {
        UpdatePlayPauseButton();
        MessageBox.Show(this, $"{message}\n\nDetails: {ex.Message}", "Lucid",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _settings.CollapsedLibraryFolders = _collapsedFolders.ToList();
        _settings.Save();
        _library.Dispose();
        _positionTimer.Stop();
        _visualizer.Dispose();
        _player.Dispose();
    }
}
