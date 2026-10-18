using System.ComponentModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;
using SavedPlaylists = WinampInspiredVisualizer.Playlist.SavedPlaylists;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace Lucent.Mac;

/// <summary>The music library: every song in the linked folders, sorted and grouped by folder.</summary>
/// <remarks>
/// Avalonia's ListBox has no groups, so the list is flat: each folder's heading is a row, followed by its
/// songs while the heading is open. <see cref="_viewItems"/> and <see cref="_groups"/> stand in for WPF's
/// ListCollectionView and its groups.
/// </remarks>
public partial class LibraryWindow : ToolWindow
{
    private MusicLibrary _library = null!;
    private AppSettings _settings = null!;
    private HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private string[] _searchWords = Array.Empty<string>();

    // The list as shown: the songs that pass the filter, in order, and their headings when grouped.
    private readonly AvaloniaList<object> _rows = new();
    private List<LibraryItem> _viewItems = new();
    private List<FolderHeading> _groups = new();
    private bool _hasView;

    // Searching waits for a short pause in the typing, so a big library doesn't re-sort on every key.
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    static LibraryWindow()
    {
        // A class handler runs before the window's own handlers, so ⌘F is the library's before the player
        // sees it, as with the OnPreviewKeyDown override on Windows.
        KeyDownEvent.AddClassHandler<LibraryWindow>((window, e) => window.OnWindowKeyDown(window, e), RoutingStrategies.Tunnel);
    }

    public LibraryWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        PlacementName = "Library";
        _searchTimer.Tick += (_, _) => ApplySearch();
        LibraryView.ItemsSource = _rows;
        LibraryView.ContainerPrepared += OnRowPrepared;

