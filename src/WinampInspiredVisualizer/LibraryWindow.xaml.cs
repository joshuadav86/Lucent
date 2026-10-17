using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        EmptyLibraryHint.Visibility = shown == 0 && !_library.IsScanning ? Visibility.Visible : Visibility.Collapsed;
        EmptyLibraryTitle.Text = noMatches ? "No songs match your search"
            : favoritesOnly ? "No favorites here yet"
            : linked ? "No music found"
            : "Link a music folder";
        EmptyLibraryText.Text = noMatches
            ? favoritesOnly
                ? "Only your favorites are searched while FAVORITES is on. Try other words, or press Esc to clear the search."
                : "Search looks in titles, artists, albums and file names. Try other words, or press Esc to clear the search."
            : favoritesOnly
                ? "Click the heart in the player, or right-click songs here or in the queue, to make them favorites."
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
            LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
        }

        ApplyLibraryFolders();
    }

    /// <summary>Takes folders out of the library. Only the library forgets them; the files stay on disk.</summary>
    private void RemoveLibraryFolders(IEnumerable<string> folders)
    {
        foreach (var folder in folders)
        {
            LibraryFolderList.Remove(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
        }

        HighlightFolders(Array.Empty<string>());
        ApplyLibraryFolders();
    }

    private void ApplyLibraryFolders()
    {
        _settings.Save();
        _library.SetFolders(_settings.LibraryFolders, _settings.HiddenLibraryFolders);
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
        menu.IsOpen = true;
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
        if (Ancestor<ListBoxItem>(source) is null)
        {
            // A folder heading, or the empty space below the songs, gets the folder menu instead.
            e.Handled = true;
            if (Ancestor<GroupItem>(source)?.DataContext is CollectionViewGroup { Name: { } name }
                && !_highlightedFolders.Contains(name.ToString() ?? string.Empty))
            {
                // Right-clicking a heading outside the highlight highlights just that one.
                HighlightFolders(new[] { name.ToString() ?? string.Empty });
            }

            OpenFolderMenu();
            return;
        }

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
        var songs = groups.SelectMany(group => group.Items.OfType<LibraryItem>()).ToList();
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

        if (groups.Count == 1 && songs.Count > 0)
        {
            var directory = songs[0].Directory;
            menu.Items.Add(new Separator());
            var show = new MenuItem { Header = "Show in File Explorer" };
            show.Click += (_, _) => Player?.ShowInExplorer(directory);
            menu.Items.Add(show);
        }

        menu.IsOpen = true;
    }

    // ---- Highlighting folder headings ----
    // Ctrl+click a heading to highlight it, Shift+click to highlight a run of them, then right-click
    // them or press Delete to take them out of the library. A plain click still opens or closes one.

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
            // A plain click opens or closes the folder, and ends any highlight.
            HighlightFolders(Array.Empty<string>());
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
    }

    private void ShowHighlight(Expander expander) =>
        expander.Tag = expander.DataContext is CollectionViewGroup group
            && _highlightedFolders.Contains(group.Name?.ToString() ?? string.Empty);

    private List<string> GroupNames() =>
        _libraryView?.Groups?.Cast<CollectionViewGroup>().Select(group => group.Name?.ToString() ?? string.Empty).ToList()
        ?? new List<string>();

    private List<CollectionViewGroup> HighlightedGroups() =>
        _libraryView?.Groups?.Cast<CollectionViewGroup>()
            .Where(group => _highlightedFolders.Contains(group.Name?.ToString() ?? string.Empty))
            .ToList()
        ?? new List<CollectionViewGroup>();

    /// <summary>The folders on disk behind the highlighted headings.</summary>
    private List<string> HighlightedDirectories() => HighlightedGroups()
        .Select(group => group.Items.OfType<LibraryItem>().FirstOrDefault()?.Directory)
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
        if (e.Key == Key.Delete && _highlightedFolders.Count > 0)
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
