using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Streaming;

namespace WinampInspiredVisualizer;

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

    /// <summary>Adds the other-apps items to the Add music menu.</summary>
    private void AddStreamingItems(ItemsControl menu)
    {
        menu.Items.Add(new Separator());

        var listen = new MenuItem
        {
            Header = "Visuals for every app's sound",
            IsCheckable = true,
            IsChecked = _settings.ListenToOtherApps || _remote is not null,
            IsEnabled = _remote is null,
            ToolTip = _remote is not null
                ? $"Always on while Lucent controls {_remote.Player.Name}."
                : $"The visuals, meter and analyzer react to whatever is playing on {SystemAudio.DefaultOutputName() ?? "this PC"}: Spotify, a browser, a game.",
        };
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
            IsCheckable = true,
            IsChecked = _settings.RemoteApp == RemoteApp.Spotify,
            IsEnabled = hasClientId && _signingIn is null,
            ToolTip = hasClientId
                ? "Show what Spotify is playing and use the player's buttons for it. The sound stays in Spotify. Playing, pausing and skipping need Premium."
                : "This copy of Lucent wasn't built with a Spotify app to sign in with.",
        };
        spotify.Click += (_, _) => ToggleRemote(RemoteApp.Spotify);
        menu.Items.Add(spotify);

        var appleMusic = new MenuItem
        {
            Header = "Control Apple Music",
            IsCheckable = true,
            IsChecked = _settings.RemoteApp == RemoteApp.AppleMusic,
            IsEnabled = AppleMusicRemote.IsSupported,
            ToolTip = "Show what the Apple Music app is playing and use the player's buttons for it. The sound stays in Apple Music.",
        };
        appleMusic.Click += (_, _) => ToggleRemote(RemoteApp.AppleMusic);
        menu.Items.Add(appleMusic);

        var youTube = new MenuItem
        {
            Header = "Control YouTube",
            IsCheckable = true,
            IsChecked = _settings.RemoteApp == RemoteApp.YouTube,
            IsEnabled = YouTubeRemote.IsSupported,
            ToolTip = "Show the YouTube video playing in your web browser and use the player's buttons for it. The sound stays in the browser.",
        };
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
                case RemoteApp.AppleMusic when AppleMusicRemote.IsSupported:
                    player = new AppleMusicRemote();
                    break;
                case RemoteApp.YouTube when YouTubeRemote.IsSupported:
                    player = new YouTubeRemote();
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

    private void OnRemoteChanged(object? sender, EventArgs e)
    {
        if (sender != _remote)
        {
            return;
        }

        ShowRemote();
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
        TrackDetails.ToolTip = remote.Problem ?? $"The sound comes from {remote.Player.Name}. Lucent shows it and presses its buttons.";

        if (track?.Id != _shownRemoteTrack || !ReferenceEquals(remote.Artwork, _shownRemoteArtwork))
        {
            _shownRemoteTrack = track?.Id;
            _shownRemoteArtwork = remote.Artwork;
            ArtworkWindow.ShowRemote(track, remote.Artwork);
        }
    }

    /// <summary>Has the visuals listen to every app's sound when asked to, or while another app is being controlled.</summary>
    private void UpdateListening()
    {
        var wanted = _settings.ListenToOtherApps || _remote is not null;
        if (wanted == (_systemAudio is not null))
        {
            return;
        }

        if (!wanted)
        {
            _systemAudio?.Dispose();
            _systemAudio = null;
            return;
        }

        try
        {
            var listening = SystemAudio.Start(_player.Capture);
            listening.Stopped += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // The output went away (unplugged headphones, say): listen to whichever one Windows uses now.
                if (_systemAudio == listening)
                {
                    listening.Dispose();
                    _systemAudio = null;
                    UpdateListening();
                }
            });
            _systemAudio = listening;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't listen to other apps: {ex}");
        }
    }

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
