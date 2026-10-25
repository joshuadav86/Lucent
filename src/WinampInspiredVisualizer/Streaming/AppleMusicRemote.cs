using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// The Apple Music app for Windows (or iTunes), through Windows' media controls. No Apple sign-in is
/// needed; whoever is signed in to Apple Music is who's playing.
/// </summary>
/// <remarks>
/// Apple's MusicKit only runs on Apple's own platforms, and MusicKit on the web plays in a browser, not
/// the app, so this is how Windows does it. The Mac controls the Music app with AppleScript instead
/// (see the Mac's Streaming/AppleMusicRemote.cs).
/// </remarks>
public sealed class AppleMusicRemote : MediaControlsRemote
{
    public override string Name => "Apple Music";

    protected override string NotOpen => "Open the Apple Music app and play something";

    public override string ListLimit => "The Apple Music app for Windows only tells Windows the song playing, not what's up next, so Lucent can't show it here.";

    public override string PlaylistsLimit => "The Apple Music app for Windows doesn't let other apps see or change your playlists. On a Mac, Lucent can.";

    public override IReadOnlyList<string> ProcessNames { get; } = ["AppleMusic", "iTunes"];

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions) =>
        sessions.FirstOrDefault(session => IsAppleMusic(session.SourceAppUserModelId));

    // The Apple Music app is "AppleInc.AppleMusicWin_…!App"; iTunes from Apple's site or the Store has iTunes in its name.
    private static bool IsAppleMusic(string? appId) =>
        appId is not null && (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("iTunes", StringComparison.OrdinalIgnoreCase));
}
