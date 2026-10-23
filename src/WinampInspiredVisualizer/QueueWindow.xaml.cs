using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Streaming;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>The queue: what's playing and what plays next. The player window does the work.</summary>
public partial class QueueWindow : ToolWindow
{
    public QueueWindow()
    {
        InitializeComponent();
        PlacementName = "Queue";
        LucentFullScreen.AddTo(QueueMenu);
    }

    /// <summary>Music dropped on the queue goes on the end of it, as on Winamp's playlist.</summary>
    protected override void OnMusicDropped(IReadOnlyList<string> paths) => Player?.AddDroppedPaths(paths);

    /// <summary>The selected rows, in queue order.</summary>
    public List<PlaylistEntry> SelectedEntries()
    {
        var selected = new HashSet<PlaylistEntry>(PlaylistView.SelectedItems.Cast<PlaylistEntry>());
        return PlaylistView.Items.Cast<PlaylistEntry>().Where(selected.Contains).ToList();
    }

    private void OnPlaylistsMenu(object sender, RoutedEventArgs e)
    {
        if (Player is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = PlaylistsButton, Placement = PlacementMode.Bottom };
        Player.FillPlaylistsMenu(menu, this);
        menu.IsOpen = true;
    }

    // Favourite and Add to playlist depend on what's selected, so they're made fresh each time.
    private readonly List<object> _songMenuItems = new();

