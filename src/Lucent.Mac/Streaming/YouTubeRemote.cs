using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// YouTube (or YouTube Music) playing in Safari, Chrome, Edge or Brave, through AppleScript: Lucent finds
/// the YouTube tab and runs a little JavaScript in it to read the video (title, channel, cover, where it's
/// up to) and to play, pause, skip and seek it. The browser has to allow that ("Allow JavaScript from
/// Apple Events"), and macOS asks once whether Lucent may control the browser (Privacy &amp; Security,
/// Automation), with the reason in Info.plist's NSAppleEventsUsageDescription.
/// </summary>
/// <remarks>
/// Windows reads browsers through Windows' media controls instead (see Windows' Streaming/YouTubeRemote.cs).
/// macOS has nothing like those that other apps may press, so the Mac goes to the tab itself. Only
/// running browsers are asked, so Lucent never opens one by itself.
/// </remarks>
public sealed class YouTubeRemote : IRemotePlayer, IRemoteLists
{
    private const string Osascript = "/usr/bin/osascript";

    /// <summary>The browsers Lucent looks in: the name AppleScript knows each by, which is its process's name too.</summary>
    private static readonly string[] Browsers = ["Safari", "Google Chrome", "Microsoft Edge", "Brave Browser"];

    // Run in the tab. The YouTube player's video, or the page's only one on YouTube Music; null when there's none.
    private const string FindVideo = "var v=document.querySelector('#movie_player video')||document.querySelector('video');if(!v)return 'none';";

    // Fields between tabs: state, the video's ID, title, channel, album (YouTube Music), length, position
    // (in seconds), the cover's address and the playlist's ID (empty when it isn't playing through one). YouTube tells the browser's media controls the title, channel
    // and cover, so they're read from there, with the page's title to fall back on.
    private const string ReadVideo = FindVideo + """
        var m=navigator.mediaSession&&navigator.mediaSession.metadata;
        var art=m&&m.artwork&&m.artwork.length?m.artwork[m.artwork.length-1].src:'';
        var id=new URLSearchParams(location.search).get('v')||location.pathname;
        var title=(m&&m.title)||document.title.replace(/ - YouTube( Music)?$/,'');
        var list=new URLSearchParams(location.search).get('list')||'';
        var f=[v.paused?'paused':'playing',id,title,(m&&m.artist)||'',(m&&m.album)||'',isFinite(v.duration)?v.duration:0,v.currentTime,art,list];
        var t=String.fromCharCode(9),n=String.fromCharCode(10);
        return f.map(function(x){return String(x).split(t).join(' ').split(n).join(' ');}).join(t);
        """;

    // Run in the tab. The playlist panel beside a video playing through a playlist (the one on screen, as a narrow
    // window has a second one under the video), or null when there's none.
    private const string FindPlaylist = """
        var p=[].slice.call(document.querySelectorAll('ytd-playlist-panel-renderer')).filter(function(x){return x.querySelector('ytd-playlist-panel-video-renderer');});
        p=p.filter(function(x){return x.offsetParent!==null;})[0]||p[0];
        if(!p)return 'none';
        var rows=[].slice.call(p.querySelectorAll('ytd-playlist-panel-video-renderer'));
        var idOf=function(r){var a=r.querySelector('a#wc-endpoint')||r.querySelector('a[href]');return a?[a,new URL(a.href,location.href).searchParams.get('v')]:[null,null];};
        """;

    // Lines: the playlist's name, then a line a video: its ID, title, channel and length (as YouTube shows it, 3:45), between tabs.
    private const string ReadPlaylist = FindPlaylist + """
        var t=String.fromCharCode(9),n=String.fromCharCode(10);
        var clean=function(x){return String(x||'').trim().split(t).join(' ').split(n).join(' ');};
        var h=p.querySelector('.title')||p.querySelector('h3');
        var out=[clean(h&&h.textContent)];
        rows.forEach(function(r){var id=idOf(r)[1];if(!id)return;var ti=r.querySelector('#video-title');var by=r.querySelector('#byline');var d=r.querySelector('ytd-thumbnail-overlay-time-status-renderer');
        out.push([id,clean(ti&&(ti.getAttribute('title')||ti.textContent)),clean(by&&by.textContent),clean(d&&d.textContent)].join(t));});
        return out.join(n);
        """;

    private readonly HttpClient _http;

    // The tab last shown, so pausing it and playing it again stays with it while another tab plays.
    private Tab? _tab;

    public YouTubeRemote(HttpClient http) => _http = http;

    public string Name => "YouTube";

