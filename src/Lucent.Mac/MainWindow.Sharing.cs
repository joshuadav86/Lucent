using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac;

// Sharing the controlled app's songs (copying their links), and adding them to the app's playlists: from the player's
// right-click menu for the song playing, and from the queue window's for the songs in the app's list.
public partial class MainWindow
{
    private string? _songNotice;
    private DateTime _songNoticeUntil;
    private int _songNotices;

    /// <summary>What was just done with a song (added, copied), shown under the title for a few seconds, or null.</summary>
    private string? SongNotice => DateTime.UtcNow < _songNoticeUntil ? _songNotice : null;

    /// <summary>Shows <paramref name="text"/> under the song's title for a few seconds, in place of where it's playing.</summary>
    private async void ShowSongNotice(string text)
    {
        var notice = ++_songNotices;
        _songNotice = text;
        _songNoticeUntil = DateTime.UtcNow + TimeSpan.FromSeconds(4);
        ShowRemote();
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (notice == _songNotices)
        {
            ShowRemote();
        }
    }

    /// <summary>True when the controlled app can add its songs to playlists (Spotify, and Music on a Mac).</summary>
    public bool CanAddToRemotePlaylists => _remote?.Player is IRemotePlaylists;

    /// <summary>
    /// The playlists the controlled app's songs can be added to: Spotify's Liked Songs and the playlists the account made
    /// or collaborates on (read at most every ten minutes), or Music's own on a Mac. Empty when the app can't add songs.
    /// </summary>
    /// <exception cref="RemoteProblem">When the app won't list them, worded for the user.</exception>
    public async Task<IReadOnlyList<RemotePlaylist>> RemotePlaylistsToAddToAsync() => _remote?.Player switch
    {
        SpotifyRemote => await SpotifyPlaylistsToAddToAsync(),
        IRemotePlaylists playlists => (await playlists.PlaylistsAsync(CancellationToken.None)).Where(playlist => playlist.CanAdd).ToList(),
        _ => Array.Empty<RemotePlaylist>(),
    };

    /// <summary>
    /// Fills <paramref name="menu"/> (an Add to playlist item) with the playlists <paramref name="track"/> can be added to,
    /// once they're read. <paramref name="notice"/> says how adding went.
    /// </summary>
    public async void FillAddToPlaylistMenu(MenuItem menu, RemoteTrack track, Action<string> notice)
    {
        menu.Items.Clear();
        menu.Items.Add(new MenuItem { Header = "Loading your playlists…", IsEnabled = false });
        IReadOnlyList<RemotePlaylist> playlists;
        try
        {
            playlists = await RemotePlaylistsToAddToAsync();
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuItem
            {
                Header = ex is RemoteProblem ? ex.Message : $"Lucent couldn't reach {_remote?.Player.Name ?? "the app"} to list your playlists.",
                IsEnabled = false,
            });
            return;
        }

        menu.Items.Clear();
        for (var i = 0; i < playlists.Count; i++)
        {
            var playlist = playlists[i];
            var item = new MenuItem { Header = playlist.Name };
            item.Click += (_, _) => AddToRemotePlaylist(playlist, track, notice);
            menu.Items.Add(item);
            // Liked Songs (or Favorite Songs) comes first, apart from the playlists.
            if (playlist.IsLiked && i < playlists.Count - 1)
            {
                menu.Items.Add(new Separator());
            }
        }

        if (playlists.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No playlists you can add to", IsEnabled = false });
        }
    }

    private async void AddToRemotePlaylist(RemotePlaylist playlist, RemoteTrack track, Action<string> notice)
    {
        if (_remote?.Player is not IRemotePlaylists app)
        {
            return;
        }

        var name = _remote.Player.Name;

        try
        {
            await app.AddToPlaylistAsync(playlist, track, CancellationToken.None);
            notice($"Added {track.Title} to {playlist.Name}.");
        }
        catch (Exception ex)
        {
            notice(ex is RemoteProblem ? ex.Message : $"Lucent couldn't reach {name} to add {track.Title}.");
        }
    }

    /// <summary>
    /// Copies <paramref name="track"/>'s web link (see <see cref="SongLinks"/>), with its title and artist first when
    /// <paramref name="withTitle"/>, to paste into a message. <paramref name="notice"/> says how it went.
    /// </summary>
    public async void CopySongLink(RemoteTrack track, bool withTitle, Action<string> notice)
    {
        if (_remote is not { } remote)
        {
            return;
        }

        string? link;
        try
        {
            link = await SongLinks.FindAsync(StreamingHttp, remote.Player, track, CancellationToken.None);
        }
        catch (Exception)
        {
            link = null;
        }

        var song = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Title} by {track.Artist}";
        if (link is null && !withTitle)
        {
            notice($"Lucent couldn't find a link to {track.Title}.");
            return;
        }

        try
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(!withTitle ? link! : link is null ? song : $"{song}\n{link}");
                notice(link is null ? $"Copied {track.Title}. Lucent couldn't find a link to it." : $"Copied the link to {track.Title}.");
            }
        }
        catch (Exception)
        {
            notice("Lucent couldn't copy the link. Try again in a moment.");
        }
    }

    // ---- The player's right-click menu, for the song playing ----

    private RemoteTrack? _menuTrack;

    private void OnNowPlayingMenuOpening(object? sender, CancelEventArgs e)
    {
        // Lucent's own song can have its info edited; another app's can be shared, and added to that app's playlists.
        var track = _remote?.State?.Track;
        _menuTrack = track;
        NowPlayingEditItem.IsVisible = _remote is null;
        NowPlayingShareItem.IsVisible = track is not null;
        NowPlayingAddItem.IsVisible = track is not null && CanAddToRemotePlaylists;
        if (_remote is not null && track is null)
        {
            e.Cancel = true;
            return;
        }

        if (track is not null && CanAddToRemotePlaylists)
        {
            FillAddToPlaylistMenu(NowPlayingAddItem, track, ShowSongNotice);
        }
    }

    private void OnNowPlayingCopyLink(object? sender, RoutedEventArgs e)
    {
        if (_menuTrack is { } track)
        {
            CopySongLink(track, withTitle: false, ShowSongNotice);
        }
    }

    private void OnNowPlayingCopySongAndLink(object? sender, RoutedEventArgs e)
    {
        if (_menuTrack is { } track)
        {
            CopySongLink(track, withTitle: true, ShowSongNotice);
        }
    }
}
