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

    private bool ShownIsPlaying => _remote?.IsPlaying ?? _player.IsPlaying;

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

    /// <summary>Adds the other-apps items to the Add music menu.</summary>
    private void AddStreamingItems(MenuFlyout menu)
    {
        menu.Items.Add(new Separator());

        var listen = new MenuItem
        {
            Header = "Visuals for every app's sound",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.ListenToOtherApps,
            IsEnabled = SystemAudio.IsSupported,
        };
        ToolTip.SetTip(listen, !SystemAudio.IsSupported ? "Listening to other apps needs macOS 14.2 or later."
            : _remote is not null && !_settings.ListenToOtherApps
                ? $"While Lucent controls {_remote.Player.Name}, the visuals hear only {_remote.Player.Name}. Turn this on to have them hear every app."
            : $"The visuals, meter and analyzer react to whatever is playing on {SystemAudio.DefaultOutputName() ?? "this Mac"}: Spotify, a browser, a game. macOS asks once whether Lucent may record the Mac's sound.");
        listen.Click += (_, _) =>
        {
            _settings.ListenToOtherApps = !_settings.ListenToOtherApps;
            _settings.Save();
            UpdateListening();
        };
        menu.Items.Add(listen);

        var hasClientId = SpotifySignIn.ClientId is not null;
        var spotifySaved = SpotifyAccount.Load() is not null;
        var spotify = new MenuItem
        {
            Header = _signingIn is not null ? "Signing in to Spotify…" : spotifySaved ? "Control Spotify" : "Sign in to Spotify…",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.RemoteApp == RemoteApp.Spotify,
            IsEnabled = hasClientId && _signingIn is null,
        };
        ToolTip.SetTip(spotify, hasClientId
            ? "Show what Spotify is playing and use the player's buttons for it. The sound stays in Spotify. Playing, pausing and skipping need Premium."
            : "This copy of Lucent wasn't built with a Spotify app to sign in with.");
        spotify.Click += (_, _) => ToggleRemote(RemoteApp.Spotify);
        menu.Items.Add(spotify);

        var appleMusic = new MenuItem
        {
            Header = "Control Apple Music",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.RemoteApp == RemoteApp.AppleMusic,
        };
        ToolTip.SetTip(appleMusic, "Show what the Music app is playing and use the player's buttons for it. The sound stays in Music. macOS asks once whether Lucent may control Music.");
        appleMusic.Click += (_, _) => ToggleRemote(RemoteApp.AppleMusic);
        menu.Items.Add(appleMusic);

        var youTube = new MenuItem
        {
            Header = "Control YouTube",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.RemoteApp == RemoteApp.YouTube,
        };
        ToolTip.SetTip(youTube, "Show the YouTube video playing in Safari, Chrome, Edge or Brave and use the player's buttons for it. The sound stays in the browser. "
            + "The browser needs \"Allow JavaScript from Apple Events\" turned on, and macOS asks once whether Lucent may control it.");
        youTube.Click += (_, _) => ToggleRemote(RemoteApp.YouTube);
        menu.Items.Add(youTube);

        if (spotifySaved)
        {
            var signOut = new MenuItem { Header = "Sign out of Spotify" };
            signOut.Click += (_, _) =>
            {
                if (_settings.RemoteApp == RemoteApp.Spotify)
                {
                    StopControllingRemote(pause: false);
                }

                SpotifyAccount.Clear();
            };
            menu.Items.Add(signOut);
        }
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
                        Activate();
                    }

                    player = new SpotifyRemote(StreamingHttp, refreshToken, SpotifyAccount.Save);
                    break;
                case RemoteApp.AppleMusic:
                    player = new AppleMusicRemote();
                    break;
                case RemoteApp.YouTube:
                    player = new YouTubeRemote(StreamingHttp);
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
        UpdateListening();
        ShowRemote();
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

    private void OnRemoteChanged(object? sender, EventArgs e)
    {
        if (sender != _remote)
        {
            return;
        }

        ShowRemote();
        ShowRemoteList();
        UpdateListening();
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

        TrackDetails.Text = remote.Problem
            ?? (string.IsNullOrEmpty(track?.Album) ? $"Playing in {remote.Player.Name}" : $"{track.Album} · in {remote.Player.Name}");
        ToolTip.SetTip(TrackDetails, remote.Problem ?? $"The sound comes from {remote.Player.Name}. Lucent shows it and presses its buttons.");

        if (track?.Id != _shownRemoteTrack || !ReferenceEquals(remote.Artwork, _shownRemoteArtwork))
        {
            _shownRemoteTrack = track?.Id;
            _shownRemoteArtwork = remote.Artwork;
            ArtworkWindow.ShowRemote(track, remote.Artwork);
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
        YouTubeRemote youTube => youTube.BundlePrefixes,
        _ => [],
    };

    private static string RemoteName(RemoteApp app) => app == RemoteApp.AppleMusic ? "Apple Music" : app.ToString();

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
        _systemAudio?.Dispose();
        _systemAudio = null;
    }
}