    private void OnQueueMenuOpening(object sender, ContextMenuEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            QueueMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var entries = SelectedEntries();
        if (Player is null || entries.Count == 0)
        {
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

    private void OnAddFiles(object sender, RoutedEventArgs e) => Player?.AddFilesFromDialog(this);

    private void OnAddFolder(object sender, RoutedEventArgs e) => Player?.AddFolderFromDialog(this);

    private void OnClearQueue(object sender, RoutedEventArgs e) => Player?.ClearQueue();

    private void OnPlaylistDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnPlaySelected(object sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnRemoveSelected(object sender, RoutedEventArgs e) => Player?.RemoveFromQueue(SelectedEntries());

    private void OnEditInfo(object sender, RoutedEventArgs e) =>
        Player?.EditInfo(SelectedEntries().Select(entry => entry.Track.Path));

    /// <summary>"Edit video…" when the first song picked is a video, as the Video window's menu says.</summary>
    private void ShowEditorItem(string path)
    {
        var (header, canOpen) = Editor.VideoEdit.EditorMenuItem(path);
        OpenInEditorItem.Header = header;
        OpenInEditorItem.IsEnabled = canOpen;
    }

    private void OnOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (SelectedEntries().FirstOrDefault() is { } entry)
        {
            Player?.OpenInEditor(this, entry.Track.Path);
        }
    }

    private void OnConvert(object sender, RoutedEventArgs e) =>
        Player?.ConvertFiles(this, SelectedEntries().Select(entry => entry.Track.Path).ToList());

    private void OnShowInExplorer(object sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.ShowInExplorer(entry.Track.Path);
        }
    }

    private void OnPlaylistKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            Player?.RemoveFromQueue(SelectedEntries());
            e.Handled = true;
        }
    }

    // ---- Another app's playlist ----

    private RemoteList? _remoteList;
    private List<RemoteListEntry> _remoteEntries = new();
    private string? _remotePlaying;
    private string _remoteApp = string.Empty;

    /// <summary>True while another app's playlist shows in place of the queue.</summary>
    public bool ShowingRemoteList => _remoteList is not null;

    /// <summary>
    /// Shows <paramref name="list"/>, the songs <paramref name="appName"/> is playing through, in place of the queue,
    /// marking the one with <paramref name="playingId"/>; or puts the queue back when <paramref name="list"/> is null.
    /// </summary>
    public void ShowRemoteList(string appName, RemoteList? list, string? playingId)
    {
        if (!ReferenceEquals(list, _remoteList))
        {
            _remoteList = list;
            _remoteEntries = list?.Tracks.Select(track => new RemoteListEntry(track)).ToList() ?? new List<RemoteListEntry>();
            RemoteListView.ItemsSource = _remoteEntries;
            _remotePlaying = null;
            var showing = list is not null;
            PlaylistView.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
            RemoteListView.Visibility = showing ? Visibility.Visible : Visibility.Collapsed;
            foreach (var button in new UIElement[] { AddFilesButton, AddFolderButton, ClearQueueButton })
            {
                button.Visibility = showing ? Visibility.Collapsed : Visibility.Visible;
            }

            if (showing)
            {
                EmptyPlaylistHint.Visibility = Visibility.Collapsed;
            }

            ShowRemoteNote(list?.Note);
            _remoteApp = appName;
            RemotePlayItem.Header = $"Play in {appName}";
            QueueTitle.Text = list is null ? "QUEUE" : appName.ToUpperInvariant();
            if (list is not null)
            {
                QueueCount.Text = list.Tracks.Count == 1 ? $"{list.Name}  ·  1 song" : $"{list.Name}  ·  {list.Tracks.Count} songs";
            }
        }

        if (playingId == _remotePlaying)
        {
            return;
        }

        _remotePlaying = playingId;
        RemoteListEntry? playing = null;
        foreach (var entry in _remoteEntries)
        {
            entry.IsCurrent = entry.Track.Id == playingId;
            playing ??= entry.IsCurrent ? entry : null;
        }

        if (playing is not null)
        {
            RemoteListView.ScrollIntoView(playing);
        }
    }

    private void PlayChosenRemoteSong()
    {
        if (RemoteListView.SelectedItem is RemoteListEntry entry)
        {
            Player?.PlayFromRemoteList(entry.Track);
        }
    }

    private void OnRemoteListDoubleClick(object sender, MouseButtonEventArgs e) => PlayChosenRemoteSong();

    private void OnRemoteListPlay(object sender, RoutedEventArgs e) => PlayChosenRemoteSong();

    private void OnRemoteListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PlayChosenRemoteSong();
            e.Handled = true;
        }
    }

    // ---- Adding a Spotify song to a playlist, and sharing it ----

    private void OnRemoteMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // Spotify's songs can be added to the account's playlists and shared as open.spotify.com links.
        var track = (RemoteListView.SelectedItem as RemoteListEntry)?.Track;
        var spotify = _remoteApp == "Spotify" && track is not null && SpotifyRemote.WebLink(track) is not null;
        RemoteSongSeparator.Visibility = RemoteAddItem.Visibility = RemoteShareItem.Visibility =
            spotify ? Visibility.Visible : Visibility.Collapsed;
        if (spotify && track is not null)
        {
            FillAddToPlaylistMenu(track);
        }
    }

    private async void FillAddToPlaylistMenu(RemoteTrack track)
    {
        RemoteAddItem.Items.Clear();
        RemoteAddItem.Items.Add(new MenuItem { Header = "Loading your playlists…", IsEnabled = false });
        if (Player is null)
        {
            return;
        }

        IReadOnlyList<RemotePlaylist> playlists;
        try
        {
            playlists = await Player.SpotifyPlaylistsToAddToAsync();
        }
        catch (Exception ex)
        {
            RemoteAddItem.Items.Clear();
            RemoteAddItem.Items.Add(new MenuItem
            {
                Header = ex is RemoteProblem ? ex.Message : "Lucent couldn't reach Spotify to list your playlists.",
                IsEnabled = false,
            });
            return;
        }

        RemoteAddItem.Items.Clear();
        foreach (var playlist in playlists)
        {
            var item = new MenuItem { Header = playlist.Name };
            item.Click += (_, _) => AddToPlaylist(playlist, track);
            RemoteAddItem.Items.Add(item);
            // Liked Songs comes first, apart from the playlists.
            if (playlist.Uri.EndsWith(":collection", StringComparison.Ordinal) && playlists.Count > 1)
            {
                RemoteAddItem.Items.Add(new Separator());
            }
        }

        if (playlists.Count == 0)
        {
            RemoteAddItem.Items.Add(new MenuItem { Header = "No playlists you made or collaborate on", IsEnabled = false });
        }
    }

    private async void AddToPlaylist(RemotePlaylist playlist, RemoteTrack track)
    {
        if (Player is null)
        {
            return;
        }

        try
        {
            await Player.AddToSpotifyPlaylistAsync(playlist, track);
            ShowRemoteNotice($"Added {track.Title} to {playlist.Name}.");
        }
        catch (Exception ex)
        {
            ShowRemoteNotice(ex is RemoteProblem ? ex.Message : $"Lucent couldn't reach Spotify to add {track.Title}.");
        }
    }

    private void OnCopySongLink(object sender, RoutedEventArgs e) => CopySong(withTitle: false);

    private void OnCopySongAndLink(object sender, RoutedEventArgs e) => CopySong(withTitle: true);

    private void CopySong(bool withTitle)
    {
        if ((RemoteListView.SelectedItem as RemoteListEntry)?.Track is not { } track || SpotifyRemote.WebLink(track) is not { } link)
        {
            return;
        }

        try
        {
            Clipboard.SetText(withTitle ? $"{track.Title} by {track.Artist}{Environment.NewLine}{link}" : link);
            ShowRemoteNotice($"Copied the link to {track.Title}.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another app has the clipboard open.
            ShowRemoteNotice("Lucent couldn't copy the link. Try again in a moment.");
        }
    }

    private int _notice;

    /// <summary>Shows <paramref name="text"/> under the list for a few seconds, then the list's own note again.</summary>
    private async void ShowRemoteNotice(string text)
    {
        var notice = ++_notice;
        ShowRemoteNote(text);
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (notice == _notice)
        {
            ShowRemoteNote(_remoteList?.Note);
        }
    }

    private void ShowRemoteNote(string? text)
    {
        RemoteListNote.Text = text ?? string.Empty;
        RemoteListNote.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }
}
