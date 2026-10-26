using Windows.Media.Control;

namespace Lucent.Streaming;

/// <summary>
/// Whatever a web browser is playing (YouTube or YouTube Music, SoundCloud, TIDAL's web player, any site),
/// through Windows' media controls. Chrome, Edge, Firefox and the other big browsers tell Windows what a
/// page is playing, with the site giving the song's or video's title, artist or channel and cover, and
/// take its play, pause, skip and seek buttons. Nothing to sign in to and nothing to turn on in the browser.
/// </summary>
/// <remarks>
/// Windows only says which browser is playing, not which site, so this is whatever the browser's media
/// controls show. A browser shows the tab that played last. The Mac has no media controls for other apps
/// to press, so it reaches the site's tab itself, with a menu item for each site it knows (see the Mac's
/// Streaming/YouTubeRemote.cs, Streaming/SoundCloudRemote.cs and Streaming/TidalRemote.cs).
/// </remarks>
public sealed class YouTubeRemote : MediaControlsRemote
{
    // How each browser names itself to Windows: "Chrome" (and "Chrome._crx_…" for an installed YouTube
    // Music app), "MSEdge", "Brave…", "Opera…", "Vivaldi…", and Firefox by its install hash.
    private static readonly string[] Browsers = ["Chrome", "MSEdge", "Edge", "Brave", "Opera", "Vivaldi", "Firefox", "308046B0AF4A39CB", "Chromium"];

    // The browser playing, once one has been read.
    public override string Name => ProcessNames switch
    {
        ["msedge"] => "Edge",
        ["chrome"] => "Chrome",
        ["brave"] => "Brave",
        ["opera"] => "Opera",
        ["vivaldi"] => "Vivaldi",
        ["firefox"] => "Firefox",
        ["chromium"] => "Chromium",
        _ => "your browser",
    };

    public override string ListLimit => "Windows only says what your browser is playing, not what the site plays next, so Lucent can't show it here.";

    public override string PlaylistsLimit => "Windows can't show Lucent your playlists on websites like YouTube and SoundCloud.";

    protected override string NotOpen => "Play YouTube, SoundCloud or another site in your web browser";

    // The browser playing, as its process is named; every browser Lucent knows until one has been read.
    public override IReadOnlyList<string> ProcessNames => BrowserProcessNames(SourceAppId);

    /// <summary>The process of the browser Windows names <paramref name="appId"/>, or every browser Lucent knows for null.</summary>
    internal static IReadOnlyList<string> BrowserProcessNames(string? appId) => appId switch
    {
        { } id when id.Contains("MSEdge", StringComparison.OrdinalIgnoreCase) || id.Contains("Edge", StringComparison.OrdinalIgnoreCase) => ["msedge"],
        { } id when id.Contains("Brave", StringComparison.OrdinalIgnoreCase) => ["brave"],
        { } id when id.Contains("Opera", StringComparison.OrdinalIgnoreCase) => ["opera"],
        { } id when id.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase) => ["vivaldi"],
        { } id when id.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || id.Contains("308046B0AF4A39CB", StringComparison.OrdinalIgnoreCase) => ["firefox"],
        { } id when id.Contains("Chromium", StringComparison.OrdinalIgnoreCase) => ["chromium"],
        { } id when id.Contains("Chrome", StringComparison.OrdinalIgnoreCase) => ["chrome"],
        _ => AllBrowsers,
    };

    /// <summary>Every browser Lucent knows, as their processes are named.</summary>
    internal static IReadOnlyList<string> AllBrowsers { get; } = ["chrome", "msedge", "brave", "opera", "vivaldi", "firefox", "chromium"];

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        var browsers = sessions.Where(session => IsBrowser(session.SourceAppUserModelId)).ToList();
        // The one playing, when another browser has something paused.
        return browsers.FirstOrDefault(session =>
                session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? browsers.FirstOrDefault();
    }

    internal static bool IsBrowser(string? appId) =>
        appId is not null && Browsers.Any(browser => appId.Contains(browser, StringComparison.OrdinalIgnoreCase));
}
