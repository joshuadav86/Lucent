using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Library;
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
        SortLabel.Text = LibrarySorts.Label(settings.LibrarySort);
        UpdateGroupFoldersButton();
        UpdateLibraryStatus();
    }

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

        if (LibraryFilter() is { } filter)
        {
            view.Filter = filter;
        }

        _libraryView = view;
        LibraryView.ItemsSource = view;
        UpdateLibraryStatus();
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

        return item => item is LibraryItem song
            && (favorites is null || favorites.Contains(song.Path))
            && SearchText.Matches(song.SearchKey, words);
    }

    private void UpdateLibraryStatus()
    {
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
            var noun = favoritesOnly ? (searched == 1 ? "favourite" : "favourites") : (searched == 1 ? "track" : "tracks");
            countText = $"{shown:N0} of {searched:N0} {noun}{updating}";
        }
        else
        {
            countText = favoritesOnly
                ? $"{shown:N0} {(shown == 1 ? "favourite" : "favourites")}{updating}"
                : $"{count:N0} {(count == 1 ? "track" : "tracks")}{updating}";
        }

        LibraryCount.Text = !linked
            ? "No folder linked"
            : _library.IsScanning && count == 0
                ? "Looking for music…"
                : countText;

        var noMatches = searching && shown == 0;
        EmptyLibraryHint.Visibility = shown == 0 && !_library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryTitle.Text = noMatches ? "No songs match your search"
            : favoritesOnly ? "No favourites here yet"
            : linked ? "No music found"
            : "Link a music folder";
        EmptyLibraryText.Text = noMatches
            ? favoritesOnly
                ? "Only your favourites are searched while FAVOURITES is on. Try other words, or press Esc to clear the search."
                : "Search looks in titles, artists, albums and file names. Try other words, or press Esc to clear the search."
            : favoritesOnly
                ? "Click the heart in the player, or right-click songs here or in the queue, to make them favourites."
                : linked
                    ? "None of your library folders have music Lucid can play yet. New files appear here on their own."
                    : "Every song inside it shows up here, newest first, and new files appear on their own.";
        LinkFolderButton.Visibility = favoritesOnly || noMatches ? Visibility.Collapsed : Visibility.Visible;
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
                    SearchText.FocusRow(LibraryView, first);
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

        foreach (var folder in dialog.FolderNames)
        {
            if (!_settings.LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                _settings.LibraryFolders.Add(folder);
            }
        }

        _settings.Save();
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

    private void UpdateGroupFoldersButton()
    {
        GroupFoldersButton.SetResourceReference(ForegroundProperty, _settings.GroupLibraryByFolder ? "AccentBrush" : "FaintTextBrush");
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

    // Favourite and Add to playlist depend on what's selected, so they're made fresh each time.
    private readonly List<object> _songMenuItems = new();

    private void OnLibraryMenuOpening(object sender, ContextMenuEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            LibraryMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var items = SelectedLibraryItems();
        if (Player is null || items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        _songMenuItems.Add(Player.FavoriteMenuItem(items.Select(item => item.Path).ToList()));
        _songMenuItems.Add(Player.AddToPlaylistMenu(this, () => Task.Run(() => items.Select(item => item.ToTrack()).ToList())));
        _songMenuItems.Add(new Separator());
        var at = LibraryMenu.Items.IndexOf(SongMenuItemsStart) + 1;
        foreach (var item in _songMenuItems)
        {
            LibraryMenu.Items.Insert(at++, item);
        }
    }

    // Folder headings remember whether they were left open or closed.
    private void OnFolderGroupLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander { DataContext: CollectionViewGroup group } expander)
        {
            // While searching every heading is open, so no match is hidden in a closed one.
            expander.IsExpanded = IsSearching || !_collapsedFolders.Contains(group.Name?.ToString() ?? string.Empty);
        }
    }

    private void OnFolderGroupExpanded(object sender, RoutedEventArgs e)
    {
        if (!IsSearching && sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Remove(group.Name?.ToString() ?? string.Empty);
        }
    }

    private void OnFolderGroupCollapsed(object sender, RoutedEventArgs e)
    {
        if (!IsSearching && sender is Expander { DataContext: CollectionViewGroup group, IsLoaded: true })
        {
            _collapsedFolders.Add(group.Name?.ToString() ?? string.Empty);
        }
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
            return groups.Cast<CollectionViewGroup>().SelectMany(group => group.Items.Cast<LibraryItem>()).ToList();
        }

        return _libraryView.Cast<LibraryItem>().ToList();
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
            && ItemsControl.ContainerFromElement(LibraryView, source) is ListBoxItem { DataContext: LibraryItem item })
        {
            Player?.PlayFromLibrary(item);
        }
    }

    private void OnLibraryKeyDown(object sender, KeyEventArgs e)
    {
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