    /// <summary>
    /// The bundle IDs the playing browser's sound comes from (its helpers' IDs start the same way; Safari's sound
    /// comes from WebKit's), so the visuals can listen to just that browser. Empty until a YouTube tab is found.
    /// </summary>
    public IReadOnlyList<string> BundlePrefixes => _tab?.Browser switch
    {
        "Safari" => ["com.apple.Safari", "com.apple.WebKit"],
        "Google Chrome" => ["com.google.Chrome"],
        "Microsoft Edge" => ["com.microsoft.edgemac"],
        "Brave Browser" => ["com.brave.Browser"],
        _ => [],
    };

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var found = new List<(Tab Tab, string[] Video)>();
        string? problem = null;
        foreach (var browser in RunningBrowsers())
        {
            string output;
            try
            {
                output = await RunAsync(browser, ReadScript(browser), cancel);
            }
            catch (RemoteProblem ex)
            {
                // Another browser may still have YouTube in it.
                problem ??= ex.Message;
                continue;
            }

            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.TrimEnd('\r').Split('\t');
                if (fields[0] == "nojs")
                {
                    problem ??= AllowJavaScript(browser);
                }
                else if (fields.Length >= 10)
                {
                    found.Add((new Tab(browser, fields[0], fields[1]), fields[2..]));
                }
            }
        }

        var readAt = DateTime.UtcNow;
        if (found.Count == 0)
        {
            _tab = null;
            throw new RemoteProblem(problem ?? "Play a YouTube video in Safari, Chrome, Edge or Brave, then Lucent can show and control it.");
        }

        // The tab playing, then the one shown last, then the first one there is.
        var pick = found.FindIndex(each => each.Video[0] == "playing");
        if (pick < 0)
        {
            pick = Math.Max(0, found.FindIndex(each => each.Tab == _tab));
        }

        var (tab, video) = found[pick];
        _tab = tab;

        var track = new RemoteTrack($"{tab.Browser}|{video[1]}", video[2], video[3], video[4], Seconds(video[5]),
            video[7].StartsWith("http", StringComparison.Ordinal) ? video[7] : null);
        var playlist = video.Length > 8 && video[8].Length > 0 ? $"{tab.Browser}|{video[8]}" : null;
        return new RemoteState(track, video[0] == "playing", Seconds(video[6]), readAt, playlist);
    }

    // A playlist's videos, from the panel YouTube shows beside the video. YouTube Music's queue isn't read.
    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel)
    {
        if (state.ListId is null || _tab is not { } tab)
        {
            return null;
        }

        var result = await AskTabAsync(ReadPlaylist, cancel);
        return result == "none" ? null : ParsePlaylist(state.ListId, tab.Browser, result);
    }

    // Clicking the video in the panel, as the user would, plays it and carries on through the playlist.
    public async Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel)
    {
        var video = track.Id[(track.Id.IndexOf('|') + 1)..];
        var result = await AskTabAsync(FindPlaylist
            + $"var r=rows.filter(function(r){{return idOf(r)[1]==={Json(video)};}})[0];if(!r)return 'none';idOf(r)[0].click();return 'ok';", cancel);
        if (result != "ok")
        {
            throw new RemoteProblem("That video isn't in the YouTube tab's playlist any more.");
        }
    }

    /// <summary>The playlist <see cref="ReadPlaylist"/> prints, in <paramref name="browser"/>, for <paramref name="id"/>.</summary>
    internal static RemoteList ParsePlaylist(string id, string browser, string text)
    {
        var lines = text.Split('\n');
        var tracks = lines.Skip(1)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new RemoteTrack($"{browser}|{fields[0]}", fields[1], fields[2], string.Empty, Clock(fields[3])))
            .ToList();
        return new RemoteList(id, lines[0].Length > 0 ? lines[0] : "Playlist", tracks);
    }

    // "3:45" or "1:02:03", as YouTube shows a video's length.
    private static TimeSpan Clock(string text)
    {
        var seconds = 0.0;
        foreach (var part in text.Trim().Split(':'))
        {
            if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return TimeSpan.Zero;
            }

            seconds = seconds * 60 + value;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    // A JavaScript string. A video ID is only letters, digits, - and _, but nothing else can get through either.
    private static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);

    public Task PlayAsync(CancellationToken cancel) => TellAsync("v.play();", cancel);

    public Task PauseAsync(CancellationToken cancel) => TellAsync("v.pause();", cancel);

    // YouTube's next button (or YouTube Music's) plays what's next in the playlist or Up next.
    public Task NextAsync(CancellationToken cancel) =>
        TellAsync("var b=document.querySelector('.ytp-next-button')||document.querySelector('.next-button');if(b)b.click();", cancel);

    // Back to the start, like any player, or to the video before when it's only just started and there is one.
    public Task PreviousAsync(CancellationToken cancel) =>
        TellAsync("var b=document.querySelector('.ytp-prev-button')||document.querySelector('.previous-button');"
            + "if(v.currentTime>3||!b||b.offsetParent===null){v.currentTime=0;}else{b.click();}", cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        TellAsync($"v.currentTime={Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)};", cancel);

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) =>
        track.ArtworkUrl is { } url ? await _http.GetByteArrayAsync(url, cancel) : null;

    /// <summary>Lists each YouTube tab's window, tab and video, a line each, or "nojs" when the browser won't run JavaScript for Lucent.</summary>
    internal static string ReadScript(string browser)
    {
        var run = IsSafari(browser) ? $"do JavaScript {Quote(Wrap(ReadVideo))} in t" : $"execute t javascript {Quote(Wrap(ReadVideo))}";
        var tabKey = IsSafari(browser) ? "i" : "(id of t)";
        // Inside a browser's tell block "tab" means its tabs, so the tab character is kept from outside.
        return $$"""
            set found to ""
            set sep to tab
            tell application "{{browser}}"
                repeat with w in windows
                    set i to 0
                    repeat with t in tabs of w
                        set i to i + 1
                        set u to URL of t
                        if u is not missing value and ({{YouTubeAddress("u")}}) then
                            try
                                set r to {{run}}
                                if r is not missing value and r is not "none" then set found to found & (id of w) & sep & {{tabKey}} & sep & r & linefeed
                            on error errorText
                                if errorText contains "JavaScript" then return "nojs"
                            end try
                        end if
                    end repeat
                end repeat
            end tell
            return found
            """;
    }

    private async Task TellAsync(string action, CancellationToken cancel)
    {
        if (await AskTabAsync(FindVideo + action + "return 'ok';", cancel) != "ok")
        {
            _tab = null;
            throw new RemoteProblem("That YouTube tab has closed or moved on. Lucent will find the next one that plays.");
        }
    }

    // Runs the JavaScript in the tab last shown and returns what it returns ("gone" when the tab has).
    private async Task<string> AskTabAsync(string script, CancellationToken cancel)
    {
        if (_tab is not { } tab || !RunningBrowsers().Contains(tab.Browser))
        {
            throw new RemoteProblem("Play a YouTube video in Safari, Chrome, Edge or Brave, then Lucent can control it.");
        }

        var js = Quote(Wrap(script));
        var apple = IsSafari(tab.Browser)
            ? $$"""
                tell application "Safari"
                    try
                        return do JavaScript {{js}} in tab {{tab.Key}} of window id {{tab.Window}}
                    on error errorText
                        if errorText contains "JavaScript" then return "nojs"
                        return "gone"
                    end try
                end tell
                """
            : $$"""
                tell application "{{tab.Browser}}"
                    try
                        repeat with t in tabs of window id {{tab.Window}}
                            if ((id of t) as string) is "{{tab.Key}}" then return execute t javascript {{js}}
                        end repeat
                    on error errorText
                        if errorText contains "JavaScript" then return "nojs"
                    end try
                end tell
                return "gone"
                """;
        var result = await RunAsync(tab.Browser, apple, cancel);
        return result == "nojs" ? throw new RemoteProblem(AllowJavaScript(tab.Browser)) : result;
    }

    private static string AllowJavaScript(string browser) => IsSafari(browser)
        ? "Safari needs to let Lucent reach YouTube: in Safari's Settings, Advanced, turn on \"Show features for web developers\", then in Developer, turn on \"Allow JavaScript from Apple Events\"."
        : $"{browser} needs to let Lucent reach YouTube: in its menu bar, choose View, Developer, Allow JavaScript from Apple Events.";

    // YouTube's watch, Shorts and live pages, and YouTube Music. Not the home page, whose previews play too.
    private static string YouTubeAddress(string url) =>
        $"{url} contains \"youtube.com/watch\" or {url} contains \"youtube.com/shorts/\" or {url} contains \"youtube.com/live/\" or {url} contains \"music.youtube.com/\"";

    private static bool IsSafari(string browser) => browser == "Safari";

    // A function, so the scripts can stop early with return.
    private static string Wrap(string js) => $"(function(){{{js.ReplaceLineEndings(string.Empty)}}})()";

    /// <summary>The text as an AppleScript string, quotes and backslashes escaped.</summary>
    internal static string Quote(string text) => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    private static IEnumerable<string> RunningBrowsers() =>
        Browsers.Where(browser =>
        {
            var processes = Process.GetProcessesByName(browser);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            return processes.Length > 0;
        });

    // JavaScript writes numbers with a point whatever the Mac's region.
    private static TimeSpan Seconds(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    private static async Task<string> RunAsync(string browser, string script, CancellationToken cancel)
    {
        // With no script file, osascript reads the script from its input.
        var start = new ProcessStartInfo(Osascript)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new RemoteProblem($"Lucent couldn't run AppleScript to reach {browser}.");
        try
        {
            await process.StandardInput.WriteAsync(script);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            if (process.ExitCode != 0)
            {
                var message = await error;
                // -1743: the user said no (or hasn't been asked yet) in Privacy & Security, Automation.
                throw new RemoteProblem(message.Contains("-1743", StringComparison.Ordinal)
                    ? $"Lucent isn't allowed to control {browser}. Turn it on in System Settings, Privacy & Security, Automation."
                    : $"{browser} didn't answer just now.");
            }

            return (await output).TrimEnd('\n', '\r');
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }

            throw;
        }
    }

    public void Dispose()
    {
        // Nothing to let go of: each request is its own osascript run.
    }

    /// <summary>A YouTube tab: its browser, its window's ID, and the tab's ID (its number in the window, in Safari).</summary>
    private sealed record Tab(string Browser, string Window, string Key);
}
