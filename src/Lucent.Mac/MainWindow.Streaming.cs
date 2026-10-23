using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Streaming;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac;

// Other apps: the visuals listening to every app's sound (see Audio/SystemAudio.cs), and the player
// showing and controlling Spotify, Apple Music or YouTube (see RemoteControl) while their sound stays in their app.
public partial class MainWindow
{
    private static readonly HttpClient StreamingHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    private SystemAudio? _systemAudio;
    private RemoteControl? _remote;
    private CancellationTokenSource? _signingIn;
    private string? _shownRemoteTrack;
    private byte[]? _shownRemoteArtwork;

    // What the player shows: the other app's song while one is being controlled, otherwise Lucent's own.
    private bool ShownHasTrack => _remote is not null ? _remote.State?.Track is not null : _player.HasTrack;

    // A station tuning in counts as playing, so the button shows it can be stopped.
    private bool ShownIsPlaying => _remote?.IsPlaying ?? (_player.IsPlaying || _tuning is not null);

    private TimeSpan ShownPosition => _remote is not null ? _remote.State?.PositionNow ?? TimeSpan.Zero : _player.Position;

    private TimeSpan ShownDuration => _remote is not null ? _remote.State?.Track?.Duration ?? TimeSpan.Zero : _player.Duration;

    /// <summary>Picks up where the last run left off: listening to other apps, and the app being controlled.</summary>
    private void StartStreaming()
    {
        if (_settings.RemoteApp != RemoteApp.None)
        {
            ControlRemote(_settings.RemoteApp, signIn: false);
        }

        UpdateListening();
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
        // A Spotify playlist double-clicked in the library while Spotify wasn't being controlled plays once it is.
        var playlist = _playWhenConnected;
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
                        _libraryWindow.SpotifyChanged();
                        Activate();
                    }

                    player = new SpotifyRemote(StreamingHttp, refreshToken, SpotifyAccount.Save, new SpotifyAppRemote());
                    break;
                case RemoteApp.AppleMusic:
                    player = new AppleMusicRemote();
                    break;
                case RemoteApp.YouTube:
                    player = new YouTubeRemote(StreamingHttp);
                    break;
                case RemoteApp.Tidal:
                    player = new TidalRemote(StreamingHttp);
                    break;
                case RemoteApp.SoundCloud:
                    player = new SoundCloudRemote(StreamingHttp);
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
            await MessageWindow.ShowAsync(this, message, "Lucent");
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
        _remote = new RemoteControl(player);
        _remote.Changed += OnRemoteChanged;
        if (playlist is not null && app == RemoteApp.Spotify)
        {
            _ = _remote.PlayPlaylistAsync(playlist);
        }

        UpdateListening();
        ShowRemote();
        ShowWhereItPlays();
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
        _shownRemoteTrack = null;
        _shownRemoteArtwork = null;
        ShowVolume();
        ShowRemoteList();
        UpdateListening();
        ShowNowPlaying(_current?.Track);
        UpdatePlayPauseButton();
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

