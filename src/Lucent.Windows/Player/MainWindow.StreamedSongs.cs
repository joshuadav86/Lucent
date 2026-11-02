using System.Windows.Controls;
using Lucent.Playlist;
using Lucent.Settings;
using Lucent.Streaming;

namespace Lucent;

// Spotify songs in Lucent's playlists and queue, beside files. When the queue reaches one, Lucent's own player lets go,
// the song plays in Spotify, and the player shows and controls Spotify for it (see MainWindow.Streaming.cs), with the
// visuals listening to Spotify as usual. When it ends, the queue carries on with what's next, in order or shuffled: a file,
// or another Spotify song. Next, Previous, shuffle and repeat stay the queue's own meanwhile. The Mac does the same.
public partial class MainWindow
{
    // The queue's Spotify song playing now, or null while Lucent plays its own music, or Spotify is controlled from Sources.
    private StreamedSong? _streamed;

    /// <summary>
    /// Plays a Spotify song from the queue in Spotify, starting to control Spotify first (signing in, if Lucent isn't) when
    /// it isn't already.
    /// </summary>
    private void PlayInSpotify(PlaylistEntry entry)
    {
        // Another app being controlled (Apple Music, say) pauses, as it does for a file.
        if (_remote is { Player: not SpotifyRemote })
        {
            StopControllingRemote(pause: true);
        }

        LeaveRadio();
        ForgetUpNext();
        // Lucent's own player lets go of its song, so Play after the queue has ended starts the queue again.
        _player.Close();
        var streamed = new StreamedSong(entry);
        Func<RemoteControl, Task> play = control => control.PlayFromListAsync(streamed.List, streamed.Song);
        ShowEntryPlaying(entry);
        if (_remote is not { Player: SpotifyRemote } remote)
        {
            _playWhenConnected = (RemoteApp.Spotify, play);
            ControlRemote(RemoteApp.Spotify, signIn: true, streamed);
            return;
        }

        _streamed = streamed;
        remote.ReadsList = false;
        // Spotify chosen under Sources hands over to the queue, which is what comes back on the next start.
        if (_settings.RemoteApp != RemoteApp.None)
        {
            _settings.RemoteApp = RemoteApp.None;
            _settings.Save();
            _libraryWindow.RemoteAppChanged();
        }

        RemoteTakes(play);
        ShowRemoteList();
        ShowRemote();
        UpdateShuffleRepeatButtons();
    }

    /// <summary>
    /// With each read of Spotify, and five times a second: once the queue's Spotify song has ended, the queue carries on
    /// (or, at its end, Spotify stops rather than playing on into its own autoplay).
    /// </summary>
    private void FollowStreamedSong()
    {
        if (_streamed is not { } streamed || _remote is not { } remote || !streamed.HasEnded(remote.State))
        {
            return;
        }

        if (_settings.Repeat == RepeatMode.One)
        {
            PlayEntry(streamed.Entry);
            return;
        }

        var next = ChooseNext(wrap: _settings.Repeat == RepeatMode.All);
        if (next is null)
        {
            StopControllingRemote(pause: true);
            return;
        }

        if (next.StartsNewRound)
        {
            _shuffleHistory.Clear();
        }

        PlayEntry(next.Entry);
    }

    /// <summary>
    /// The Lucent playlists a Spotify song can go in, at the top of its Add to playlist menu: each saved playlist, then New
    /// playlist…. Null when the song isn't Spotify's.
    /// </summary>
    private MenuItem? LucentPlaylistsMenu(RemoteTrack song, Action<string> notice, RemoteApp? app)
    {
        if (!(Controls(app) ? _remote!.Player is SpotifyRemote : app == RemoteApp.Spotify))
        {
            return null;
        }

        var menu = new MenuItem { Header = "Lucent playlists" };
        if (StreamedSong.TrackOf(song) is not { } track)
        {
            menu.Items.Add(new MenuItem { Header = "Spotify hasn't said which song this is yet. Try again in a moment.", IsEnabled = false });
            return menu;
        }

        foreach (var name in SavedPlaylists.Names())
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) =>
            {
                if (SavePlaylist(() => SavedPlaylists.Append(name, new[] { track })))
                {
                    notice($"Added {song.Title} to {name}.");
                }
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var create = new MenuItem { Header = "New playlist…" };
        create.Click += (_, _) =>
        {
            if (AskForPlaylistName(this, "New playlist", $"A Lucent playlist, with {song.Title} in it. Files and Spotify songs can go in it together.", "Create") is { } name
                && SavePlaylist(() => SavedPlaylists.Write(name, new[] { track })))
            {
                notice($"Added {song.Title} to {name}.");
            }
        };
        menu.Items.Add(create);
        return menu;
    }
}
