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
using SavedPlaylists = WinampInspiredVisualizer.Playlist.SavedPlaylists;
using Track = WinampInspiredVisualizer.Playlist.Track;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>The music library: every song in the linked folders, sorted and grouped by folder.</summary>
public partial class LibraryWindow : ToolWindow
{
    private MusicLibrary _library = null!;
    private AppSettings _settings = null!;
    private HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
    private ListCollectionView? _libraryView;
    private string[] _searchWords = Array.Empty<string>();

    // Searching waits for a short pause in the typing, so a big library doesn't re-sort on every key.
    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };

    public LibraryWindow()
    {
        InitializeComponent();
        PlacementName = "Library";
        _searchTimer.Tick += (_, _) => ApplySearch();
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
                Dispatcher.InvokeAsync(() =>
                {
                    _libraryView?.Refresh();
                    UpdateLibraryStatus();
                }, DispatcherPriority.Background);
            }
            else if (_settings.LibrarySort is LibrarySort.Title or LibrarySort.Artist)
            {
                // Titles and artists only become known once tags are read, so re-sort by them now.
                _libraryView?.Refresh();
            }
        };
        library.ScanningChanged += (_, _) => UpdateLibraryStatus();
        TrackStats.Shared.Changed += (_, _) => Dispatcher.BeginInvoke(new Action(StatsChanged));
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
            _libraryView?.Refresh();
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
                IsChecked = current.Count == 1 && current[0] == stars,
            };
            item.Click += (_, _) => Task.Run(() => TrackStats.Shared.SetRating(paths, value));
            menu.Items.Add(item);
        }

        return menu;
    }

    private void RebuildLibraryView()
    {
        // Playlists keep their own order, under a heading each.
        var view = new ListCollectionView((ShowingPlaylists ? _playlistItems : _library.Items).ToList());
        if (!ShowingPlaylists)
        {
            view.CustomSort = new LibraryItemComparer(_settings.LibrarySort);
        }

        if (ShowingPlaylists || _settings.GroupLibraryByFolder)
        {
            // A store's folders go under its one heading; see FolderGroupStyleSelector.
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryItem.Section)));
            view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LibraryItem.Folder)));
        }

        if (LibraryFilter() is { } filter)
        {
            view.Filter = filter;
        }

        _libraryView = view;
        LibraryView.ItemsSource = view;
        UpdateLibraryStatus();
        UpdateFoldAllButton();
    }

    /// <summary>Which songs the list shows: the favourites when FAVOURITES is on, and the ones matching the search. Null shows them all.</summary>
    private Predicate<object>? LibraryFilter()
    {
        var favorites = _settings.LibraryFavoritesOnly
            ? new HashSet<string>(_settings.FavoriteTracks, StringComparer.OrdinalIgnoreCase)
            : null;
        var words = _searchWords;
        if (favorites is null && words.Length == 0)
        {
            return null;
        }

        return item => item is LibraryItem { IsPlaceholder: false } song
            && (favorites is null || favorites.Contains(song.Path))
            && SearchText.Matches(song.SearchKey, words);
    }

    private void UpdateLibraryStatus()
    {
        var picked = ShowingPlaylists || _library.Folders.Count == 0 ? 0 : _highlightedFolders.Count;
        LibraryCount.SetResourceReference(ForegroundProperty, picked > 0 ? "AccentBrush" : "FaintTextBrush");
        if (ShowingPlaylists)
        {
            UpdatePlaylistsStatus();
            return;
        }

        var count = _library.Items.Count;
        var linked = _library.Folders.Count > 0;
        var favoritesOnly = _settings.LibraryFavoritesOnly && linked && count > 0;
        var searching = IsSearching && linked && count > 0;
        var shown = favoritesOnly || searching ? _libraryView?.Count ?? 0 : count;
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

        // While folder headings are picked, say how many, and what Delete does with them.
        LibraryCount.Text = !linked
            ? "No folder linked"
            : picked > 0
                ? $"{picked} {(picked == 1 ? "folder" : "folders")} selected  ·  Delete to remove"
                : _library.IsScanning && count == 0
                    ? "Looking for music…"
                    : countText;

        var noMatches = searching && shown == 0;
        EmptyLibraryHint.Visibility = shown == 0 && !_library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
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
        LinkFolderButton.Visibility = favoritesOnly || noMatches ? Visibility.Collapsed : Visibility.Visible;
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
                _libraryView?.Refresh();
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
        var shown = filtered ? _libraryView?.Count ?? 0 : total;
        LibraryCount.Text = lists == 0
            ? "No playlists"
            : filtered
                ? $"{shown:N0} of {total:N0} {(total == 1 ? "sound" : "sounds")}"
                : $"{lists:N0} {(lists == 1 ? "playlist" : "playlists")}  ·  {total:N0} {(total == 1 ? "sound" : "sounds")}";

        // Empty playlists show their headings, so the hint is only for no playlists at all, or no matches.
        EmptyLibraryHint.Visibility = lists == 0 || (filtered && shown == 0) ? Visibility.Visible : Visibility.Collapsed;

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
        LinkFolderButton.Visibility = Visibility.Collapsed;
    }

    private void OnTogglePlaylists(object sender, RoutedEventArgs e) => SetShowingPlaylists(!ShowingPlaylists);

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
        var menu = new ContextMenu { PlacementTarget = LibraryView, Placement = PlacementMode.MousePoint };
        var names = HighlightedGroups().Select(group => group.Name?.ToString() ?? string.Empty).ToList();
        var songs = HighlightedGroups().SelectMany(group => group.Items.OfType<LibraryItem>())
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
        menu.IsOpen = true;
    }

    // ---- Search ----

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        ClearSearchButton.Visibility = SearchBox.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _searchTimer.Stop();
        if (SearchBox.Text.Length == 0)
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
        if (_libraryView is not null)
        {
            // Setting the filter re-sorts and regroups the list.
            _libraryView.Filter = LibraryFilter();
            UpdateLibraryStatus();
            UpdateFoldAllButton();
        }
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when SearchBox.Text.Length > 0:
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

    private void OnClearSearch(object sender, RoutedEventArgs e)
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        // In the folders view, Ctrl+A picks every folder heading, ready for Delete, unless songs are chosen.
        if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control && !SearchBox.IsKeyboardFocusWithin
            && _settings.GroupLibraryByFolder && !ShowingPlaylists
            && (_highlightedFolders.Count > 0 || LibraryView.SelectedItems.Count == 0))
        {
            HighlightFolders(GroupNames());
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnLinkLibraryFolder(object sender, RoutedEventArgs e) => LinkLibraryFolder();

    private void LinkLibraryFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose a music folder for your library", Multiselect = true };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        AddLibraryFolders(dialog.FolderNames);
    }

    /// <summary>
    /// Puts folders into the library. A new one with folders inside it gets a heading per folder, or one
    /// heading for all of it, as the user picks (asking unless they said not to).
    /// </summary>
    private void AddLibraryFolders(IEnumerable<string> folders)
    {
        var added = folders
            .Where(folder => LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder))
            .ToList();
        var nested = added.Where(LibraryFolderList.HasSubfolders).ToList();
        var choice = _settings.LibrarySubfolders;
        if (nested.Count > 0 && choice == LibrarySubfolders.Ask)
        {
            (choice, var remember) = SubfolderWindow.Ask(this, nested);
            if (remember)
            {
                _settings.LibrarySubfolders = choice;
            }
        }

        foreach (var folder in added)
        {
            LibraryFolderList.SetTogether(_settings.LibraryFoldersTogether, folder,
                choice == LibrarySubfolders.Together && nested.Contains(folder));
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
        var folders = paths.Where(Directory.Exists).ToList();
        foreach (var path in paths)
        {
            if (folders.Contains(path))
            {
                continue;
            }

            if (File.Exists(path) && AudioReaderFactory.IsSupported(path)
                && !_settings.LibraryFiles.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _settings.LibraryFiles.Add(path);
                changed = true;
            }
        }

        if (folders.Count > 0)
        {
            // After the drop has finished, so a question about subfolders doesn't hold up File Explorer.
            Dispatcher.BeginInvoke(new Action(() => AddLibraryFolders(folders)));
        }
        else if (changed)
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

        LibraryFolderList.ForgetUnlinked(_settings.LibraryFoldersTogether, _settings.LibraryFolders);
        HighlightFolders(Array.Empty<string>());
        ApplyLibraryFolders();
    }

    private void ApplyLibraryFolders()
    {
        _settings.Save();
        _library.SetFolders(_settings.LibraryFolders, _settings.HiddenLibraryFolders, _settings.LibraryFiles,
            _settings.LibraryFoldersTogether);
        UpdateLibraryStatus();
    }

    private void OnLibraryFolders(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = LibraryFoldersButton, Placement = PlacementMode.Bottom };
        foreach (var folder in _settings.LibraryFolders.ToList())
        {
            var remove = new MenuItem { Header = $"Remove {LibraryFolderList.Name(folder)}", ToolTip = folder };
            remove.Click += (_, _) => RemoveLibraryFolders(new[] { folder });
            menu.Items.Add(remove);
        }

        // Folders taken out from inside a linked one can be brought back from here.
        foreach (var folder in _settings.HiddenLibraryFolders.ToList())
        {
            var show = new MenuItem { Header = $"Bring back {LibraryFolderList.Name(folder)}", ToolTip = folder };
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
        if (_settings.LibrarySubfolders != LibrarySubfolders.Ask)
        {
            var ask = new MenuItem
            {
                Header = "Ask about folders inside new folders",
                ToolTip = _settings.LibrarySubfolders == LibrarySubfolders.Together
                    ? "New folders currently list everything inside them under one heading"
                    : "New folders currently give each folder inside them its own heading",
            };
            ask.Click += (_, _) =>
            {
                _settings.LibrarySubfolders = LibrarySubfolders.Ask;
                _settings.Save();
            };
            menu.Items.Add(ask);
        }

        var stores = new MenuItem
        {
            Header = "Bandcamp and Beatport…",
            ToolTip = "Add the music you buy on Bandcamp and Beatport to the library",
        };
        stores.Click += (_, _) => Player?.ShowStores();
        menu.Items.Add(stores);
        var import = new MenuItem
        {
            Header = "Import from another player…",
            ToolTip = "Bring playlists, play counts and ratings over from Apple Music, MusicBee, foobar2000, Plex or YouTube Music",
        };
        import.Click += (_, _) => Player?.ShowImport();
        menu.Items.Add(import);
        if (RemoveAllFoldersMenuItem() is { } removeAll)
        {
            menu.Items.Add(removeAll);
        }

        menu.IsOpen = true;
    }

    /// <summary>"Remove all folders…", or null when the library has none.</summary>
    private MenuItem? RemoveAllFoldersMenuItem()
    {
        if (_settings.LibraryFolders.Count == 0)
        {
            return null;
        }

        var removeAll = new MenuItem
        {
            Header = "Remove all folders…",
            ToolTip = "Empties the library. The files stay on your computer.",
        };
        removeAll.Click += (_, _) => RemoveAllLibraryFolders();
        return removeAll;
    }

    /// <summary>The folders Delete acts on: the highlighted headings', else those of the chosen songs.</summary>
    private List<string> FoldersToDelete() => _highlightedFolders.Count > 0
        ? HighlightedDirectories()
        : SelectedLibraryItems().Where(item => !item.IsPlaceholder).Select(item => item.Directory)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Takes folders out of the library after asking, as Delete does. Nothing on disk is touched.</summary>
    private void AskToRemoveLibraryFolders(IReadOnlyList<string> folders)
    {
        var answer = MessageBox.Show(this, RemoveFoldersQuestion(folders),
            "Library", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes)
        {
            RemoveLibraryFolders(folders);
        }
    }

    private static string RemoveFoldersQuestion(IReadOnlyList<string> folders) =>
        (folders.Count == 1
            ? $"Remove “{LibraryFolderList.Name(folders[0])}” from your library?"
            : $"Remove these {folders.Count} folders from your library?") +
        "\n\nYour music files stay where they are. You can add the folders back at any time.";

    /// <summary>Takes every folder out of the library, after asking. Nothing on disk is touched.</summary>
    private void RemoveAllLibraryFolders()
    {
        var count = _settings.LibraryFolders.Count;
        var answer = MessageBox.Show(this,
            $"Remove {(count == 1 ? "the 1 folder" : $"all {count} folders")} from your library?\n\n" +
            "Your music files stay where they are. You can add the folders back at any time.",
            "Library", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _settings.LibraryFolders.Clear();
        _settings.HiddenLibraryFolders.Clear();
        _settings.LibraryFiles.Clear();
        _settings.LibraryFoldersTogether.Clear();
        HighlightFolders(Array.Empty<string>());
        ApplyLibraryFolders();
    }

    /// <summary>
    /// For the library folder a heading is in: list everything inside it under the one heading, or go back
    /// to a heading per folder. Null when it has no folders inside it.
    /// </summary>
    private MenuItem? ListTogetherMenuItem(string directory)
    {
        if (LibraryFolderList.LinkedFolderOf(_settings.LibraryFolders, directory) is not { } root)
        {
            return null;
        }

        var together = LibraryFolderList.IsTogether(_settings.LibraryFoldersTogether, root);
        if (!together && !LibraryFolderList.HasSubfolders(root))
        {
            return null;
        }

        var name = LibraryFolderList.Name(root);
        var item = new MenuItem
        {
            Header = together ? $"Give each folder in “{name}” its own heading" : $"List everything in “{name}” as one folder",
        };
        item.Click += (_, _) =>
        {
            LibraryFolderList.SetTogether(_settings.LibraryFoldersTogether, root, !together);
            ApplyLibraryFolders();
        };
        return item;
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
            ToolTip = "Takes the folder, and the folders inside it, out of the library. The files stay on your computer.",
        };
        remove.Click += (_, _) => RemoveLibraryFolders(folders);
        return remove;
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

    private void OnToggleFavoritesOnly(object sender, RoutedEventArgs e)
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
    private readonly List<object> _songMenuItems = new();

    private void OnLibraryMenuOpening(object sender, ContextMenuEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            LibraryMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var source = e.OriginalSource as DependencyObject;
        if (Ancestor<ListBoxItem>(source) is not { DataContext: LibraryItem { IsPlaceholder: false } })
        {
            // A folder heading, or the empty space below the songs, gets the folder menu instead.
            e.Handled = true;
            if (Ancestor<GroupItem>(source)?.DataContext is CollectionViewGroup { Name: { } name }
                && !_highlightedFolders.Contains(name.ToString() ?? string.Empty))
            {
                // Right-clicking a heading outside the highlight highlights just that one.
                HighlightFolders(new[] { name.ToString() ?? string.Empty });
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
            foreach (var item in new object[] { new Separator(), remove })
            {
                LibraryMenu.Items.Add(item);
                _songMenuItems.Add(item);
            }

            return;
        }

        // The folders the chosen songs are in, at the bottom.
        var folders = items.Select(item => item.Directory).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var folderItems = new object[] { new Separator(), AddFolderMenuItem(), RemoveFoldersMenuItem(folders) };
        foreach (var item in folderItems)
        {
            LibraryMenu.Items.Add(item);
            _songMenuItems.Add(item);
        }
    }

    /// <summary>The menu for the highlighted folder headings: play them, or add and remove library folders.</summary>
    private void OpenFolderMenu()
    {
        var menu = new ContextMenu { PlacementTarget = LibraryView, Placement = PlacementMode.MousePoint };
        var groups = HighlightedGroups();
        var songs = groups.SelectMany(SongsIn).Distinct().ToList();
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
            remove.InputGestureText = "Del";
            menu.Items.Add(remove);
        }

        if (RemoveAllFoldersMenuItem() is { } removeAll)
        {
            menu.Items.Add(removeAll);
        }

        if (groups.Count == 1 && songs.Count > 0)
        {
            var directory = DirectoryOf(groups[0]) ?? songs[0].Directory;
            menu.Items.Add(new Separator());
            if (ListTogetherMenuItem(directory) is { } together)
            {
                menu.Items.Add(together);
            }

            var show = new MenuItem { Header = "Show in File Explorer" };
            show.Click += (_, _) => Player?.ShowInExplorer(directory);
            menu.Items.Add(show);
        }

        menu.IsOpen = true;
    }

    // ---- Highlighting folder headings ----
    // Click a heading to pick it (it also opens or closes), Ctrl+click to add one, Shift+click for a run,
    // or Ctrl+A for all of them; then right-click them or press Delete to take them out of the library.

    private readonly HashSet<string> _highlightedFolders = new(StringComparer.OrdinalIgnoreCase);
    private string? _folderAnchor;

    private void OnFolderHeadingMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Only the heading itself; clicks on the songs inside the folder go to the list as usual.
        if (sender is not Expander { DataContext: CollectionViewGroup group } expander
            || Ancestor<ToggleButton>(e.OriginalSource as DependencyObject) is not { } heading
            || Ancestor<Expander>(heading) != expander)
        {
            return;
        }

        var name = group.Name?.ToString() ?? string.Empty;
        var modifiers = Keyboard.Modifiers;
        if (modifiers.HasFlag(ModifierKeys.Shift) && _folderAnchor is not null)
        {
            var names = GroupNames();
            var from = names.FindIndex(n => string.Equals(n, _folderAnchor, StringComparison.OrdinalIgnoreCase));
            var to = names.IndexOf(name);
            if (from >= 0 && to >= 0)
            {
                var run = names.GetRange(Math.Min(from, to), Math.Abs(to - from) + 1);
                HighlightFolders(modifiers.HasFlag(ModifierKeys.Control) ? _highlightedFolders.Concat(run) : run, keepAnchor: true);
            }
        }
        else if (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Shift))
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
            // A plain click opens or closes the folder. Under FOLDERS it also picks that folder on its own, so it's
            // clear what Delete acts on; playlists can't be removed that way, so there it just ends any highlight.
            HighlightFolders(ShowingPlaylists ? Array.Empty<string>() : new[] { name });
            if (ShowingPlaylists)
            {
                return;
            }

            LibraryView.UnselectAll();
            LibraryView.Focus();
            return;
        }

        // Keep the folder open or closed, and take the keys so Delete works next.
        e.Handled = true;
        LibraryView.UnselectAll();
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

        foreach (var expander in Descendants<Expander>(LibraryView))
        {
            ShowHighlight(expander);
        }

        UpdateLibraryStatus();
    }

    private void ShowHighlight(Expander expander) =>
        expander.Tag = expander.DataContext is CollectionViewGroup group
            && _highlightedFolders.Contains(group.Name?.ToString() ?? string.Empty);

    private List<string> GroupNames() => HeadingGroups().Select(group => group.Name?.ToString() ?? string.Empty).ToList();

    /// <summary>The groups that show a heading, in order: each section, and the subheadings inside a store's.</summary>
    private List<CollectionViewGroup> HeadingGroups()
    {
        var headings = new List<CollectionViewGroup>();
        foreach (var section in _libraryView?.Groups?.Cast<CollectionViewGroup>() ?? Enumerable.Empty<CollectionViewGroup>())
        {
            headings.Add(section);
            headings.AddRange(section.Items.OfType<CollectionViewGroup>()
                .Where(folder => folder.Items.OfType<LibraryItem>().FirstOrDefault() is { IsInSection: true }));
        }

        return headings;
    }

    private List<CollectionViewGroup> HighlightedGroups() =>
        HeadingGroups().Where(group => _highlightedFolders.Contains(group.Name?.ToString() ?? string.Empty)).ToList();

    /// <summary>The songs under a heading, through any subheadings.</summary>
    private static IEnumerable<LibraryItem> SongsIn(CollectionViewGroup group) =>
        group.Items.SelectMany(item => item is CollectionViewGroup inner ? SongsIn(inner) : item is LibraryItem song ? new[] { song } : Enumerable.Empty<LibraryItem>());

    /// <summary>The folder on disk behind a heading: a store's own folder for its heading, or the songs' folder.</summary>
    private static string? DirectoryOf(CollectionViewGroup group) =>
        SongsIn(group).FirstOrDefault() is { } first
            ? string.Equals(group.Name?.ToString(), first.Section, StringComparison.Ordinal) ? first.SectionDirectory : first.Directory
            : null;

    /// <summary>The folders on disk behind the highlighted headings.</summary>
    private List<string> HighlightedDirectories() => HighlightedGroups()
        .Select(DirectoryOf)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void OnLibrarySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking songs ends the folder highlight, so Delete never acts on something out of sight.
        if (LibraryView.SelectedItems.Count > 0 && _highlightedFolders.Count > 0)
        {
            HighlightFolders(Array.Empty<string>());
        }
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in Descendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static T? Ancestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null and not T)
        {
            element = element is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(element)
                : LogicalTreeHelper.GetParent(element);
        }

        return element as T;
    }

    // Folder headings remember whether they were left open or closed.
    private void OnFolderGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } expander)
        {
            ShowHighlight(expander);

            // While searching every heading is open, so no match is hidden in a closed one.
            expander.IsExpanded = IsSearching || !_collapsedFolders.Contains(group.Name?.ToString() ?? string.Empty);
        }
    }

    private void OnFolderGroupExpanded(object sender, RoutedEventArgs e)
    {
        // A subheading's Expanded bubbles up through its store's heading; only the one that opened counts.
        if (!IsSearching && e.OriginalSource == sender && sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Remove(group.Name?.ToString() ?? string.Empty);
            UpdateFoldAllButton();
        }
    }

    private void OnFolderGroupCollapsed(object sender, RoutedEventArgs e)
    {
        if (!IsSearching && e.OriginalSource == sender && sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Add(group.Name?.ToString() ?? string.Empty);
            UpdateFoldAllButton();
        }
    }

    /// <summary>Closes every folder heading, or opens them all when every one is already closed.</summary>
    private void OnFoldAllFolders(object sender, RoutedEventArgs e)
    {
        var names = GroupNames();
        var open = !AnyFolderOpen(names);
        if (open)
        {
            _collapsedFolders.ExceptWith(names);
        }
        else
        {
            _collapsedFolders.UnionWith(names);
        }

        // Headings scrolled out of sight pick up the state when they're next shown.
        foreach (var expander in Descendants<Expander>(LibraryView))
        {
            expander.IsExpanded = open;
        }

        UpdateFoldAllButton();
    }

    private bool AnyFolderOpen(IEnumerable<string> names) => names.Any(name => !_collapsedFolders.Contains(name));

    /// <summary>Shows the fold-all button only over folder headings, ready to close them all or, when they're all closed, open them.</summary>
    private void UpdateFoldAllButton()
    {
        FoldAllButton.Visibility = ShowingPlaylists || _settings.GroupLibraryByFolder ? Visibility.Visible : Visibility.Collapsed;

        // A search opens every heading, so no match is hidden; there's nothing to fold until it's cleared.
        FoldAllButton.IsEnabled = !IsSearching;
        var collapse = AnyFolderOpen(GroupNames());
        FoldAllIcon.Data = (Geometry)FindResource(collapse ? "CollapseAllIcon" : "ExpandAllIcon");
        FoldAllButton.ToolTip = collapse ? "Close every folder" : "Open every folder";
    }

    /// <summary>The library as it's listed on screen: folder by folder when grouped.</summary>
    public List<LibraryItem> LibraryInViewOrder()
    {
        if (_libraryView is null)
        {
            return new List<LibraryItem>();
        }

        if (_libraryView.Groups is { Count: > 0 } groups)
        {
            return groups.Cast<CollectionViewGroup>().SelectMany(SongsIn)
                .Where(item => !item.IsPlaceholder)
                .ToList();
        }

        return _libraryView.Cast<LibraryItem>().Where(item => !item.IsPlaceholder).ToList();
    }

    public List<LibraryItem> SelectedLibraryItems()
    {
        var selected = new HashSet<LibraryItem>(LibraryView.SelectedItems.Cast<LibraryItem>());
        return LibraryInViewOrder().Where(selected.Contains).ToList();
    }

    private void OnLibraryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only rows count; double-clicking a folder heading or the scroll bar does nothing.
        if (e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(LibraryView, source) is ListBoxItem { DataContext: LibraryItem { IsPlaceholder: false } item })
        {
            Player?.PlayFromLibrary(item);
        }
    }

    private void OnLibraryKeyDown(object sender, KeyEventArgs e)
    {
        // Delete takes the highlighted headings' folders out, or the folders the chosen songs are in, after asking.
        if (e.Key == Key.Delete && !ShowingPlaylists && FoldersToDelete() is { Count: > 0 } folders)
        {
            e.Handled = true;
            AskToRemoveLibraryFolders(folders);
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

    private void OnLibraryPlayNow(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.PlayFromLibrary(item);
        }
    }

    private async void OnLibraryAddToQueue(object sender, RoutedEventArgs e)
    {
        var items = SelectedLibraryItems();
        var tracks = await Task.Run(() => items.Select(item => item.ToTrack()).ToList());
        Player?.AddToQueue(tracks);
    }

    private async void OnLibraryPlayFolder(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is not { } chosen)
        {
            return;
        }

        var folder = LibraryInViewOrder().Where(item => item.Folder == chosen.Folder).ToList();
        var tracks = await Task.Run(() => folder.Select(item => item.ToTrack()).ToList());
        Player?.ReplaceQueue(tracks, folder.IndexOf(chosen));
    }

    private async void OnPlayLibrary(object sender, RoutedEventArgs e)
    {
        var items = LibraryInViewOrder();
        var tracks = await Task.Run(() => items.Select(item => item.ToTrack()).ToList());
        Player?.ReplaceQueue(tracks, 0);
    }

    private void OnLibraryEditInfo(object sender, RoutedEventArgs e) =>
        Player?.EditInfo(SelectedLibraryItems().Select(item => item.Path));

    private void OnLibraryOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.OpenInEditor(this, item.Path);
        }
    }

    private void OnLibraryConvert(object sender, RoutedEventArgs e) =>
        Player?.ConvertFiles(this, SelectedLibraryItems().Select(item => item.Path).ToList());

    private void OnLibraryShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.ShowInExplorer(item.Path);
        }
    }
}
