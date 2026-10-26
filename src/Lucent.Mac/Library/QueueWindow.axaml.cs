using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lucent.Mac.Windows;
using Lucent.Playlist;
using Lucent.Streaming;

namespace Lucent.Mac;

/// <summary>The queue: what's playing and what plays next. The player window does the work.</summary>
public partial class QueueWindow : ToolWindow
{
    public QueueWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        PlacementName = "Queue";
        LucentFullScreen.AddTo(QueueMenu, this);

        // Runs before the list's menu opens, to fill in the items that depend on what's selected.
        PlaylistView.AddHandler(ContextRequestedEvent, OnQueueMenuOpening, RoutingStrategies.Tunnel);
        RemoteListView.AddHandler(ContextRequestedEvent, OnRemoteMenuOpening, RoutingStrategies.Tunnel);
    }

    /// <summary>Music dropped on the queue goes on the end of it, as on Winamp's playlist.</summary>
    protected override void OnMusicDropped(IReadOnlyList<string> paths) => Player?.AddDroppedPaths(paths);

    /// <summary>The selected rows, in queue order.</summary>
    public List<PlaylistEntry> SelectedEntries()
    {
        var selected = new HashSet<PlaylistEntry>(PlaylistView.SelectedItems?.OfType<PlaylistEntry>() ?? Enumerable.Empty<PlaylistEntry>());
        return PlaylistView.Items.OfType<PlaylistEntry>().Where(selected.Contains).ToList();
    }

    private void OnPlaylistsMenu(object? sender, RoutedEventArgs e)
    {
        if (Player is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = PlaylistsButton, Placement = PlacementMode.Bottom };
        Player.FillPlaylistsMenu(menu, this);
        menu.Open(PlaylistsButton);
    }

    // Favourite and Add to playlist depend on what's selected, so they're made fresh each time.
    private readonly List<Control> _songMenuItems = new();

    private void OnQueueMenuOpening(object? sender, ContextRequestedEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            QueueMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var entries = SelectedEntries();
        if (Player is null || entries.Count == 0)
        {
            // Nothing to act on: no menu.
            e.Handled = true;
            return;
        }

        var tracks = entries.Select(entry => entry.Track).ToList();
        ShowEditorItem(tracks[0].Path);
        _songMenuItems.Add(Player.FavoriteMenuItem(tracks.Select(track => track.Path).ToList()));
        _songMenuItems.Add(Player.AddToPlaylistMenu(this, () => Task.FromResult(tracks)));
        _songMenuItems.Add(new Separator());
        var at = QueueMenu.Items.IndexOf(SongMenuItemsStart) + 1;
        foreach (var item in _songMenuItems)
        {
            QueueMenu.Items.Insert(at++, item);
        }
    }

    private void OnAddFiles(object? sender, RoutedEventArgs e) => Player?.AddFilesFromDialog(this);

    private void OnAddFolder(object? sender, RoutedEventArgs e) => Player?.AddFolderFromDialog(this);

    private void OnClearQueue(object? sender, RoutedEventArgs e) => Player?.ClearQueue();

    private void OnPlaylistDoubleClick(object? sender, TappedEventArgs e)
    {
        // Only rows count; double-clicking the empty space or the scroll bar does nothing.
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: PlaylistEntry entry })
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnPlaySelected(object? sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e) => Player?.RemoveFromQueue(SelectedEntries());

    private void OnEditInfo(object? sender, RoutedEventArgs e) =>
        Player?.EditInfo(SelectedEntries().Select(entry => entry.Track.Path));

    /// <summary>"Edit video…" when the first song picked is a video, as the Video window's menu says.</summary>
    private void ShowEditorItem(string path)
    {
        var (header, canOpen) = Lucent.Editor.VideoEdit.EditorMenuItem(path);
        OpenInEditorItem.Header = header;
        OpenInEditorItem.IsEnabled = canOpen;
    }

    private void OnOpenInEditor(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntries().FirstOrDefault() is { } entry)
        {
            Player?.OpenInEditor(this, entry.Track.Path);
        }
    }

    private void OnConvert(object? sender, RoutedEventArgs e) =>
        Player?.ConvertFiles(this, SelectedEntries().Select(entry => entry.Track.Path).ToList());

    private void OnShowInExplorer(object? sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.ShowInExplorer(entry.Track.Path);
        }
    }

    private void OnPlaylistKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            // The Mac's delete key sends Back; the forward-delete key sends Delete.
            Player?.RemoveFromQueue(SelectedEntries());
            e.Handled = true;
        }
    }

    // ---- Another app's playlist ----

    private RemoteList? _remoteList;
    private List<RemoteListEntry> _remoteEntries = new();
    private RemoteTrack? _remotePlaying;
    private string? _remotePlayingTitle;

    /// <summary>True while another app's playlist shows in place of the queue.</summary>
    public bool ShowingRemoteList => _remoteList is not null;

    /// <summary>
    /// Shows <paramref name="list"/>, the songs <paramref name="appName"/> is playing through, in place of the queue,
    /// marking <paramref name="playing"/> with the songs either side of it in view; or puts the queue back when
    /// <paramref name="list"/> is null.
    /// </summary>
    public void ShowRemoteList(string appName, RemoteList? list, RemoteTrack? playing)
    {
        var listChanged = !ReferenceEquals(list, _remoteList);
        // The whole of a long playlist arriving after its start, or the same playlist read again.
        var sameList = list is not null && _remoteList is not null && list.Id == _remoteList.Id && list.Name == _remoteList.Name;
        var wasMarked = _remoteEntries.Any(entry => entry.IsCurrent);
        if (listChanged)
        {
            _remoteList = list;
            FillRemoteEntries();
            var showing = list is not null;
            PlaylistView.IsVisible = !showing;
            RemoteListView.IsVisible = showing;
            AddFilesButton.IsVisible = AddFolderButton.IsVisible = ClearQueueButton.IsVisible = !showing;
            if (showing)
            {
                EmptyPlaylistHint.IsVisible = false;
            }

            ShowRemoteNote(list?.Note);
            ShowSortButton();
            RemotePlayItem.Header = $"Play in {appName}";
            QueueTitle.Text = list is null ? "QUEUE" : appName.ToUpperInvariant();
            if (list is not null)
            {
                QueueCount.Text = list.CountText;
            }
        }

        // The song by its title, as its ID changes a few seconds into it (see RemoteControl.ReadListIfChanged).
        var title = playing?.Title.Trim().ToLowerInvariant();
        var songChanged = title != _remotePlayingTitle;
        _remotePlaying = playing;
        _remotePlayingTitle = title;
        if (listChanged || songChanged)
        {
            // Not when the rest of a long playlist arrives, as the list may have been scrolled meanwhile, unless the
            // song playing is only in the rest.
            MarkRemotePlaying(scroll: songChanged || !sameList || !wasMarked);
        }
    }

    // How the list is shown: as the app lists it, unless the user picked another order and the list can be sorted.
    private RemoteListOrder ShownOrder =>
        _remoteList is { FollowsSong: false } list && Player?.RemoteListOrder is { } order && RemoteListSorting.OrdersFor(list).Contains(order)
            ? order
            : RemoteListOrder.Playlist;

    private void FillRemoteEntries()
    {
        var chosen = (RemoteListView.SelectedItem as RemoteListEntry)?.Track.Id;
        var tracks = _remoteList is null ? Array.Empty<RemoteTrack>() : RemoteListSorting.Sort(_remoteList.Tracks, ShownOrder);
        _remoteEntries = tracks.Select(track => new RemoteListEntry(track)).ToList();
        RemoteListView.ItemsSource = _remoteEntries;
        // The song chosen stays chosen when the list is read again (what's up next is, with every song).
        RemoteListView.SelectedItem = _remoteEntries.FirstOrDefault(entry => entry.Track.Id == chosen);
    }

    private void MarkRemotePlaying(bool scroll)
    {
        RemoteListEntry? current = null;
        foreach (var entry in _remoteEntries)
        {
            entry.IsCurrent = current is null && entry.Track.IsSameSong(_remotePlaying);
            current ??= entry.IsCurrent ? entry : null;
        }

        if (scroll && current is not null)
        {
            ShowAround(current);
        }
    }

    // Puts the song in the middle of the list, with the songs before and after it either side.
    private void ShowAround(RemoteListEntry entry)
    {
        RemoteListView.ScrollIntoView(entry);
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var index = _remoteEntries.IndexOf(entry);
            if (index < 0 || RemoteListView.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is not { } scroller)
            {
                return;
            }

            // The rows are all the same height.
            var row = scroller.Extent.Height / _remoteEntries.Count;
            var top = Math.Clamp(index * row - (scroller.Viewport.Height - row) / 2, 0, Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
            scroller.Offset = new Avalonia.Vector(scroller.Offset.X, top);
        }, Avalonia.Threading.DispatcherPriority.Loaded);
    }

    // The Sort button shows for a playlist, album or Liked Songs, not for what's up next, which is in the order it plays.
    private void ShowSortButton()
    {
        SortButton.IsVisible = _remoteList is { FollowsSong: false, Tracks.Count: > 1 };
        SortButtonText.Text = RemoteListSorting.Name(ShownOrder);
    }

    private void OnSortMenu(object? sender, RoutedEventArgs e)
    {
        if (_remoteList is not { } list)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.Bottom };
        foreach (var order in RemoteListSorting.OrdersFor(list))
        {
            var item = new MenuItem
            {
                Header = RemoteListSorting.Name(order),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = order == ShownOrder,
            };
            item.Click += (_, _) => SortRemoteList(order);
            menu.Items.Add(item);
        }

        menu.Open(SortButton);
    }

    private void SortRemoteList(RemoteListOrder order)
    {
        if (Player is { } player)
        {
            player.RemoteListOrder = order;
        }

        FillRemoteEntries();
        ShowSortButton();
        MarkRemotePlaying(scroll: true);
    }

    private void PlayChosenRemoteSong()
    {
        if (RemoteListView.SelectedItem is RemoteListEntry entry)
        {
            Player?.PlayFromRemoteList(entry.Track);
        }
    }

    private void OnRemoteListDoubleClick(object? sender, TappedEventArgs e)
    {
        // Only rows count, as in the queue.
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: RemoteListEntry entry })
        {
            Player?.PlayFromRemoteList(entry.Track);
        }
    }

    private void OnRemoteListPlay(object? sender, RoutedEventArgs e) => PlayChosenRemoteSong();

    private void OnRemoteListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PlayChosenRemoteSong();
            e.Handled = true;
        }
    }

    // ---- Adding the app's songs to its playlists, and sharing them ----

    private void OnRemoteMenuOpening(object? sender, ContextRequestedEventArgs e)
    {
        // The row right-clicked is the one the menu is for.
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: RemoteListEntry row })
        {
            RemoteListView.SelectedItem = row;
        }

        var track = (RemoteListView.SelectedItem as RemoteListEntry)?.Track;
        var canAdd = track is not null && Player?.ShowsAddToPlaylist == true;
        RemoteSongSeparator.IsVisible = RemoteShareItem.IsVisible = track is not null;
        RemoteAddItem.IsVisible = canAdd;
        if (canAdd && track is not null)
        {
            Player?.FillAddToPlaylistMenu(RemoteAddItem, track, ShowRemoteNotice);
        }
    }

    private void OnCopySongLink(object? sender, RoutedEventArgs e) => CopySong(withTitle: false);

    private void OnCopySongAndLink(object? sender, RoutedEventArgs e) => CopySong(withTitle: true);

    private void CopySong(bool withTitle)
    {
        if ((RemoteListView.SelectedItem as RemoteListEntry)?.Track is { } track)
        {
            Player?.CopySongLink(track, withTitle, ShowRemoteNotice);
        }
    }

    private int _notice;

    /// <summary>Shows <paramref name="text"/> under the list for a few seconds, then the list's own note again.</summary>
    private async void ShowRemoteNotice(string text)
    {
        var notice = ++_notice;
        ShowRemoteNote(text);
        Toast.Show(RemoteListView, text);
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (notice == _notice)
        {
            ShowRemoteNote(_remoteList?.Note);
        }
    }

    private void ShowRemoteNote(string? text)
    {
        RemoteListNote.Text = text ?? string.Empty;
        RemoteListNote.IsVisible = text is not null;
    }
}
