using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Lucent.Audio;
using Lucent.Settings;
using Lucent.Streaming;

namespace Lucent;

// Other apps: the visuals listening to every app's sound (see Audio/SystemAudio.cs), and the player
// showing and controlling Spotify, Apple Music or YouTube (see RemoteControl) while their sound stays in their app.
public partial class MainWindow
{
    private static readonly HttpClient StreamingHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private SystemAudio? _systemAudio;
    private RemoteControl? _remote;
    private CancellationTokenSource? _signingIn;

    // Finds sharper covers than the apps give (kept between apps, so a song's cover isn't looked up twice).
    private SharperCover? _sharperCover;
    private string? _shownRemoteTrack;
    private byte[]? _shownRemoteArtwork;

    // What the player shows: the other app's song while one is being controlled, otherwise Lucent's own.
    private bool ShownHasTrack => _remote is not null ? _remote.State?.Track is not null : _player.HasTrack;

    // A station tuning in counts as playing, so the button shows it can be stopped.
    private bool ShownIsPlaying => _remote?.IsPlaying ?? (_player.IsPlaying || _tuning is not null);

    private TimeSpan ShownPosition => _remote is not null ? _remote.State?.PositionNow ?? TimeSpan.Zero : _player.Position;

    private TimeSpan ShownDuration => _remote is not null ? _remote.State?.Track?.Duration ?? TimeSpan.Zero : _player.Duration;

    // Shuffle and repeat as the player shows them: the other app's own while one is being controlled.
    private bool ShownShuffle => _remote is not null ? _remote.State?.Shuffle == true : _settings.Shuffle;

    private RepeatMode ShownRepeat => _remote is not null ? _remote.State?.Repeat ?? RepeatMode.Off : _settings.Repeat;

    /// <summary>Picks up where the last run left off: listening to other apps, and the app being controlled.</summary>
    private void StartStreaming()
    {
        if (_settings.RemoteApp != RemoteApp.None)
        {
            ControlRemote(_settings.RemoteApp, signIn: false);
        }

        UpdateListening();
        ShowVolume();
    }

    private void ToggleRemote(RemoteApp app)
    {
        if (_settings.RemoteApp == app)
        {
            StopControllingRemote(pause: false);
            return;
        }

        ControlRemote(app, signIn: true);
    }

