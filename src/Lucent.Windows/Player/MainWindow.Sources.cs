using System.Windows;
using Lucent.Audio;
using Lucent.Streaming;

namespace Lucent;

// Sources: the panel with every way to bring sound in (files and folders, online radio, and the music apps
// Lucent can show and control), opened from the labelled button in the title bar or from the eject button.
public partial class MainWindow
{
    private DateTime _sourcesClosedAt;

    private void OnToggleSources(object sender, RoutedEventArgs e) => ToggleSources(SourcesButton);

    private void OnOpenMenu(object sender, RoutedEventArgs e) => ToggleSources(OpenButton);

    /// <summary>Opens the Sources panel under <paramref name="from"/>, or closes it if it's open.</summary>
    private void ToggleSources(FrameworkElement from)
    {
        if (SourcesPopup.IsOpen)
        {
            SourcesPopup.IsOpen = false;
            return;
        }

        // The click that closed the panel (by clicking outside it, on a button that opens it) shouldn't reopen it.
        if (_visuals.IsFullscreen || (DateTime.UtcNow - _sourcesClosedAt).TotalMilliseconds < 250)
        {
            SourcesButton.IsChecked = false;
            return;
        }

        // A classic skin hides Lucent's own player and its buttons: open under the classic player instead.
        if (!from.IsVisible)
        {
            from = PlayerRoot;
        }

        UpdateSourcesPanel();
        SourcesPopup.PlacementTarget = from;
        // The panel has a 16px margin for its shadow. Under the title bar's button it lines up with the button's
        // right edge, so it stays over the player; under the eject button, with its left edge.
        SourcesPopup.HorizontalOffset = from == SourcesButton ? from.ActualWidth - 336 : -16;
        SourcesPopup.IsOpen = true;
    }

    private void OnSourcesOpened(object? sender, EventArgs e) => SourcesButton.IsChecked = true;

    private void OnSourcesClosed(object? sender, EventArgs e)
    {
        SourcesButton.IsChecked = false;
        _sourcesClosedAt = DateTime.UtcNow;
    }

    /// <summary>Brings the music-app switches up to date: which app is being controlled, and whether Spotify is signed in.</summary>
    private void UpdateSourcesPanel()
    {
        var hasClientId = SpotifySignIn.ClientId is not null;
        var spotifySaved = SpotifyAccount.Load() is not null;
        SpotifySwitch.Content = _signingIn is not null ? "Spotify: signing in…" : spotifySaved ? "Spotify" : "Spotify: sign in";
        SpotifySwitch.IsChecked = _settings.RemoteApp == RemoteApp.Spotify;
        SpotifySwitch.IsEnabled = hasClientId && _signingIn is null;
        SpotifySwitch.ToolTip = hasClientId
            ? "Show what Spotify is playing and use the player's buttons for it. The sound stays in Spotify. Playing, pausing and skipping need Premium."
            : "This copy of Lucent wasn't built with a Spotify app to sign in with.";
        SpotifySignOutButton.Visibility = spotifySaved ? Visibility.Visible : Visibility.Collapsed;

        AppleMusicSwitch.IsChecked = _settings.RemoteApp == RemoteApp.AppleMusic;
        AppleMusicSwitch.IsEnabled = AppleMusicRemote.IsSupported;
        AppleMusicSwitch.ToolTip = "Show what the Apple Music app is playing and use the player's buttons for it. The sound stays in Apple Music.";

        TidalSwitch.IsChecked = _settings.RemoteApp == RemoteApp.Tidal;
        TidalSwitch.IsEnabled = TidalRemote.IsSupported;
        TidalSwitch.ToolTip = "Show what the TIDAL app is playing and use the player's buttons for it. The sound stays in TIDAL. For TIDAL's web player, use the web browser switch below.";

        PandoraSwitch.IsChecked = _settings.RemoteApp == RemoteApp.Pandora;
        PandoraSwitch.IsEnabled = PandoraRemote.IsSupported;
        PandoraSwitch.ToolTip = "Show what Pandora is playing (its app, or pandora.com in your browser) and use the player's buttons for it, thumbs up and down included. The sound stays in Pandora.";

        YouTubeSwitch.IsChecked = _settings.RemoteApp == RemoteApp.YouTube;
        YouTubeSwitch.IsEnabled = YouTubeRemote.IsSupported;
        YouTubeSwitch.ToolTip = "Show what's playing in your web browser (YouTube, SoundCloud, TIDAL's web player, or any site) and use the player's buttons for it. The sound stays in the browser.";

        ListenSwitch.IsChecked = _settings.ListenToOtherApps;
        ListenHint.Text = _remote is not null && !_settings.ListenToOtherApps
            ? $"While Lucent controls {_remote.Player.Name}, the visuals hear only {_remote.Player.Name}. Turn this on to have them hear every app."
            : $"The visuals, meter, and analyzer react to whatever is playing on {SystemAudio.DefaultOutputName() ?? "this PC"}: Spotify, a browser, a game.";
        UpdateListenSource();
    }

    private void OnSourcesAddFiles(object sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        AddFilesFromDialog(this);
    }

    private void OnSourcesAddFolder(object sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        AddFolderFromDialog(this);
    }

    private void OnSourcesImport(object sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowImport();
    }

    private void OnSourcesStores(object sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowStores();
    }

    private void OnSourcesRadio(object sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowRadio();
    }

    private void OnSourcesRemoteSwitched(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string tag && Enum.TryParse<RemoteApp>(tag, out var app))
        {
            ToggleRemote(app);
        }

        // Only one app can be controlled at once, and signing in takes a moment: show how things stand now.
        UpdateSourcesPanel();
    }

    private void OnSignOutOfSpotify(object sender, RoutedEventArgs e)
    {
        if (_settings.RemoteApp == RemoteApp.Spotify)
        {
            StopControllingRemote(pause: false);
        }

        SpotifyAccount.Clear();
        RemoteListStore.ForgetAll();
        SpotifyAccountChanged();
        UpdateSourcesPanel();
    }

    private void OnListenSwitched(object sender, RoutedEventArgs e)
    {
        _settings.ListenToOtherApps = !_settings.ListenToOtherApps;
        _settings.Save();
        UpdateListening();
        UpdateSourcesPanel();
    }
}
