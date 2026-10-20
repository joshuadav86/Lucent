using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// YouTube (or YouTube Music) playing in a web browser, through Windows' media controls. Chrome, Edge,
/// Firefox and the other big browsers tell Windows what a page is playing, with YouTube giving the
/// video's title, channel and thumbnail, and take its play, pause, skip and seek buttons. Nothing to
/// sign in to and nothing to turn on in the browser.
/// </summary>
/// <remarks>
/// Windows only says which browser is playing, not which site, so this is whatever the browser's media
/// controls show: YouTube while YouTube plays. A browser shows the tab that played last. The Mac has no
/// media controls for other apps to press, so it reaches the YouTube tab itself (see the Mac's
/// Streaming/YouTubeRemote.cs).
/// </remarks>
public sealed class YouTubeRemote : MediaControlsRemote
{
    // How each browser names itself to Windows: "Chrome" (and "Chrome._crx_…" for an installed YouTube
    // Music app), "MSEdge", "Brave…", "Opera…", "Vivaldi…", and Firefox by its install hash.
    private static readonly string[] Browsers = ["Chrome", "MSEdge", "Edge", "Brave", "Opera", "Vivaldi", "Firefox", "308046B0AF4A39CB", "Chromium"];

    public override string Name => "YouTube";

    protected override string NotOpen => "Play a YouTube video in your web browser";

    // The browser playing, as its process is named; every browser Lucent knows until one has been read.
    public override IReadOnlyList<string> ProcessNames => SourceAppId switch
    {
        { } id when id.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) || id.Contains("Edge", StringComparison.OrdinalIgnoreCase) => ["msedge"],
        { } id when id.Contains("Brave", StringComparison.OrdinalIgnoreCase) => ["brave"],
        { } id when id.Contains("Opera", StringComparison.OrdinalIgnoreCase) => ["opera"],
        { } id when id.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase) => ["vivaldi"],
        { } id when id.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || id.Contains("308046B0AF4A39CB", StringComparison.OrdinalIgnoreCase) => ["firefox"],
        { } id when id.Contains("Chromium", StringComparison.OrdinalIgnoreCase) => ["chromium"],
        { } id when id.Contains("Chrome", StringComparison.OrdinalIgnoreCase) => ["chrome"],
        _ => ["chrome", "msedge", "brave", "opera", "vivaldi", "firefox", "chromium"],
    };

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        var browsers = sessions.Where(session => IsBrowser(session.SourceAppUserModelId)).ToList();
        // The one playing, when another browser has something paused.
        return browsers.FirstOrDefault(session =>
                session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? browsers.FirstOrDefault();
    }

    private static bool IsBrowser(string? appId) =>
        appId is not null && Browsers.Any(browser => appId.Contains(browser, StringComparison.OrdinalIgnoreCase));
}