    /// <summary>Starts showing and controlling <paramref name="app"/>, signing in first when it needs it and <paramref name="signIn"/> allows.</summary>
    private async void ControlRemote(RemoteApp app, bool signIn)
    {
        // A playlist (or one of its songs) chosen in the library while its app wasn't being controlled plays once it is.
        var pending = _playWhenConnected;
        _playWhenConnected = null;
        IRemotePlayer player;
        try
        {
            switch (app)
            {
                case RemoteApp.Spotify:
                    var refreshToken = SpotifyAccount.Load();
                    if (refreshToken is null)
                    {
                        if (!signIn)
                        {
                            _settings.RemoteApp = RemoteApp.None;
                            _settings.Save();
                            return;
                        }

                        _signingIn = new CancellationTokenSource();
                        try
                        {
                            refreshToken = await SpotifySignIn.SignInAsync(StreamingHttp, OpenInBrowser, _signingIn.Token);
                        }
                        finally
                        {
                            _signingIn = null;
                        }

                        SpotifyAccount.Save(refreshToken);
                        SpotifyAccountChanged();
                        Activate();
                    }

                    player = new SpotifyRemote(StreamingHttp, refreshToken, SpotifyAccount.Save, new SpotifyAppRemote());
                    break;
                case RemoteApp.AppleMusic when AppleMusicRemote.IsSupported:
                    player = new AppleMusicRemote();
                    break;
                case RemoteApp.YouTube when YouTubeRemote.IsSupported:
                    player = new YouTubeRemote();
                    break;
                case RemoteApp.Tidal when TidalRemote.IsSupported:
                    player = new TidalRemote();
                    break;
                case RemoteApp.Pandora when PandoraRemote.IsSupported:
                    player = new PandoraRemote();
                    break;
                default:
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            // Lucent closed while the sign-in page was open.
            return;
        }
        catch (Exception ex)
        {
            var message = ex is RemoteProblem ? ex.Message : $"Lucent couldn't connect to {RemoteName(app)}. {ex.Message}";
            MessageBox.Show(this, message, "Lucent", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StopControllingRemote(pause: false);
        // Two songs at once helps nobody: Lucent's own music pauses while another app's plays.
        if (_player.IsPlaying)
        {
            _player.Pause();
        }

        _settings.RemoteApp = app;
        _settings.Save();
        _remote = new RemoteControl(player, _sharperCover ??= new SharperCover(StreamingHttp));
        _remote.Changed += OnRemoteChanged;
        _libraryWindow.RemoteAppChanged();
        if (pending is { } wanted && wanted.App == app)
        {
            _ = wanted.Play(_remote);
        }

        UpdateListening();
        ShowRemote();
        ShowWhereItPlays();
        UpdateShuffleRepeatButtons();
    }

    /// <summary>Goes back to Lucent's own music, pausing the other app first when <paramref name="pause"/> says to.</summary>
    private void StopControllingRemote(bool pause)
    {
        if (_remote is not { } remote)
        {
            return;
        }

        _remote = null;
        remote.Changed -= OnRemoteChanged;
        ShowWhereItPlays();
        if (pause)
        {
            // It shuts down once the pause has gone, so it isn't cut off on the way.
            _ = remote.PauseAsync().ContinueWith(_ => remote.Dispose(), TaskScheduler.FromCurrentSynchronizationContext());
        }
        else
        {
            remote.Dispose();
        }

        _settings.RemoteApp = RemoteApp.None;
        _settings.Save();
        _libraryWindow.RemoteAppChanged();
        _shownRemoteTrack = null;
        _shownRemoteArtwork = null;
        ShowVolume();
        ShowOpenApp();
        ShowRemoteList();
        UpdateListening();
        ShowNowPlaying(_current?.Track);
        UpdatePlayPauseButton();
        UpdateShuffleRepeatButtons();
        UpdatePosition();
    }

    /// <summary>
    /// Presses one of the other app's buttons instead of the player's own, when an app is being controlled.
    /// Returns false (and does nothing) when the player is playing Lucent's own music.
    /// </summary>
    private bool RemoteTakes(Func<RemoteControl, Task> press)
    {
        if (_remote is not { } remote)
        {
            return false;
        }

        _ = press(remote);
        UpdatePlayPauseButton();
        UpdatePosition();
        return true;
    }

    /// <summary>Moves the song to <paramref name="position"/>: Lucent's own, or the other app's.</summary>
    private void SeekTo(TimeSpan position)
    {
        if (!RemoteTakes(remote => remote.SeekAsync(position)))
        {
            _player.Position = position;
        }
    }

    /// <summary>How long the queue is, in its window's title bar, unless another app's playlist is showing there instead.</summary>
    private void ShowQueueSize()
    {
        if (_queue.ShowingRemoteList)
        {
            return;
        }

        _queue.EmptyPlaylistHint.Visibility = _playlist.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _queue.QueueCount.Text = _playlist.Count switch
        {
            0 => "Empty",
            1 => "1 track",
            var count => $"{count} tracks",
        };
    }

    /// <summary>Shows the playlist the other app is playing through in the queue window, or the queue again.</summary>
    private void ShowRemoteList()
    {
        var showing = _queue.ShowingRemoteList;
        _queue.ShowRemoteList(_remote?.Player.Name ?? string.Empty, _remote?.List, _remote?.State?.Track);
        if (showing && !_queue.ShowingRemoteList)
        {
            ShowQueueSize();
        }
    }

    /// <summary>How the queue window lists the other app's playlist, kept between runs.</summary>
    public RemoteListOrder RemoteListOrder
    {
        get => _settings.RemoteListOrder;
        set
        {
            _settings.RemoteListOrder = value;
            _settings.Save();
        }
    }

    /// <summary>Plays one of the songs from the other app's playlist, in that app.</summary>
    public void PlayFromRemoteList(RemoteTrack track) => RemoteTakes(remote => remote.PlayFromListAsync(track));

    private (RemoteApp App, Func<RemoteControl, Task> Play)? _playWhenConnected;

    /// <summary>
    /// The Spotify account's playlists for the library, or null when Lucent isn't signed in to Spotify. While Spotify is
    /// being controlled its connection asks; otherwise one is made just for this, from the saved sign-in (which Spotify
    /// renews with each use, so two connections at once would sign each other out).
    /// </summary>
    /// <exception cref="RemoteProblem">When Spotify won't list them, worded for the user.</exception>
    public async Task<IReadOnlyList<RemotePlaylist>?> SpotifyPlaylistsAsync()
    {
        var playlists = await WithSpotifyAsync(spotify => spotify.PlaylistsAsync(CancellationToken.None));
        _spotifyPlaylists = playlists;
        _spotifyPlaylistsReadAt = DateTime.UtcNow;
        return playlists;
    }

    // The playlists last read, for the song menus' Add to playlist, as Spotify limits how often all of Lucent's users may ask.
    private IReadOnlyList<RemotePlaylist>? _spotifyPlaylists;
    private DateTime _spotifyPlaylistsReadAt;

    /// <summary>Called when Lucent signs in to or out of Spotify, as the playlists are then another account's, or none.</summary>
    private void SpotifyAccountChanged()
    {
        _spotifyPlaylists = null;
        _libraryWindow.SpotifyChanged();
    }

    /// <summary>
    /// The Spotify playlists a song can be added to (Liked Songs, and the ones the account made or collaborates on), from
    /// the last read when that was under ten minutes ago.
    /// </summary>
    /// <exception cref="RemoteProblem">When Spotify won't list them, worded for the user.</exception>
    public async Task<IReadOnlyList<RemotePlaylist>> SpotifyPlaylistsToAddToAsync()
    {
        var playlists = _spotifyPlaylists is not null && DateTime.UtcNow - _spotifyPlaylistsReadAt < TimeSpan.FromMinutes(10)
            ? _spotifyPlaylists
            : await SpotifyPlaylistsAsync();
        return playlists?.Where(playlist => playlist.CanAdd).ToList() ?? new List<RemotePlaylist>();
    }

    /// <summary>
    /// Asks Spotify through the connection controlling it, or, when Spotify isn't being controlled, one made just for this
    /// from the saved sign-in (which Spotify renews with each use, so two connections at once would sign each other out).
    /// Null when Lucent isn't signed in to Spotify.
    /// </summary>
    private async Task<T?> WithSpotifyAsync<T>(Func<SpotifyRemote, Task<T>> ask)
        where T : class
    {
        if (_remote?.Player is SpotifyRemote controlling)
        {
            return await ask(controlling);
        }

        if (SpotifyAccount.Load() is not { } refreshToken)
        {
            return null;
        }

        using var spotify = new SpotifyRemote(StreamingHttp, refreshToken, SpotifyAccount.Save);
        return await ask(spotify);
    }

    /// <summary>
    /// The apps whose playlists the library lists under PLAYLISTS: Spotify. (On a Mac, Music's too; Windows' Apple Music app
    /// can't be asked for its playlists.)
    /// </summary>
    public IReadOnlyList<RemoteApp> PlaylistApps { get; } = [RemoteApp.Spotify];

    /// <summary>
    /// The app being controlled, by name, and why the library can't list its playlists, when it's one whose playlists
    /// can't show (TIDAL, Pandora and so on); null otherwise.
    /// </summary>
    public (string Name, string Why)? RemotePlaylistsLimit =>
        _remote is { } remote && !PlaylistApps.Contains(_settings.RemoteApp) && remote.Player.PlaylistsLimit is { } why ? (remote.Player.Name, why) : null;

    /// <summary>
    /// An app's playlists for the library, or null when there are none to show (not signed in to Spotify).
    /// </summary>
    /// <exception cref="RemoteProblem">When the app won't list them, worded for the user.</exception>
    public async Task<IReadOnlyList<RemotePlaylist>?> RemotePlaylistsAsync(RemoteApp app) => app switch
    {
        RemoteApp.Spotify => await SpotifyPlaylistsAsync(),
        _ => null,
    };

    /// <summary>Plays one of an app's playlists in the app, starting to control the app first when it isn't.</summary>
    public void PlayRemotePlaylist(RemoteApp app, RemotePlaylist playlist) =>
        PlayInRemote(app, control => control.PlayPlaylistAsync(playlist));

    /// <summary>
    /// Plays one song of a playlist looked through in the library (<see cref="RemotePlaylistSongsAsync"/>), carrying on
    /// through the playlist, starting to control the app first when it isn't.
    /// </summary>
    public void PlayFromRemotePlaylist(RemoteApp app, RemoteList list, RemoteTrack track) =>
        PlayInRemote(app, control => control.PlayFromListAsync(list, track));

    private void PlayInRemote(RemoteApp app, Func<RemoteControl, Task> play)
    {
        if (_remote is not null && _settings.RemoteApp == app)
        {
            RemoteTakes(play);
            return;
        }

        _playWhenConnected = (app, play);
        ControlRemote(app, signIn: true);
    }

    /// <summary>
    /// A playlist's songs, for the library to show without playing anything, or null when the app can't be asked
    /// (signed out of Spotify, say).
    /// </summary>
    /// <exception cref="RemoteProblem">When the app won't say, worded for the user.</exception>
    public async Task<RemoteList?> RemotePlaylistSongsAsync(RemoteApp app, RemotePlaylist playlist)
    {
        switch (app)
        {
            case RemoteApp.Spotify:
                return await WithSpotifyAsync(spotify => spotify.SongsAsync(playlist, CancellationToken.None));
            default:
                return null;
        }
    }

    /// <summary>The other app's song now playing, for the library to mark in a playlist it shows, or null.</summary>
    public RemoteTrack? RemotePlayingTrack => _remote?.State?.Track;

    private void OnRemoteChanged(object? sender, EventArgs e)
    {
        if (sender != _remote)
        {
            return;
        }

        ShowRemote();
        ShowRemoteList();
        _libraryWindow.MarkRemotePlaying(_remote?.State?.Track);
        UpdateListening();
        ShowWhereItPlays();
        UpdatePlayPauseButton();
        UpdateShuffleRepeatButtons();
        UpdatePosition();
    }

    /// <summary>Shows the other app's song in the title, details, window title and artwork window.</summary>
    private void ShowRemote()
    {
        if (_remote is not { } remote)
        {
            return;
        }

        var track = remote.State?.Track;
        ShowRemoteHeart(track);
        var name = track is null ? null : string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} – {track.Title}";
        var marquee = name ?? (remote.State is null && remote.Problem is null
            ? $"Connecting to {remote.Player.Name}…"
            : $"Nothing playing in {remote.Player.Name}");
        // Set only when it changes, so a long title keeps scrolling smoothly.
        if (NowPlayingText.Text != marquee)
        {
            SetMarquee(marquee);
        }

        Title = name is null ? "Lucent" : $"{name} - Lucent";

        // Spotify says which of the account's devices it's playing on, which may not be this computer.
        var where = remote.State?.Device is { } device ? $"{remote.Player.Name} on {device}" : remote.Player.Name;
        TrackDetails.Text = SongNotice ?? remote.Problem
            ?? (remote.NeedsOpening ? $"{remote.Player.Name} isn't open." : null)
            ?? (string.IsNullOrEmpty(track?.Album) ? $"Playing in {where}" : $"{track.Album} · in {where}");
        TrackDetails.ToolTip = remote.Problem ?? $"The sound comes from {where}. Lucent shows it and presses its buttons.";
        ShowVolume();
        ShowOpenApp();

        if (track?.Id != _shownRemoteTrack || !ReferenceEquals(remote.Artwork, _shownRemoteArtwork))
        {
            _shownRemoteTrack = track?.Id;
            _shownRemoteArtwork = remote.Artwork;
            ArtworkWindow.ShowRemote(track, remote.Artwork);
        }
    }

    /// <summary>
    /// Shows the Open button in the volume bar's place while the controlled app isn't open on this computer (Spotify not
    /// started, no Pandora tab), so one click opens it and carries on with what was asked for.
    /// </summary>
    private void ShowOpenApp()
    {
        var remote = _remote is { NeedsOpening: true } needs ? needs : null;
        OpenAppButton.Visibility = remote is null ? Visibility.Collapsed : Visibility.Visible;
        if (remote is null)
        {
            return;
        }

        var name = remote.Player.Name;
        OpenAppButton.Content = remote.IsOpening ? $"Opening {name}…" : $"Open {name}";
        OpenAppButton.IsEnabled = !remote.IsOpening;
        OpenAppButton.ToolTip = $"Start {name}, then Lucent carries on";
    }

    private void OnOpenApp(object sender, RoutedEventArgs e) => RemoteTakes(remote => remote.OpenAppAsync());

    /// <summary>
    /// Says in the visuals window when the controlled app plays on another device (Spotify on a TV, say), whose sound
    /// never reaches this computer, so the visuals hear nothing; with a button to move it here, where the app can.
    /// </summary>
    private void ShowWhereItPlays()
    {
        // Only while nothing is heard: a computer the app names differently from this one may still be this one.
        var note = _remote is { } remote && _player.Capture.HeardNothingFor(TimeSpan.FromSeconds(2)) ? remote.ElsewhereNote : null;
        _visuals.ShowElsewhere(note, _remote?.CanPlayHere == true);
        EqualizerWindow.ShowPlayingIn(_remote?.Player.Name);
    }

    // Set while the volume bar is being moved to show a volume, so that isn't taken as the user changing it.
    private bool _showingVolume;

    /// <summary>
    /// Shows the controlled app's volume on the volume bar, on the device it's playing on, or hides the bar when Lucent
    /// can't change the volume there. Lucent's own volume comes back when it stops controlling the app, or Windows' volume
    /// while the visuals listen to every app (see MainWindow.ComputerVolume.cs).
    /// </summary>
    private void ShowVolume()
    {
        _showingVolume = true;
        try
        {
            var visible = true;
            if (_remote is { } remote)
            {
                var volume = remote.HasVolume ? remote.State?.Volume : null;
                // The Open button takes the bar's place while the app isn't open.
                visible = volume is not null && !remote.NeedsOpening;
                if (volume is { } level)
                {
                    var where = remote.State?.Device is { } device ? $"{remote.Player.Name} on {device}" : remote.Player.Name;
                    VolumeSlider.Value = level;
                    VolumeText.Text = $"{level * 100:0}%";
                    VolumeSlider.ToolTip = $"{where}'s volume (mouse wheel over the player changes it too)";
                }
            }
            else if (BarIsComputerVolume)
            {
                visible = ShowComputerVolume();
            }
            else
            {
                VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
                VolumeText.Text = $"{VolumeSlider.Value * 100:0}%";
                VolumeSlider.ToolTip = "Volume (mouse wheel over the player changes it too)";
            }

            ((UIElement)VolumeSlider.Parent).Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        }
        finally
        {
            _showingVolume = false;
        }
    }

    // What the visuals are listening to: null for nothing (Lucent's own music), EveryApp for every app's sound,
    // or the process ID of the app being controlled.
    private const int EveryApp = -1;
    private int? _listeningTo;

    // Set when Windows couldn't listen to just one app, so every app's sound is listened to instead.
    private bool _oneAppFailed;

    /// <summary>
    /// Has the visuals listen to every app's sound when asked to, and otherwise, while another app is being
    /// controlled, to just that app, so a video or a call playing alongside doesn't move them. Called again
    /// with each read of the app, to follow it when it starts, quits or moves to another browser.
    /// </summary>
    private void UpdateListening()
    {
        // An input chosen for the visuals (Ableton through TotalMix's loopback, say) comes first.
        if (UpdateInputListening())
        {
            return;
        }

        int? wanted;
        if (_settings.ListenToOtherApps || (_remote is not null && (!SystemAudio.CanListenToOneApp || _oneAppFailed)))
        {
            wanted = EveryApp;
        }
        else if (_remote is null)
        {
            wanted = null;
        }
        else
        {
            wanted = _systemAudio is not null && _listeningTo is int current and > 0 && IsRemoteProcess(current)
                ? current
                : FindRemoteProcess();
        }

        if (wanted == (_systemAudio is null ? null : _listeningTo))
        {
            return;
        }

        _systemAudio?.Dispose();
        _systemAudio = null;
        _listeningTo = null;
        if (wanted is not { } target)
        {
            return;
        }

        try
        {
            var listening = target == EveryApp ? SystemAudio.Start(_player.Capture) : SystemAudio.StartForApp(_player.Capture, target);
            listening.Stopped += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // The output went away (unplugged headphones, say), or the app did: listen again to whatever's there now.
                if (_systemAudio == listening)
                {
                    listening.Dispose();
                    _systemAudio = null;
                    _listeningTo = null;
                    UpdateListening();
                }
            });
            _systemAudio = listening;
            _listeningTo = target;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't listen to other apps: {ex}");
            if (target != EveryApp)
            {
                // This Windows can't listen to one app after all, so the visuals hear every app's sound.
                _oneAppFailed = true;
                UpdateListening();
            }
        }
    }

