using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Audio;
using Lucent.Mac.Streaming;
using Lucent.Streaming;

namespace Lucent.Mac;

// Sources: the panel with every way to bring sound in (files and folders, online radio, and the music apps
// Lucent can show and control), opened from the labelled button in the title bar or from the eject button.
public partial class MainWindow
{
    private DateTime _sourcesClosedAt;

    private void OnToggleSources(object? sender, RoutedEventArgs e) => ToggleSources(SourcesButton);

    private void OnOpenMenu(object? sender, RoutedEventArgs e) => ToggleSources(OpenButton);

    /// <summary>Opens the Sources panel under <paramref name="from"/>, or closes it if it's open.</summary>
    private void ToggleSources(Control from)
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
        if (!from.IsEffectivelyVisible)
        {
            from = PlayerRoot;
        }

        UpdateSourcesPanel();
        SourcesPopup.PlacementTarget = from;
        // The panel has a 16px margin for its shadow. Under the title bar's button it lines up with the button's
        // right edge, so it stays over the player; under the eject button, with its left edge.
        var underTitleBar = from == SourcesButton;
        SourcesPopup.Placement = underTitleBar ? PlacementMode.BottomEdgeAlignedRight : PlacementMode.BottomEdgeAlignedLeft;
        SourcesPopup.HorizontalOffset = underTitleBar ? 16 : -16;
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
        ToolTip.SetTip(SpotifySwitch, hasClientId
            ? "Show what Spotify is playing and use the player's buttons for it. The sound stays in Spotify. Playing, pausing and skipping need Premium."
            : "This copy of Lucent wasn't built with a Spotify app to sign in with.");
        SpotifySignOutButton.IsVisible = spotifySaved;

        AppleMusicSwitch.IsChecked = _settings.RemoteApp == RemoteApp.AppleMusic;
        ToolTip.SetTip(AppleMusicSwitch, "Show what the Music app is playing and use the player's buttons for it. The sound stays in Music. macOS asks once whether Lucent may control Music.");

        YouTubeSwitch.IsChecked = _settings.RemoteApp == RemoteApp.YouTube;
        ToolTip.SetTip(YouTubeSwitch, "Show the YouTube video playing in Safari, Chrome, Edge or Brave and use the player's buttons for it. The sound stays in the browser. "
            + "The browser needs \"Allow JavaScript from Apple Events\" turned on, and macOS asks once whether Lucent may control it.");

        TidalSwitch.IsChecked = _settings.RemoteApp == RemoteApp.Tidal;
        ToolTip.SetTip(TidalSwitch, "Show what TIDAL's web player (tidal.com) is playing in Safari, Chrome, Edge or Brave and use the player's buttons for it. "
            + "TIDAL's Mac app can't be controlled by other apps. The browser needs \"Allow JavaScript from Apple Events\" turned on, and macOS asks once whether Lucent may control it.");

        SoundCloudSwitch.IsChecked = _settings.RemoteApp == RemoteApp.SoundCloud;
        ToolTip.SetTip(SoundCloudSwitch, "Show what SoundCloud is playing in Safari, Chrome, Edge or Brave and use the player's buttons for it. The sound stays in the browser. "
            + "The browser needs \"Allow JavaScript from Apple Events\" turned on, and macOS asks once whether Lucent may control it.");

        PandoraSwitch.IsChecked = _settings.RemoteApp == RemoteApp.Pandora;
        ToolTip.SetTip(PandoraSwitch, "Show what Pandora's web player (pandora.com) is playing in Safari, Chrome, Edge or Brave and use the player's buttons for it, thumbs up and down included. "
            + "Pandora has no Mac app. The browser needs \"Allow JavaScript from Apple Events\" turned on, and macOS asks once whether Lucent may control it.");

        ListenSwitch.IsChecked = _settings.ListenToOtherApps;
        ListenSwitch.IsEnabled = SystemAudio.IsSupported;
        ListenHint.Text = !SystemAudio.IsSupported ? "Listening to other apps needs macOS 14.2 or later."
            : _remote is not null && !_settings.ListenToOtherApps
                ? $"While Lucent controls {_remote.Player.Name}, the visuals hear only {_remote.Player.Name}. Turn this on to have them hear every app."
            : $"The visuals, meter, and analyzer react to whatever is playing on {SystemAudio.DefaultOutputName() ?? "this Mac"}: Spotify, a browser, a game. macOS asks once whether Lucent may record the Mac's sound.";
        UpdateListenSource();
    }

    private void OnSourcesAddFiles(object? sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        AddFilesFromDialog(this);
    }

    private void OnSourcesAddFolder(object? sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        AddFolderFromDialog(this);
    }

    private void OnSourcesImport(object? sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowImport();
    }

    private void OnSourcesStores(object? sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowStores();
    }

    private void OnSourcesRadio(object? sender, RoutedEventArgs e)
    {
        SourcesPopup.IsOpen = false;
        ShowRadio();
    }

    private void OnSourcesRemoteSwitched(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is string tag && Enum.TryParse<RemoteApp>(tag, out var app))
        {
            ToggleRemote(app);
        }

        // Only one app can be controlled at once, and signing in takes a moment: show how things stand now.
        UpdateSourcesPanel();
    }

    private void OnSignOutOfSpotify(object? sender, RoutedEventArgs e)
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

    private void OnListenSwitched(object? sender, RoutedEventArgs e)
    {
        _settings.ListenToOtherApps = !_settings.ListenToOtherApps;
        _settings.Save();
        UpdateListening();
        ApplyOwnVolume();
        ShowVolume();
        UpdateSourcesPanel();
    }
}
