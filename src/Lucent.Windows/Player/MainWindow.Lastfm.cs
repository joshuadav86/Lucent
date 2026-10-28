using System.Net.Http;
using System.Windows;
using Lucent.Lastfm;

namespace Lucent;

// Scrobbling to Last.fm: signing in from the Last.fm tab in Options, and telling Last.fm what plays (Lucent's own
// music, online radio, and the music apps it controls). The rules for when a song counts are in Lastfm/Scrobbler.cs.
public partial class MainWindow
{
    private Scrobbler? _scrobbler;
    private CancellationTokenSource? _lastfmSigningIn;

    // Why Lucent isn't signed in when it should be (the sign-in didn't work out, or Last.fm took it back), for the tab.
    private string? _lastfmProblem;

    /// <summary>Starts scrobbling when Lucent is signed in to Last.fm.</summary>
    private void StartScrobbling()
    {
        if (_scrobbler is not null || _settings.LastfmUser is null || LastfmClient.App is null)
        {
            return;
        }

        if (LastfmAccount.Load() is not { } key)
        {
            // The sign-in can't be read back (another Windows account's, say), so it has to be done again.
            _settings.LastfmUser = null;
            _settings.Save();
            return;
        }

        var scrobbler = new Scrobbler(new LastfmClient(StreamingHttp, key), Scrobbler.WaitingFile);
        scrobbler.Changed += (_, _) => ShowLastfmState();
        scrobbler.SignedOut += (_, _) =>
        {
            var problem = scrobbler.Problem;
            SignOutOfLastfm();
            _lastfmProblem = problem;
            ShowLastfmState();
        };
        _scrobbler = scrobbler;
    }

    /// <summary>Tells the scrobbler what's playing. The player's position timer calls it five times a second.</summary>
    private void ObserveForScrobbling() => _scrobbler?.Observe(NowListening());

    /// <summary>The song playing now as Last.fm should hear of it, or null when there's none to scrobble.</summary>
    private Listening? NowListening()
    {
        if (_remote is not null)
        {
            return _settings.LastfmScrobbleApps && _remote.State is { Track: { } playing } state
                && ScrobbleSong.FromApp(_settings.RemoteApp, playing) is { } song
                ? new Listening(song, state.IsPlaying, state.PositionNow)
                : null;
        }

        if (_station is not null)
        {
            // Only a song the station names as "Artist - Title"; a show's name or a station without song titles isn't one.
            var (artist, title) = SplitSong(_radioStream?.NowPlaying ?? (_liveShow is { IsSong: true } listed ? listed.Title : null));
            return _settings.LastfmScrobbleRadio && artist is not null && title is not null
                ? new Listening(new ScrobbleSong(artist, title, ChosenByUser: false), _player.IsPlaying, null)
                : null;
        }

        if (_current?.Track is not { } track || !_player.HasTrack)
        {
            return null;
        }

        var duration = track.Duration > TimeSpan.Zero ? track.Duration : _player.Duration;
        return new Listening(new ScrobbleSong(track.Artist, track.Title, track.Album, duration, track.Tags.AlbumArtist),
            _player.IsPlaying, _player.Position);
    }

    private async void OnLastfmSignIn(object sender, RoutedEventArgs e)
    {
        if (_lastfmSigningIn is not null)
        {
            _lastfmSigningIn.Cancel();
            return;
        }

        if (_settings.LastfmUser is not null)
        {
            SignOutOfLastfm();
            ShowLastfmState();
            return;
        }

        _lastfmProblem = null;
        _lastfmSigningIn = new CancellationTokenSource();
        ShowLastfmState();
        try
        {
            var session = await LastfmClient.SignInAsync(StreamingHttp, OpenInBrowser, _lastfmSigningIn.Token);
            LastfmAccount.Save(session.Key);
            _settings.LastfmUser = session.UserName;
            _settings.Save();
            StartScrobbling();
        }
        catch (OperationCanceledException) when (_lastfmSigningIn.IsCancellationRequested)
        {
            // Cancelled with the button.
        }
        catch (LastfmProblem problem)
        {
            _lastfmProblem = problem.Message;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            _lastfmProblem = "Couldn't reach Last.fm. Check your internet connection and try again.";
        }
        finally
        {
            _lastfmSigningIn.Dispose();
            _lastfmSigningIn = null;
            ShowLastfmState();
        }
    }

    private void SignOutOfLastfm()
    {
        _scrobbler?.Forget();
        _scrobbler = null;
        LastfmAccount.Clear();
        _settings.LastfmUser = null;
        _settings.Save();
    }

    private void OnLastfmProfile(object sender, RoutedEventArgs e)
    {
        if (_settings.LastfmUser is { } user)
        {
            OpenInBrowser($"https://www.last.fm/user/{Uri.EscapeDataString(user)}");
        }
    }

    private void OnLastfmSwitched(object sender, RoutedEventArgs e)
    {
        _settings.LastfmScrobbleApps = LastfmAppsSwitch.IsChecked == true;
        _settings.LastfmScrobbleRadio = LastfmRadioSwitch.IsChecked == true;
        _settings.Save();
    }

    /// <summary>Shows on the Last.fm tab whether Lucent is signed in, and how scrobbling is going.</summary>
    private void ShowLastfmState()
    {
        var canSignIn = LastfmClient.App is not null;
        var user = _settings.LastfmUser;
        LastfmAccountText.Text = _lastfmSigningIn is not null
            ? "Say yes to Lucent on the Last.fm page that opened in your browser. Lucent waits here for the answer."
            : user is not null ? $"Signed in as {user}."
            : canSignIn ? "Not signed in."
            : "This copy of Lucent wasn't built with a Last.fm app to sign in with.";
        LastfmSignInButton.Content = _lastfmSigningIn is not null ? "Cancel" : user is not null ? "Sign out" : "Sign in to Last.fm";
        LastfmSignInButton.IsEnabled = canSignIn || user is not null;
        LastfmProfileButton.Visibility = user is not null && _lastfmSigningIn is null ? Visibility.Visible : Visibility.Collapsed;

        var status = _lastfmProblem ?? (user is not null ? _scrobbler?.Status : null);
        LastfmStatusText.Text = status;
        LastfmStatusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        LastfmAppsSwitch.IsChecked = _settings.LastfmScrobbleApps;
        LastfmRadioSwitch.IsChecked = _settings.LastfmScrobbleRadio;
    }
}