    // The process the controlled app's sound comes from, or null when it isn't running on this PC (Spotify
    // playing on a phone, say).
    private int? FindRemoteProcess()
    {
        foreach (var name in RemoteProcessNames())
        {
            using var process = SystemAudio.FindApp(name);
            if (process is not null)
            {
                return process.Id;
            }
        }

        return null;
    }

    private bool IsRemoteProcess(int id)
    {
        try
        {
            using var process = Process.GetProcessById(id);
            return !process.HasExited && RemoteProcessNames().Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private IReadOnlyList<string> RemoteProcessNames() => _remote?.Player switch
    {
        SpotifyRemote => ["Spotify"],
        MediaControlsRemote media => media.ProcessNames,
        _ => [],
    };

    private static string RemoteName(RemoteApp app) => app switch
    {
        RemoteApp.AppleMusic => "Apple Music",
        RemoteApp.Tidal => "TIDAL",
        RemoteApp.YouTube => "your browser",
        _ => app.ToString(),
    };

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            throw new RemoteProblem("Lucent couldn't open a web browser to sign in with.", ex);
        }
    }

    private void StopStreaming()
    {
        _signingIn?.Cancel();
        _remote?.Dispose();
        _remote = null;
        ShowWhereItPlays();
        _systemAudio?.Dispose();
        _systemAudio = null;
        StopInputListening();
    }
}
