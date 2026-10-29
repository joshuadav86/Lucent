using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Streaming;
using Lucent.Mac.Streaming;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

// Sharing the controlled app's songs (copying their links), and adding them to the app's playlists: from the player's
// right-click menu for the song playing, from the queue window's for the songs in the app's list, and from the library's
// for the songs of a playlist opened there (Spotify's, or Music's on a Mac), whichever app Lucent is controlling.
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
        Toast.Show(this, text);
        await Task.Delay(TimeSpan.FromSeconds(4));
        if (notice == _songNotices)
        {
            ShowRemote();
        }
    }

    /// <summary>True when the controlled app can add its songs to playlists (Spotify, and Music on a Mac).</summary>
    public bool CanAddToRemotePlaylists => _remote?.Player is IRemotePlaylists;

    /// <summary>
    /// True when song menus have Add to playlist: for an app that can add songs, and for one that can't, whose menu then
    /// says why (<see cref="IRemotePlayer.PlaylistsLimit"/>).
    /// </summary>
    public bool ShowsAddToPlaylist => CanAddToRemotePlaylists || _remote?.Player.PlaylistsLimit is not null;

    /// <summary>
    /// The playlists the controlled app's songs can be added to: Spotify's Liked Songs and the playlists the account made
    /// or collaborates on (read at most every ten minutes), or Music's own on a Mac. Empty when the app can't add songs.
    /// </summary>
    /// <exception cref="RemoteProblem">When the app won't list them, worded for the user.</exception>
    /// <param name="app">The app the song is from, when it may not be the one being controlled (see <see cref="Controls"/>).</param>
    public async Task<IReadOnlyList<RemotePlaylist>> RemotePlaylistsToAddToAsync(RemoteApp? app = null)
    {
        if (Controls(app) ? _remote!.Player is SpotifyRemote : app == RemoteApp.Spotify)
        {
            return await SpotifyPlaylistsToAddToAsync();
        }

        IReadOnlyList<RemotePlaylist> found = Array.Empty<RemotePlaylist>();
        await WithPlaylistsAsync(app, async playlists =>
            found = (await playlists.PlaylistsAsync(CancellationToken.None)).Where(playlist => playlist.CanAdd).ToList());
        return found;
    }

    /// <summary>
    /// Whether a song from <paramref name="app"/> goes through the app being controlled: a song of a playlist opened in the
    /// library can be Spotify's while Lucent controls another app, or none. Null stands for the controlled app's own songs.
    /// </summary>
    private bool Controls(RemoteApp? app) => _remote is not null && (app is null || _settings.RemoteApp == app);

    /// <summary>
    /// Asks <paramref name="app"/> about its playlists: through the app being controlled when it's that one, or else a
    /// connection made just for this (Spotify's from the saved sign-in). False when it can't be reached (signed out).
    /// </summary>
    private async Task<bool> WithPlaylistsAsync(RemoteApp? app, Func<IRemotePlaylists, Task> ask)
    {
        if (Controls(app))
        {
            if (_remote!.Player is not IRemotePlaylists controlled)
            {
                return false;
            }

            await ask(controlled);
            return true;
        }

        switch (app)
        {
            case RemoteApp.Spotify:
                return await WithSpotifyAsync<object>(async spotify =>
                {
                    await ask(spotify);
                    return spotify;
                }) is not null;
            case RemoteApp.AppleMusic:
                // Each request to Music is its own osascript run, so another connection beside the controlling one is fine.
                using (var music = new AppleMusicRemote())
                {
                    await ask(music);
                }

                return true;
            default:
                return false;
        }
    }

    /// <summary>The app's name for what's said about its songs, as <see cref="Controls"/> picks the app.</summary>
    private string NameOf(RemoteApp? app) => Controls(app)
        ? _remote!.Player.Name
        : app == RemoteApp.AppleMusic ? "Apple Music" : app?.ToString() ?? "the app";

    /// <summary>
    /// Fills <paramref name="menu"/> (an Add to playlist item) with the playlists <paramref name="track"/> can be added to,
    /// once they're read. <paramref name="notice"/> says how adding went. <paramref name="app"/> is the app the song is
    /// from, when it may not be the one being controlled (a playlist opened in the library).
    /// </summary>
    public async void FillAddToPlaylistMenu(MenuItem menu, RemoteTrack track, Action<string> notice, RemoteApp? app = null)
    {
        menu.Items.Clear();
        if (Controls(app) ? !CanAddToRemotePlaylists : app is null)
        {
            menu.Items.Add(new MenuItem { Header = _remote?.Player.PlaylistsLimit ?? "This app can't add songs to playlists.", IsEnabled = false });
            return;
        }

        menu.Items.Add(new MenuItem { Header = "Loading your playlists…", IsEnabled = false });
        IReadOnlyList<RemotePlaylist> playlists;
        try
        {
            // A Spotify sign-in from before Lucent added songs can't, so the menu offers to sign in again instead of
            // listing playlists that would only refuse.
            var canAdd = true;
            await WithPlaylistsAsync(app, async playlists =>
                canAdd = playlists is not SpotifyRemote spotify || await spotify.CanAddSongsAsync(CancellationToken.None));
            if (!canAdd)
            {
                menu.Items.Clear();
                var signIn = new MenuItem { Header = "Sign in to Spotify again to add songs…" };
                signIn.Click += (_, _) => SignInToSpotifyAgain();
                menu.Items.Add(signIn);
                return;
            }

            playlists = await RemotePlaylistsToAddToAsync(app);
        }
        catch (Exception ex)
        {
            menu.Items.Clear();
            menu.Items.Add(new MenuItem
            {
                Header = ex is RemoteProblem ? ex.Message : $"Lucent couldn't reach {NameOf(app)} to list your playlists.",
                IsEnabled = false,
            });
            return;
        }

        menu.Items.Clear();
        for (var i = 0; i < playlists.Count; i++)
        {
            var playlist = playlists[i];
            var item = new MenuItem { Header = playlist.Name };
            item.Click += (_, _) => AddToRemotePlaylist(playlist, track, notice, app);
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

    private async void AddToRemotePlaylist(RemotePlaylist playlist, RemoteTrack track, Action<string> notice, RemoteApp? app)
    {
        var name = NameOf(app);
        try
        {
            var reached = await WithPlaylistsAsync(app, async playlists =>
            {
                // Like Spotify, a song already in the playlist isn't added twice without asking. When the app can't say, it's added.
                var already = false;
                try
                {
                    already = await playlists.ContainsAsync(playlist, track, CancellationToken.None);
                }
                catch (Exception)
                {
                    // Couldn't check; adding still works.
                }

                if (already && (playlist.IsLiked || !await AskAddAgainAsync(playlist, track)))
                {
                    notice($"{track.Title} is already in {playlist.Name}.");
                    return;
                }

                await playlists.AddToPlaylistAsync(playlist, track, CancellationToken.None);
                notice($"Added {track.Title} to {playlist.Name}.");
            });
            if (!reached)
            {
                notice($"Sign in to {name} from the Add music menu to add {track.Title}.");
            }
        }
        catch (Exception ex)
        {
            notice(ex is RemoteProblem ? ex.Message : $"Lucent couldn't reach {name} to add {track.Title}.");
        }
    }

    /// <summary>
    /// Signs out of Spotify and straight back in, for the permissions a sign-in from an older Lucent doesn't have, then
    /// carries on controlling Spotify.
    /// </summary>
    private void SignInToSpotifyAgain()
    {
        if (_settings.RemoteApp == RemoteApp.Spotify)
        {
            StopControllingRemote(pause: false);
        }

        SpotifyAccount.Clear();
        SpotifyAccountChanged();
        ControlRemote(RemoteApp.Spotify, signIn: true);
        UpdateSourcesPanel();
    }

    /// <summary>Asks whether to add a song that's already in the playlist again, as Spotify does. No unless the user says yes.</summary>
    private async Task<bool> AskAddAgainAsync(RemotePlaylist playlist, RemoteTrack track) =>
        await MessageWindow.ShowAsync(this, $"{track.Title} is already in {playlist.Name}. Add it again?", "Already added",
            MessageButtons.YesNo, MessageResult.No) == MessageResult.Yes;

    /// <summary>
    /// Copies <paramref name="track"/>'s web link (see <see cref="SongLinks"/>), with its title and artist first when
    /// <paramref name="withTitle"/>, to paste into a message. <paramref name="notice"/> says how it went. <paramref name="app"/>
    /// is the app the song is from, when it may not be the one being controlled (a playlist opened in the library).
    /// </summary>
    public async void CopySongLink(RemoteTrack track, bool withTitle, Action<string> notice, RemoteApp? app = null)
    {
        if (!Controls(app) && app is null)
        {
            return;
        }

        string? link;
        try
        {
            link = Controls(app) ? await SongLinks.FindAsync(StreamingHttp, _remote!.Player, track, CancellationToken.None)
                : app == RemoteApp.Spotify ? SpotifyRemote.LinkOf(track)
                : app == RemoteApp.AppleMusic ? await MusicLinkAsync(track)
                : null;
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

    // A Music song's link (on Apple Music, found by its title and artist), when Music isn't the app being controlled.
    private async Task<string?> MusicLinkAsync(RemoteTrack track)
    {
        using var music = new AppleMusicRemote();
        return await SongLinks.FindAsync(StreamingHttp, music, track, CancellationToken.None);
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
        NowPlayingAddItem.IsVisible = track is not null && ShowsAddToPlaylist;
        if (_remote is not null && track is null)
        {
            e.Cancel = true;
            return;
        }

        if (track is not null && ShowsAddToPlaylist)
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