        // Before the list's own handling: the search box's keys, heading clicks, arrow keys and the right-click menu.
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        LibraryView.AddHandler(PointerPressedEvent, OnFolderHeadingMouseDown, RoutingStrategies.Tunnel);
        LibraryView.AddHandler(KeyDownEvent, OnLibraryNavigationKey, RoutingStrategies.Tunnel);
        LibraryView.AddHandler(ContextRequestedEvent, OnLibraryMenuOpening, RoutingStrategies.Tunnel);
    }

    private bool IsSearching => _searchWords.Length > 0;

    /// <summary>Folder headings the user has closed, saved with the settings.</summary>
    public IEnumerable<string> CollapsedFolders => _collapsedFolders;

    /// <summary>Connects the window to the library. Called once, before the window is first shown.</summary>
    public void Attach(MusicLibrary library, AppSettings settings)
    {
        _library = library;
        _settings = settings;
        _collapsedFolders = new HashSet<string>(settings.CollapsedLibraryFolders, StringComparer.OrdinalIgnoreCase);
        library.ItemsChanged += (_, _) => RebuildLibraryView();
        library.TagsLoaded += (_, _) =>
        {
            if (IsSearching)
            {
                // Titles, artists and albums only become known once tags are read, so search again (which
                // also re-sorts). The last tags are put on their rows at background priority, so wait for them.
                Dispatcher.UIThread.Post(() =>
                {
                    RefreshView();
                    UpdateLibraryStatus();
                }, DispatcherPriority.Background);
            }
            else if (_settings.LibrarySort is LibrarySort.Title or LibrarySort.Artist)
            {
                // Titles and artists only become known once tags are read, so re-sort by them now.
                RefreshView();
            }
        };
        library.ScanningChanged += (_, _) => UpdateLibraryStatus();
        TrackStats.Shared.Changed += (_, _) => Dispatcher.UIThread.Post(StatsChanged);
        SortLabel.Text = LibrarySorts.Label(settings.LibrarySort);
        UpdateGroupFoldersButton();
        UpdateLibraryStatus();
        LoadPlaylists();
    }

    private bool ShowingPlaylists => _settings.LibraryShowPlaylists;

    /// <summary>Shows new play counts and ratings, and re-sorts if the order depends on them.</summary>
    private void StatsChanged()
    {
        foreach (var item in _library.Items.Concat(_playlistItems))
        {
            item.StatsChanged();
        }

        if (!ShowingPlaylists && _settings.LibrarySort is LibrarySort.MostPlayed or LibrarySort.RecentlyPlayed or LibrarySort.Rating)
        {
            RefreshView();
        }
    }

    /// <summary>The Rating submenu for the chosen songs: one to five stars, or none.</summary>
    private static MenuItem RatingMenu(IReadOnlyList<LibraryItem> items)
    {
        var paths = items.Select(item => item.Path).ToList();
        var current = items.Select(item => item.Rating).Distinct().ToList();
        var menu = new MenuItem { Header = "Rating" };
        for (var stars = 5; stars >= 0; stars--)
        {
            var value = stars;
            var item = new MenuItem
            {
                Header = stars == 0 ? "No rating" : TrackStats.Stars(stars),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = current.Count == 1 && current[0] == stars,
            };
            item.Click += (_, _) => Task.Run(() => TrackStats.Shared.SetRating(paths, value));
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>A new list: re-filtered, re-sorted and regrouped, with nothing selected.</summary>
    private void RebuildLibraryView()
    {
        BuildView(keepSelection: false);
        UpdateLibraryStatus();
    }

    /// <summary>The same list, re-filtered and re-sorted (like WPF's ListCollectionView.Refresh), keeping the selection.</summary>
    private void RefreshView()
    {
        if (_hasView)
        {
            BuildView(keepSelection: true);
        }
    }

    private void BuildView(bool keepSelection)
    {
        var selected = keepSelection ? SelectedLibraryItems() : new List<LibraryItem>();
        var scroller = LibraryView.FindDescendantOfType<ScrollViewer>();
        var offset = scroller?.Offset;

        // Playlists keep their own order, under a heading each.
        var items = (ShowingPlaylists ? _playlistItems : _library.Items).ToList();
        if (LibraryFilter() is { } filter)
        {
            items = items.Where(filter).ToList();
        }

        if (!ShowingPlaylists)
        {
            var comparer = new LibraryItemComparer(_settings.LibrarySort);
            items.Sort(comparer.Compare);
        }

        _viewItems = items;
        _hasView = true;

        foreach (var heading in _groups)
        {
            heading.PropertyChanged -= OnHeadingChanged;
        }

        // Groups come in the order their first song does, as in WPF.
        _groups = new List<FolderHeading>();
        if (ShowingPlaylists || _settings.GroupLibraryByFolder)
        {
            // A store's folder (Bandcamp, Beatport) is one heading, with a subheading for each folder inside it.
            var sections = new List<FolderHeading>();
            var byName = new Dictionary<string, FolderHeading>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!byName.TryGetValue(item.Section, out var heading))
                {
                    heading = NewHeading(item.Section);
                    byName[item.Section] = heading;
                    sections.Add(heading);
                }

                heading.Items.Add(item);
                if (item.IsInSection)
                {
                    var key = "\0" + item.Folder;
                    if (!byName.TryGetValue(key, out var sub))
                    {
                        sub = NewHeading(item.Folder, item.SubFolder);
                        byName[key] = sub;
                        heading.Subheadings.Add(sub);
                    }

                    sub.Items.Add(item);
                }
            }

            foreach (var heading in sections)
            {
                _groups.Add(heading);
                _groups.AddRange(heading.Subheadings);
            }

            FolderHeading NewHeading(string name, string? label = null)
            {
                // While searching every heading is open, so no match is hidden in a closed one.
                var heading = new FolderHeading(name, IsSearching || !_collapsedFolders.Contains(name), label, label is not null)
                {
                    IsHighlighted = _highlightedFolders.Contains(name),
                };
                heading.PropertyChanged += OnHeadingChanged;
                return heading;
            }
        }

        var rows = new List<object>(items.Count + _groups.Count);
        if (_groups.Count > 0)
        {
            foreach (var heading in _groups.Where(heading => !heading.IsSubheading))
            {
                rows.Add(heading);
                if (heading.IsExpanded)
                {
                    rows.AddRange(heading.OpenRows());
                }
            }
        }
        else
        {
            rows.AddRange(items);
        }

        LibraryView.SelectedItems?.Clear();
        _rows.Clear();
        _rows.AddRange(rows);

        if (selected.Count > 0)
        {
            var visible = new HashSet<object>(rows);
            foreach (var item in selected.Where(visible.Contains))
            {
                LibraryView.SelectedItems?.Add(item);
            }
        }

        if (keepSelection && scroller is not null && offset is { } kept)
        {
            Dispatcher.UIThread.Post(() => scroller.Offset = kept, DispatcherPriority.Loaded);
        }

        UpdateFoldAllButton();
    }

    /// <summary>Which songs the list shows: the favourites when FAVOURITES is on, and the ones matching the search. Null shows them all.</summary>
    private Func<LibraryItem, bool>? LibraryFilter()
    {
        var favorites = _settings.LibraryFavoritesOnly
            ? new HashSet<string>(_settings.FavoriteTracks, StringComparer.OrdinalIgnoreCase)
            : null;
        var words = _searchWords;
        if (favorites is null && words.Length == 0)
        {
            return null;
        }

        return song => !song.IsPlaceholder
            && (favorites is null || favorites.Contains(song.Path))
            && SearchText.Matches(song.SearchKey, words);
    }

    private void UpdateLibraryStatus()
    {
        if (ShowingPlaylists)
        {
            UpdatePlaylistsStatus();
            return;
        }

        var count = _library.Items.Count;
        var linked = _library.Folders.Count > 0;
        var favoritesOnly = _settings.LibraryFavoritesOnly && linked && count > 0;
        var searching = IsSearching && linked && count > 0;
        var shown = favoritesOnly || searching ? (_hasView ? _viewItems.Count : 0) : count;
        var updating = _library.IsScanning ? "  ·  updating" : string.Empty;
        string countText;
        if (searching)
        {
            // "12 of 1,234 tracks": how many match out of what's being searched.
            var favorites = new HashSet<string>(_settings.FavoriteTracks, StringComparer.OrdinalIgnoreCase);
            var searched = favoritesOnly ? _library.Items.Count(item => favorites.Contains(item.Path)) : count;
            var noun = favoritesOnly ? (searched == 1 ? "favorite" : "favorites") : (searched == 1 ? "track" : "tracks");
            countText = $"{shown:N0} of {searched:N0} {noun}{updating}";
        }
        else
        {
            countText = favoritesOnly
                ? $"{shown:N0} {(shown == 1 ? "favorite" : "favorites")}{updating}"
                : $"{count:N0} {(count == 1 ? "track" : "tracks")}{updating}";
        }

        LibraryCount.Text = !linked
            ? "No folder linked"
            : _library.IsScanning && count == 0
                ? "Looking for music…"
                : countText;

        var noMatches = searching && shown == 0;
        EmptyLibraryHint.IsVisible = shown == 0 && !_library.IsScanning;
        EmptyLibraryTitle.Text = noMatches ? "No sounds match your search"
            : favoritesOnly ? "No favorites here yet"
            : linked ? "No music found"
            : "Link a music folder";
        EmptyLibraryText.Text = noMatches
            ? favoritesOnly
                ? "Only your favorites are searched while FAVORITES is on. Try other words, or press Esc to clear the search."
                : "Search looks in titles, artists, albums, and file names. Try other words, or press Esc to clear the search."
            : favoritesOnly
                ? "Click the heart in the player, or right-click sounds here or in the queue, to make them favorites."
                : linked
                    ? "None of your library folders have music Lucent can play yet. New files appear here on their own."
                    : "Every sound inside it shows up here, newest first, and new files appear on their own.";
        LinkFolderButton.IsVisible = !(favoritesOnly || noMatches);
        EmptyLibraryHint.IsHitTestVisible = true;
    }

    // ---- Playlists ----
    // PLAYLISTS swaps the library folders for the saved playlists: each playlist is a heading with its
    // sounds under it, in the playlist's own order. The favourites filter and the search still apply.

    private List<LibraryItem> _playlistItems = new();
    private int _playlistLoad;

    /// <summary>Called by the player whenever a playlist is saved, renamed or deleted.</summary>
    public void PlaylistsChanged() => LoadPlaylists();

    private async void LoadPlaylists()
    {
        var load = ++_playlistLoad;
        var known = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _library.Items)
        {
            known[item.Path] = item;
        }

        List<(string Name, List<(string Path, DateTime Added)> Files)> playlists;
        try
        {
            playlists = await Task.Run(() => SavedPlaylists.Names()
                .Select(name => (name, SavedPlaylists.Read(name)
                    .Select(path => (path, known.TryGetValue(path, out var song) ? song.Added : AddedTime(path)))
                    .ToList()))
                .ToList());
        }
        catch (Exception)
        {
            // A playlist file that can't be read right now; the next change tries again.
            return;
        }

        if (load != _playlistLoad)
        {
            return;
        }

        var items = new List<LibraryItem>();
        var untagged = new List<LibraryItem>();
        foreach (var (name, files) in playlists)
        {
            if (files.Count == 0)
            {
                // An empty playlist still gets its heading, so it can be found, renamed and filled.
                items.Add(LibraryItem.Placeholder(name, "Empty. Right-click sounds and choose Add to playlist."));
                continue;
            }

            foreach (var (path, added) in files)
            {
                var item = new LibraryItem(path, name, Path.GetDirectoryName(path) ?? string.Empty, added);
                if (known.TryGetValue(path, out var song) && song.Track is { } track)
                {
                    item.ApplyTags(track);
                }
                else
                {
                    untagged.Add(item);
                }

                items.Add(item);
            }
        }

        _playlistItems = items;
        if (ShowingPlaylists)
        {
            RebuildLibraryView();
        }

        // Sounds outside the library folders get their titles and artists read here.
        if (untagged.Count > 0)
        {
            var tracks = await Task.Run(() => untagged.Select(item => Track.FromFile(item.Path)).ToList());
            if (load != _playlistLoad)
            {
                return;
            }

            for (var i = 0; i < untagged.Count; i++)
            {
                untagged[i].ApplyTags(tracks[i]);
            }

            if (ShowingPlaylists && IsSearching)
            {
                RefreshView();
                UpdateLibraryStatus();
            }
        }
    }

    private static DateTime AddedTime(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.CreationTime > info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
        }
        catch (Exception)
        {
            return DateTime.MinValue;
        }
    }

    private void UpdatePlaylistsStatus()
    {
        var total = _playlistItems.Count(item => !item.IsPlaceholder);
        var lists = _playlistItems.Select(item => item.Folder).Distinct().Count();
        var filtered = IsSearching || _settings.LibraryFavoritesOnly;
        var shown = filtered ? (_hasView ? _viewItems.Count : 0) : total;
        LibraryCount.Text = lists == 0
            ? "No playlists"
            : filtered
                ? $"{shown:N0} of {total:N0} {(total == 1 ? "sound" : "sounds")}"
                : $"{lists:N0} {(lists == 1 ? "playlist" : "playlists")}  ·  {total:N0} {(total == 1 ? "sound" : "sounds")}";

        // Empty playlists show their headings, so the hint is only for no playlists at all, or no matches.
        EmptyLibraryHint.IsVisible = lists == 0 || (filtered && shown == 0);

        // Right-clicks go through the hint to the list underneath, for New playlist….
        EmptyLibraryHint.IsHitTestVisible = false;
        EmptyLibraryTitle.Text = lists == 0 ? "No playlists yet"
            : IsSearching ? "No sounds match your search"
            : "No favorites in your playlists";
        EmptyLibraryText.Text = lists == 0
            ? "Right-click here and choose New playlist, or right-click sounds and choose Add to playlist."
            : IsSearching
                ? "Search looks in titles, artists, albums, and file names. Try other words, or press Esc to clear the search."
                : "Turn FAVORITES off to see every sound in your playlists.";
        LinkFolderButton.IsVisible = false;
    }

    private void OnTogglePlaylists(object? sender, RoutedEventArgs e) => SetShowingPlaylists(!ShowingPlaylists);

    private void SetShowingPlaylists(bool show)
    {
        _settings.LibraryShowPlaylists = show;
        _settings.Save();
        HighlightFolders(Array.Empty<string>());
        UpdateGroupFoldersButton();
        RebuildLibraryView();
    }

    /// <summary>The menu for highlighted playlist headings: play them, rename or delete one.</summary>
    private void OpenPlaylistMenu()
    {
        var menu = new ContextMenu { PlacementTarget = LibraryView, Placement = PlacementMode.Pointer };
        var names = HighlightedGroups().Select(group => group.Name).ToList();
        var songs = HighlightedGroups().SelectMany(group => group.Items)
            .Where(item => !item.IsPlaceholder)
            .ToList();
        if (songs.Count > 0)
        {
            var play = new MenuItem { Header = names.Count == 1 ? "Play this playlist" : "Play these playlists" };
            play.Click += async (_, _) =>
            {
                var tracks = await Task.Run(() => songs.Select(item => item.ToTrack()).ToList());
                Player?.ReplaceQueue(tracks, 0);
            };
            var queue = new MenuItem { Header = "Add to queue" };
            queue.Click += async (_, _) =>
            {
                var tracks = await Task.Run(() => songs.Select(item => item.ToTrack()).ToList());
                Player?.AddToQueue(tracks);
            };
            menu.Items.Add(play);
            menu.Items.Add(queue);
        }

        if (names.Count == 1 && Player is { } player)
        {
            var name = names[0];
            menu.Items.Add(new Separator());
            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => player.RenamePlaylist(this, name);
            var delete = new MenuItem { Header = "Delete playlist" };
            delete.Click += (_, _) => player.DeletePlaylist(this, name);
            menu.Items.Add(rename);
            menu.Items.Add(delete);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var create = new MenuItem { Header = "New playlist…" };
        create.Click += (_, _) => Player?.CreatePlaylist(this);
        menu.Items.Add(create);

        var folders = new MenuItem { Header = "Back to library folders" };
        folders.Click += (_, _) => SetShowingPlaylists(false);
        menu.Items.Add(folders);
        menu.Open(LibraryView);
    }

    // ---- Search ----

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? string.Empty;
        ClearSearchButton.IsVisible = text.Length > 0;
        _searchTimer.Stop();
        if (text.Length == 0)
        {
            // Clearing the search shows everything again straight away.
            ApplySearch();
        }
        else
        {
            _searchTimer.Start();
        }
    }

    /// <summary>Narrows the list to the songs matching what's typed in the search box.</summary>
    private void ApplySearch()
    {
        _searchTimer.Stop();
        var words = SearchText.Words(SearchBox.Text);
        if (words.SequenceEqual(_searchWords))
        {
            return;
        }

        _searchWords = words;
        if (_hasView)
        {
            // Setting the filter re-sorts and regroups the list.
            RefreshView();
            UpdateLibraryStatus();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when !string.IsNullOrEmpty(SearchBox.Text):
                SearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Escape:
                // Already empty: hand the keys back to the list (and the player's shortcuts).
                LibraryView.Focus();
                e.Handled = true;
                break;
            case Key.Down:
            case Key.Enter:
                // Into the results: the first match is selected, and Enter again plays it.
                ApplySearch();
                if (LibraryInViewOrder().FirstOrDefault() is { } first)
                {
                    ListRows.Focus(LibraryView, first);
                }

                e.Handled = true;
                break;
        }
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    /// <summary>Puts the keyboard in the search box, with what's there selected so typing replaces it.</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        // ⌘F, as Ctrl+F on Windows.
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Meta)
        {
            FocusSearch();
            e.Handled = true;
        }
    }

    private void OnLinkLibraryFolder(object? sender, RoutedEventArgs e) => LinkLibraryFolder();

    private async void LinkLibraryFolder()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a music folder for your library",
            AllowMultiple = true,
        });
        var paths = folders.Select(folder => folder.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        foreach (var folder in paths)
        {
            LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
        }

        ApplyLibraryFolders();
    }

    /// <summary>
    /// Music dropped on the library goes into it: folders become library folders, and single files are
    /// added on their own. Nothing is played or queued.
    /// </summary>
    protected override void OnMusicDropped(IReadOnlyList<string> paths)
    {
        var changed = false;
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, path);
                changed = true;
            }
            else if (File.Exists(path) && AudioReaderFactory.IsSupported(path)
                && !_settings.LibraryFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settings.LibraryFiles.Add(path);
                changed = true;
            }
        }

        if (changed)
        {
            ApplyLibraryFolders();
        }
    }

    /// <summary>Takes folders out of the library. Only the library forgets them; the files stay on disk.</summary>
    private void RemoveLibraryFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            LibraryFolderList.Remove(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
            LibraryFolderList.RemoveFilesIn(_settings.LibraryFiles, folder);
        }

        HighlightFolders(Array.Empty<string>());
        ApplyLibraryFolders();
    }

    private void ApplyLibraryFolders()
    {
        _settings.Save();
        _library.SetFolders(_settings.LibraryFolders, _settings.HiddenLibraryFolders, _settings.LibraryFiles);
        UpdateLibraryStatus();
    }

    private void OnLibraryFolders(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LibraryFoldersButton, Placement = PlacementMode.Bottom };
        foreach (var folder in _settings.LibraryFolders.ToList())
        {
            var remove = new MenuItem { Header = $"Remove {LibraryFolderList.Name(folder)}" };
            ToolTip.SetTip(remove, folder);
            remove.Click += (_, _) => RemoveLibraryFolders(new[] { folder });
            menu.Items.Add(remove);
        }

        // Folders taken out from inside a linked one can be brought back from here.
        foreach (var folder in _settings.HiddenLibraryFolders.ToList())
        {
            var show = new MenuItem { Header = $"Bring back {LibraryFolderList.Name(folder)}" };
            ToolTip.SetTip(show, folder);
            show.Click += (_, _) =>
            {
                LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
                ApplyLibraryFolders();
            };
            menu.Items.Add(show);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var add = new MenuItem { Header = _settings.LibraryFolders.Count == 0 ? "Link a music folder…" : "Link another folder…" };
        add.Click += (_, _) => LinkLibraryFolder();
        menu.Items.Add(add);
        var stores = new MenuItem { Header = "Bandcamp and Beatport…" };
        ToolTip.SetTip(stores, "Add the music you buy on Bandcamp and Beatport to the library");
        stores.Click += (_, _) => Player?.ShowStores();
        menu.Items.Add(stores);
        var import = new MenuItem { Header = "Import from another player…" };
        ToolTip.SetTip(import, "Bring playlists, play counts and ratings over from Apple Music, foobar2000, Plex or YouTube Music");
        import.Click += (_, _) => Player?.ShowImport();
        menu.Items.Add(import);
        if (RemoveAllFoldersMenuItem() is { } removeAll)
        {
            menu.Items.Add(removeAll);
        }

        menu.Open(LibraryFoldersButton);
    }

    /// <summary>"Remove all folders…", or null when the library has none.</summary>
    private MenuItem? RemoveAllFoldersMenuItem()
    {
        if (_settings.LibraryFolders.Count == 0)
        {
            return null;
        }

        var removeAll = new MenuItem { Header = "Remove all folders…" };
        ToolTip.SetTip(removeAll, "Empties the library. The files stay on your Mac.");
        removeAll.Click += (_, _) => RemoveAllLibraryFolders();
        return removeAll;
    }

    /// <summary>Takes every folder out of the library, after asking. Nothing on disk is touched.</summary>
    private async void RemoveAllLibraryFolders()
    {
        var count = _settings.LibraryFolders.Count;
        var answer = await MessageWindow.ShowAsync(this,
            $"Remove {(count == 1 ? "the 1 folder" : $"all {count} folders")} from your library?\n\n" +
            "Your music files stay where they are. You can add the folders back at any time.",
            "Library", MessageButtons.YesNo, MessageResult.No);
        if (answer != MessageResult.Yes)
        {
            return;
        }

        _settings.LibraryFolders.Clear();
        _settings.HiddenLibraryFolders.Clear();
        _settings.LibraryFiles.Clear();
        HighlightFolders(Array.Empty<string>());
        ApplyLibraryFolders();
    }

    private MenuItem AddFolderMenuItem()
    {
        var add = new MenuItem { Header = "Add a folder to the library…" };
        add.Click += (_, _) => LinkLibraryFolder();
        return add;
    }

    private MenuItem RemoveFoldersMenuItem(IReadOnlyList<string> folders)
    {
        var remove = new MenuItem
        {
            Header = folders.Count == 1
                ? $"Remove “{LibraryFolderList.Name(folders[0])}” from library"
                : $"Remove {folders.Count} folders from library",
        };
        ToolTip.SetTip(remove, "Takes the folder, and the folders inside it, out of the library. The files stay on your Mac.");
        remove.Click += (_, _) => RemoveLibraryFolders(folders);
        return remove;
    }

    private async void OnRefreshLibrary(object? sender, RoutedEventArgs e) => await _library.RescanAsync();

    private void OnSortMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var sort in LibrarySorts.All)
        {
            var item = new MenuItem
            {
                Header = LibrarySorts.Label(sort),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = sort == _settings.LibrarySort,
            };
            item.Click += (_, _) =>
            {
                _settings.LibrarySort = sort;
                _settings.Save();
                SortLabel.Text = LibrarySorts.Label(sort);
                RebuildLibraryView();
            };
            menu.Items.Add(item);
        }

        menu.Open(SortButton);
    }

    private void OnToggleGroupFolders(object? sender, RoutedEventArgs e)
    {
        if (ShowingPlaylists)
        {
            // Back from the playlists to the library folders, with their headings on.
            _settings.GroupLibraryByFolder = true;
            SetShowingPlaylists(false);
            return;
        }

        _settings.GroupLibraryByFolder = !_settings.GroupLibraryByFolder;
        _settings.Save();
        UpdateGroupFoldersButton();
        RebuildLibraryView();
    }

    private void UpdateGroupFoldersButton()
    {
        GroupFoldersButton.SetResourceReference(ForegroundProperty,
            _settings.GroupLibraryByFolder && !ShowingPlaylists ? "AccentBrush" : "FaintTextBrush");
        PlaylistsButton.SetResourceReference(ForegroundProperty, ShowingPlaylists ? "AccentBrush" : "FaintTextBrush");

        // Playlists keep their own order, so there's nothing to sort.
        SortButton.IsEnabled = !ShowingPlaylists;
        FavoritesOnlyButton.SetResourceReference(ForegroundProperty, _settings.LibraryFavoritesOnly ? "AccentBrush" : "FaintTextBrush");
    }

    private void OnToggleFavoritesOnly(object? sender, RoutedEventArgs e)
    {
        _settings.LibraryFavoritesOnly = !_settings.LibraryFavoritesOnly;
        _settings.Save();
        UpdateGroupFoldersButton();
        RebuildLibraryView();
    }

    /// <summary>Called by the player when songs become favourites or stop being them.</summary>
    public void FavoritesChanged()
    {
        if (_settings.LibraryFavoritesOnly)
        {
            RebuildLibraryView();
        }
    }

    // Favourite, Add to playlist and the folder items depend on what's selected, so they're made fresh each time.
    private readonly List<Control> _songMenuItems = new();

    private void OnLibraryMenuOpening(object? sender, ContextRequestedEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            LibraryMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var source = e.Source as Visual;
        if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: LibraryItem { IsPlaceholder: false } })
        {
            // A folder heading, or the empty space below the songs, gets the folder menu instead.
            e.Handled = true;
            if (source?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: FolderHeading heading }
                && !_highlightedFolders.Contains(heading.Name))
            {
                // Right-clicking a heading outside the highlight highlights just that one.
                HighlightFolders(new[] { heading.Name });
            }

            if (ShowingPlaylists)
            {
                OpenPlaylistMenu();
            }
            else
            {
                OpenFolderMenu();
            }

            return;
        }

        var items = SelectedLibraryItems();
        if (Player is null || items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        _songMenuItems.Add(Player.FavoriteMenuItem(items.Select(item => item.Path).ToList()));
        _songMenuItems.Add(RatingMenu(items));
        _songMenuItems.Add(Player.AddToPlaylistMenu(this, () => Task.Run(() => items.Select(item => item.ToTrack()).ToList())));
        _songMenuItems.Add(new Separator());
        var at = LibraryMenu.Items.IndexOf(SongMenuItemsStart) + 1;
        foreach (var item in _songMenuItems)
        {
            LibraryMenu.Items.Insert(at++, item);
        }

        if (ShowingPlaylists)
        {
            // Under PLAYLISTS the bottom item takes the sounds out of the playlists they're listed in.
            var remove = new MenuItem { Header = "Remove from playlist" };
            remove.Click += (_, _) =>
            {
                foreach (var playlist in items.GroupBy(item => item.Folder))
                {
                    Player?.RemoveFromPlaylist(playlist.Key, playlist.Select(item => item.Path).ToList());
                }
            };
            foreach (var item in new Control[] { new Separator(), remove })
            {
                LibraryMenu.Items.Add(item);
                _songMenuItems.Add(item);
            }

            return;
        }

        // The folders the chosen songs are in, at the bottom.
        var folders = items.Select(item => item.Directory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folderItems = new Control[] { new Separator(), AddFolderMenuItem(), RemoveFoldersMenuItem(folders) };
        foreach (var item in folderItems)
        {
            LibraryMenu.Items.Add(item);
            _songMenuItems.Add(item);
        }
    }

    /// <summary>The menu for the highlighted folder headings: play them, or add and remove library folders.</summary>
    private void OpenFolderMenu()
    {
        var menu = new ContextMenu { PlacementTarget = LibraryView, Placement = PlacementMode.Pointer };
        var groups = HighlightedGroups();
        var songs = groups.SelectMany(group => group.Items).Distinct().ToList();
        if (songs.Count > 0)
        {
            var play = new MenuItem { Header = groups.Count == 1 ? "Play this folder" : "Play these folders" };
            play.Click += async (_, _) =>
            {
                var tracks = await Task.Run(() => songs.Select(item => item.ToTrack()).ToList());
                Player?.ReplaceQueue(tracks, 0);
            };
            var queue = new MenuItem { Header = "Add to queue" };
            queue.Click += async (_, _) =>
            {
                var tracks = await Task.Run(() => songs.Select(item => item.ToTrack()).ToList());
                Player?.AddToQueue(tracks);
            };
            menu.Items.Add(play);
            menu.Items.Add(queue);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(AddFolderMenuItem());
        if (songs.Count > 0)
        {
            var remove = RemoveFoldersMenuItem(HighlightedDirectories());
            remove.InputGesture = new KeyGesture(Key.Back);
            menu.Items.Add(remove);
        }

        if (RemoveAllFoldersMenuItem() is { } removeAll)
        {
            menu.Items.Add(removeAll);
        }

        if (groups.Count == 1 && songs.Count > 0)
        {
            var directory = groups[0].Directory ?? songs[0].Directory;
            menu.Items.Add(new Separator());
            var show = new MenuItem { Header = "Show in Finder" };
            show.Click += (_, _) => Player?.ShowInExplorer(directory);
            menu.Items.Add(show);
        }

        menu.Open(LibraryView);
    }

    // ---- Highlighting folder headings ----
    // ⌘-click a heading to highlight it, Shift-click to highlight a run of them, then right-click
    // them or press Delete to take them out of the library. A plain click still opens or closes one.

    private readonly HashSet<string> _highlightedFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? _folderAnchor;

    private void OnFolderHeadingMouseDown(object? sender, PointerPressedEventArgs e)
    {
        // Only the heading itself, with the left button; clicks on the songs go to the list as usual.
        if (!e.GetCurrentPoint(LibraryView).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: FolderHeading heading })
        {
            return;
        }

        var name = heading.Name;
        var modifiers = e.KeyModifiers;
        if (modifiers.HasFlag(KeyModifiers.Shift) && _folderAnchor is not null)
        {
            var names = GroupNames();
            var from = names.FindIndex(n => string.Equals(n, _folderAnchor, StringComparison.OrdinalIgnoreCase));
            var to = names.IndexOf(name);
            if (from >= 0 && to >= 0)
            {
                var run = names.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
                HighlightFolders(modifiers.HasFlag(KeyModifiers.Meta) ? _highlightedFolders.Concat(run) : run, keepAnchor: true);
            }
        }
        else if (modifiers.HasFlag(KeyModifiers.Meta) || modifiers.HasFlag(KeyModifiers.Shift))
        {
            var highlighted = _highlightedFolders.ToList();
            if (!highlighted.Remove(name))
            {
                highlighted.Add(name);
            }

            HighlightFolders(highlighted);
            _folderAnchor = name;
        }
        else
        {
            // A plain click opens or closes the folder, and ends any highlight.
            HighlightFolders(Array.Empty<string>());
            return;
        }

        // Keep the folder open or closed, and take the keys so Delete works next.
        e.Handled = true;
        LibraryView.SelectedItems?.Clear();
        LibraryView.Focus();
    }

    private void HighlightFolders(IEnumerable<string> names, bool keepAnchor = false)
    {
        var list = names.ToList();
        _highlightedFolders.Clear();
        _highlightedFolders.UnionWith(list);
        if (!keepAnchor)
        {
            _folderAnchor = list.LastOrDefault();
        }

        foreach (var heading in _groups)
        {
            heading.IsHighlighted = _highlightedFolders.Contains(heading.Name);
        }
    }

    private List<string> GroupNames() => _groups.Select(group => group.Name).ToList();

    private List<FolderHeading> HighlightedGroups() =>
        _groups.Where(group => _highlightedFolders.Contains(group.Name)).ToList();

    /// <summary>The folders on disk behind the highlighted headings.</summary>
    private List<string> HighlightedDirectories() => HighlightedGroups()
        .Select(group => group.Directory)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void OnLibrarySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Headings are rows of the list but not songs: they're never part of the selection (a Shift-click
        // or ⌘A across them takes in just the songs).
        if (LibraryView.SelectedItems is { } selected && e.AddedItems.OfType<FolderHeading>().Any())
        {
            foreach (var heading in e.AddedItems.OfType<FolderHeading>().ToList())
            {
                selected.Remove(heading);
            }

            return;
        }

        // Picking songs ends the folder highlight, so Delete never acts on something out of sight.
        if (LibraryView.SelectedItems?.Count > 0 && _highlightedFolders.Count > 0)
        {
            HighlightFolders(Array.Empty<string>());
        }
    }

    private void OnRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        // Rows are reused as the list scrolls, so this is set (or cleared) every time.
        e.Container.Classes.Set("heading", e.Index >= 0 && e.Index < _rows.Count && _rows[e.Index] is FolderHeading);
    }

    // Folder headings remember whether they were left open or closed, and open or close their songs.
    private void OnHeadingChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FolderHeading heading || e.PropertyName != nameof(FolderHeading.IsExpanded))
        {
            return;
        }

        if (!IsSearching)
        {
            if (heading.IsExpanded)
            {
                _collapsedFolders.Remove(heading.Name);
            }
            else
            {
                _collapsedFolders.Add(heading.Name);
            }

            UpdateFoldAllButton();
        }

        var at = _rows.IndexOf(heading);
        if (at < 0)
        {
            return;
        }

        if (heading.IsExpanded)
        {
            _rows.InsertRange(at + 1, heading.OpenRows());
        }
        else
        {
            // Songs going out of sight leave the selection, as in WPF's closed groups.
            if (LibraryView.SelectedItems is { } selected)
            {
                foreach (var item in heading.Items.Where(selected.Contains).ToList())
                {
                    selected.Remove(item);
                }
            }

            _rows.RemoveRange(at + 1, heading.OpenRows().Count());
        }
    }

    /// <summary>Closes every folder heading, or opens them all when every one is already closed.</summary>
    private void OnFoldAllFolders(object? sender, RoutedEventArgs e)
    {
        var names = _groups.Select(heading => heading.Name).ToList();
        if (AnyFolderOpen())
        {
            _collapsedFolders.UnionWith(names);
        }
        else
        {
            _collapsedFolders.ExceptWith(names);
        }

        // Rebuilt in one go rather than heading by heading, which is slow with many folders.
        RefreshView();
    }

    private bool AnyFolderOpen() => _groups.Any(heading => !_collapsedFolders.Contains(heading.Name));

    /// <summary>Shows the fold-all button only over folder headings, ready to close them all or, when they're all closed, open them.</summary>
    private void UpdateFoldAllButton()
    {
        FoldAllButton.IsVisible = ShowingPlaylists || _settings.GroupLibraryByFolder;

        // A search opens every heading, so no match is hidden; there's nothing to fold until it's cleared.
        FoldAllButton.IsEnabled = !IsSearching;
        var collapse = AnyFolderOpen();
        if (this.TryFindResource(collapse ? "CollapseAllIcon" : "ExpandAllIcon", ActualThemeVariant, out var icon)
            && icon is Avalonia.Media.Geometry geometry)
        {
            FoldAllIcon.Data = geometry;
        }

        ToolTip.SetTip(FoldAllButton, collapse ? "Close every folder" : "Open every folder");
    }

    // ---- Keyboard ----
    // The list's arrow keys step over the headings, which aren't songs, as WPF's grouped list does.

    private void OnLibraryNavigationKey(object? sender, KeyEventArgs e)
    {
        if (_groups.Count == 0 || e.Key is not (Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown))
        {
            return;
        }

        var songs = _rows.Select((row, index) => (row, index)).Where(pair => pair.row is LibraryItem).ToList();
        if (songs.Count == 0)
        {
            e.Handled = true;
            return;
        }

        var current = LibraryView.SelectedItem is LibraryItem selected ? _rows.IndexOf(LibraryFocusedRow() ?? selected) : -1;
        var position = songs.FindIndex(pair => pair.index == current);
        var page = Math.Max(1, (int)(LibraryView.Bounds.Height / 48) - 1);
        var next = e.Key switch
        {
            Key.Home => 0,
            Key.End => songs.Count - 1,
            Key.Up => position < 0 ? songs.Count - 1 : Math.Max(0, position - 1),
            Key.Down => position < 0 ? 0 : Math.Min(songs.Count - 1, position + 1),
            Key.PageUp => position < 0 ? 0 : Math.Max(0, position - page),
            _ => position < 0 ? 0 : Math.Min(songs.Count - 1, position + page),
        };

        var target = (LibraryItem)songs[next].row;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && LibraryView.SelectedItems is { } extended)
        {
            // Shift extends the selection from the first chosen song to the new one.
            var anchor = _selectionAnchor is not null && _rows.Contains(_selectionAnchor) ? _rows.IndexOf(_selectionAnchor) : songs[Math.Max(0, position)].index;
            var to = songs[next].index;
            extended.Clear();
            foreach (var row in _rows.Skip(Math.Min(anchor, to)).Take(Math.Abs(to - anchor) + 1).OfType<LibraryItem>())
            {
                extended.Add(row);
            }

            _focusedRow = target;
            LibraryView.ScrollIntoView(target);
            (LibraryView.ContainerFromItem(target) as ListBoxItem)?.Focus();
        }
        else
        {
            ListRows.Focus(LibraryView, target);
            _selectionAnchor = target;
            _focusedRow = target;
        }

        e.Handled = true;
    }

    private LibraryItem? _selectionAnchor;
    private LibraryItem? _focusedRow;

    private LibraryItem? LibraryFocusedRow() =>
        _focusedRow is not null && LibraryView.SelectedItems?.Contains(_focusedRow) == true ? _focusedRow : null;

    /// <summary>The library as it's listed on screen: folder by folder when grouped.</summary>
    public List<LibraryItem> LibraryInViewOrder()
    {
        if (!_hasView)
        {
            return new List<LibraryItem>();
        }

        if (_groups.Count > 0)
        {
            // A store's heading lists only the songs right in its folder; its subheadings list the rest.
            return _groups.SelectMany(group => group.Subheadings.Count == 0 ? group.Items : group.Items.Where(item => !item.IsInSection))
                .Where(item => !item.IsPlaceholder)
                .ToList();
        }

        return _viewItems.Where(item => !item.IsPlaceholder).ToList();
    }

    public List<LibraryItem> SelectedLibraryItems()
    {
        var selected = new HashSet<LibraryItem>(LibraryView.SelectedItems?.OfType<LibraryItem>() ?? Enumerable.Empty<LibraryItem>());
        return LibraryInViewOrder().Where(selected.Contains).ToList();
    }

    private void OnLibraryDoubleClick(object? sender, TappedEventArgs e)
    {
        // Only rows count; double-clicking a folder heading or the scroll bar does nothing.
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: LibraryItem { IsPlaceholder: false } item })
        {
            Player?.PlayFromLibrary(item);
        }
    }

    private void OnLibraryKeyDown(object? sender, KeyEventArgs e)
    {
        // The Mac's delete key sends Back; the forward-delete key sends Delete.
        if (e.Key is Key.Delete or Key.Back && _highlightedFolders.Count > 0 && !ShowingPlaylists)
        {
            RemoveLibraryFolders(HighlightedDirectories());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _highlightedFolders.Count > 0)
        {
            HighlightFolders(Array.Empty<string>());
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.PlayFromLibrary(item);
            e.Handled = true;
        }
    }

    private void OnLibraryPlayNow(object? sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.PlayFromLibrary(item);
        }
    }

    private async void OnLibraryAddToQueue(object? sender, RoutedEventArgs e)
    {
        var items = SelectedLibraryItems();
        var tracks = await Task.Run(() => items.Select(item => item.ToTrack()).ToList());
        Player?.AddToQueue(tracks);
    }

    private async void OnLibraryPlayFolder(object? sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is not { } chosen)
        {
            return;
        }

        var folder = LibraryInViewOrder().Where(item => item.Folder == chosen.Folder).ToList();
        var tracks = await Task.Run(() => folder.Select(item => item.ToTrack()).ToList());
        Player?.ReplaceQueue(tracks, folder.IndexOf(chosen));
    }

    private async void OnPlayLibrary(object? sender, RoutedEventArgs e)
    {
        var items = LibraryInViewOrder();
        var tracks = await Task.Run(() => items.Select(item => item.ToTrack()).ToList());
        Player?.ReplaceQueue(tracks, 0);
    }

    private void OnLibraryEditInfo(object? sender, RoutedEventArgs e) =>
        Player?.EditInfo(SelectedLibraryItems().Select(item => item.Path));

    private void OnLibraryOpenInEditor(object? sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.OpenInEditor(this, item.Path);
        }
    }

    private void OnLibraryConvert(object? sender, RoutedEventArgs e) =>
        Player?.ConvertFiles(this, SelectedLibraryItems().Select(item => item.Path).ToList());

    private void OnLibraryShowInExplorer(object? sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.ShowInExplorer(item.Path);
        }
    }
}
