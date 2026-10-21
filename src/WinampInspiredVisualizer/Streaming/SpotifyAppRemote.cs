using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// The Spotify app on this PC, through Windows' media controls, which Spotify tells what's playing (title,
/// artist, album and cover) and takes play, pause, skip and seek from. Lucent.Core's SpotifyRemote reads this
/// first, so the song shows and the buttons work even while Spotify's Web API has asked Lucent to wait.
/// </summary>
/// <remarks>
/// The Mac reads the Spotify app through AppleScript instead (see the Mac's Streaming/SpotifyAppRemote.cs).
/// </remarks>
public sealed class SpotifyAppRemote : MediaControlsRemote
{
    public override string Name => "Spotify";

    protected override string NotOpen => "Open Spotify and play something";

    public override IReadOnlyList<string> ProcessNames { get; } = ["Spotify"];

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions) =>
        sessions.FirstOrDefault(session => IsSpotify(session.SourceAppUserModelId));

    // Spotify's installer names it "Spotify.exe", and the Microsoft Store's has Spotify in its package name.
    private static bool IsSpotify(string? appId) =>
        appId is not null && appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase);
}