        _queue.EmptyPlaylistHint.IsVisible = _playlist.Count == 0;
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
        _queue.ShowRemoteList(_remote?.Player.Name ?? string.Empty, _remote?.List, _remote?.State?.Track?.Id);
        if (showing && !_queue.ShowingRemoteList)
        {
            ShowQueueSize();
        }
    }

    /// <summary>Plays one of the songs from the other app's playlist, in that app.</summary>
    public void PlayFromRemoteList(RemoteTrack track) => RemoteTakes(remote => remote.PlayFromListAsync(track));

    private RemotePlaylist? _playWhenConnected;

    /// <summary>
    /// The Spotify account's playlists for the library, or null when Lucent isn't signed in to Spotify. While Spotify is
    /// being controlled its connection asks; otherwise one is made just for this, from the saved sign-in (which Spotify
    /// renews with each use, so two connections at once would sign each other out).
    /// </summary>
    /// <exception cref="RemoteProblem">When Spotify won't list them, worded for the user.</exception>
    public async Task<IReadOnlyList<RemotePlaylist>?> SpotifyPlaylistsAsync()
    {
        if (_remote?.Player is SpotifyRemote controlling)
        {
            return await controlling.PlaylistsAsync(CancellationToken.None);
        }

        if (SpotifyAccount.Load() is not { } refreshToken)
        {
            return null;
        }

        using var spotify = new SpotifyRemote(StreamingHttp, refreshToken, SpotifyAccount.Save);
        return await spotify.PlaylistsAsync(CancellationToken.None);
    }

    /// <summary>Plays one of the account's Spotify playlists in Spotify, starting to control Spotify first when it isn't.</summary>
    public void PlaySpotifyPlaylist(RemotePlaylist playlist)
    {
        if (_remote is { Player: SpotifyRemote } remote)
        {
            RemoteTakes(control => control.PlayPlaylistAsync(playlist));
            return;
        }

        _playWhenConnected = playlist;
        ControlRemote(RemoteApp.Spotify, signIn: true);
    }

    private void OnRemoteChanged(object? sender, EventArgs e)
    {
        if (sender != _remote)
        {
            return;
        }

        ShowRemote();
        ShowRemoteList();
        UpdateListening();
        ShowWhereItPlays();
        UpdatePlayPauseButton();
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
        TrackFavoriteButton.IsEnabled = false;
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
        TrackDetails.Text = remote.Problem
            ?? (string.IsNullOrEmpty(track?.Album) ? $"Playing in {where}" : $"{track.Album} · in {where}");
        ToolTip.SetTip(TrackDetails, remote.Problem ?? $"The sound comes from {where}. Lucent shows it and presses its buttons.");
        ShowVolume();

        if (track?.Id != _shownRemoteTrack || !ReferenceEquals(remote.Artwork, _shownRemoteArtwork))
        {
            _shownRemoteTrack = track?.Id;
            _shownRemoteArtwork = remote.Artwork;
            ArtworkWindow.ShowRemote(track, remote.Artwork);
        }
    }

    /// <summary>
    /// Says in the visuals window when the controlled app plays on another device (Spotify on a TV, say), whose sound
    /// never reaches this computer, so the visuals hear nothing; with a button to move it here, where the app can.
    /// </summary>
    private void ShowWhereItPlays()
    {
        // Only while nothing is heard: a computer the app names differently from this one may still be this one.
        var note = _remote is { } remote && _player.Capture.HeardNothingFor(TimeSpan.FromSeconds(2)) ? remote.ElsewhereNote : null;
        _visuals.ShowElsewhere(note, _remote?.CanPlayHere == true);
    }

    // Set while the volume bar is being moved to show a volume, so that isn't taken as the user changing it.
    private bool _showingVolume;

    /// <summary>
    /// Shows the controlled app's volume on the volume bar, on the device it's playing on, or hides the bar when Lucent
    /// can't change the volume there. Lucent's own volume comes back when it stops controlling the app.
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
                visible = volume is not null;
                if (volume is { } level)
                {
                    var where = remote.State?.Device is { } device ? $"{remote.Player.Name} on {device}" : remote.Player.Name;
                    VolumeSlider.Value = level;
                    VolumeText.Text = $"{level * 100:0}%";
                    ToolTip.SetTip(VolumeSlider, $"{where}'s volume (scrolling over the player changes it too)");
                }
            }
            else
            {
                VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
                VolumeText.Text = $"{VolumeSlider.Value * 100:0}%";
                ToolTip.SetTip(VolumeSlider, "Volume (scrolling over the player changes it too)");
            }

            if (VolumeSlider.Parent is Control bar)
            {
                // Hidden but still taking its room, so the player doesn't change shape.
                bar.Opacity = visible ? 1 : 0;
                bar.IsHitTestVisible = visible;
            }
        }
        finally
        {
            _showingVolume = false;
        }
    }

    // What the visuals are listening to: null for nothing (Lucent's own music), EveryApp for every app's sound,
    // or the controlled app's processes (Core Audio's objects for them, joined with commas).
    private const string EveryApp = "*";
    private string? _listeningTo;

    // Set when macOS couldn't listen to just one app, so every app's sound is listened to instead.
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

        List<uint>? processes = null;
        string? wanted;
        if (!SystemAudio.IsSupported)
        {
            return;
        }

        if (_settings.ListenToOtherApps || (_remote is not null && _oneAppFailed))
        {
            wanted = EveryApp;
        }
        else if (_remote is null)
        {
            wanted = null;
        }
        else
        {
            // Only processes that use sound have an object, so the app is looked for again until it plays.
            processes = SystemAudio.FindApp(RemoteBundles());
            wanted = processes.Count == 0 ? null : string.Join(",", processes);
        }

        if (wanted == (_systemAudio is null ? null : _listeningTo))
        {
            return;
        }

        _systemAudio?.Dispose();
        _systemAudio = null;
        _listeningTo = null;
        if (wanted is null)
        {
            return;
        }

        try
        {
            var listening = processes is null ? SystemAudio.Start(_player.Capture) : SystemAudio.StartForApp(_player.Capture, processes);
            listening.Stopped += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                // The output went away (unplugged headphones, say): listen to whichever one the Mac uses now.
                if (_systemAudio == listening)
                {
                    listening.Dispose();
                    _systemAudio = null;
                    _listeningTo = null;
                    UpdateListening();
                }
            });
            _systemAudio = listening;
            _listeningTo = wanted;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't listen to other apps: {ex}");
            if (processes is not null)
            {
                // macOS wouldn't tap just the one app, so the visuals hear every app's sound.
                _oneAppFailed = true;
                UpdateListening();
            }
        }
    }

    // The bundle IDs the controlled app's sound comes from.
    private IReadOnlyList<string> RemoteBundles() => _remote?.Player switch
    {
        SpotifyRemote => ["com.spotify.client"],
        AppleMusicRemote => ["com.apple.Music"],
        BrowserTabRemote browser => browser.BundlePrefixes,
        _ => [],
    };

    private static string RemoteName(RemoteApp app) => app switch
    {
        RemoteApp.AppleMusic => "Apple Music",
        RemoteApp.Tidal => "TIDAL",
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
