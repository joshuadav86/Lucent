using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
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

    public LibraryWindow()
    {
        InitializeComponent();
        PlacementName = "Library";
    }

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
            // Titles and artists only become known once tags are read, so re-sort by them now.
            if (_settings.LibrarySort is LibrarySort.Title or LibrarySort.Artist)
            {
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

    private void UpdateGroupFoldersButton() =>
        GroupFoldersButton.SetResourceReference(ForegroundProperty, _settings.GroupLibraryByFolder ? "AccentBrush" : "FaintTextBrush");

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

    private void OnLibraryShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            Player?.ShowInExplorer(item.Path);
        }
    }
}
