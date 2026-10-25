using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// The TIDAL app for Windows, through Windows' media controls, which TIDAL tells what's playing (title,
/// artist, album and cover) and takes play, pause, skip and seek from. No TIDAL sign-in is needed;
/// whoever is signed in to the app is who's playing.
/// </summary>
/// <remarks>
/// TIDAL's web player plays in a browser, so the browser item controls that, like any site. TIDAL's Mac
/// app can't be controlled by other apps, so the Mac controls the web player instead (see the Mac's
/// Streaming/TidalRemote.cs).
/// </remarks>
public sealed class TidalRemote : MediaControlsRemote
{
    public override string Name => "TIDAL";

    protected override string NotOpen => "Open the TIDAL app and play something";

    public override string PlaylistsLimit => "TIDAL only shares playlists with apps it has approved, which Lucent isn't yet.";

    public override IReadOnlyList<string> ProcessNames { get; } = ["TIDAL"];

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions) =>
        sessions.FirstOrDefault(session => IsTidal(session.SourceAppUserModelId));

    // TIDAL's installer names it "com.squirrel.TIDAL.TIDAL" (or plain "TIDAL.exe"), and the Microsoft Store's has TIDAL in its package name.
    private static bool IsTidal(string? appId) =>
        appId is not null && appId.Contains("TIDAL", StringComparison.OrdinalIgnoreCase);
}
