using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// A music site playing in Safari, Chrome, Edge or Brave, through AppleScript: Lucent finds the site's tab
/// and runs a little JavaScript in it to read what's playing (title, artist, cover, where it's up to) and
/// to play, pause, skip and seek it. The browser has to allow that ("Allow JavaScript from Apple Events"),
/// and macOS asks once whether Lucent may control the browser (Privacy &amp; Security, Automation), with
/// the reason in Info.plist's NSAppleEventsUsageDescription. <see cref="YouTubeRemote"/>,
/// <see cref="TidalRemote"/> and <see cref="SoundCloudRemote"/> say which tabs are theirs and how to read
/// and press their players.
/// </summary>
/// <remarks>
/// Windows reads browsers through Windows' media controls instead (see Windows' Streaming/YouTubeRemote.cs).
/// macOS has nothing like those that other apps may press, so the Mac goes to the tab itself. Only
/// running browsers are asked, so Lucent never opens one by itself.
/// </remarks>
public abstract class BrowserTabRemote : IRemotePlayer, IRemoteVolume, IRemoteLinks, IRemoteModes, IRemoteOpener
{
    private const string Osascript = "/usr/bin/osascript";

    /// <summary>The browsers Lucent looks in: the name AppleScript knows each by, which is its process's name too.</summary>
    private static readonly string[] Browsers = ["Safari", "Google Chrome", "Microsoft Edge", "Brave Browser"];

    /// <summary>
    /// JavaScript the read scripts can use: <c>out(fields)</c> joins fields with tabs (taking tabs and line breaks
    /// out of each), <c>clock(text)</c> reads "3:45" or "1:02:03" as seconds, and <c>text(element)</c> is an
    /// element's trimmed text, or empty when there's no element. <c>lit(button)</c> is whether a toggle button is on (by
    /// its aria-pressed or aria-checked, or a class saying it's active), and <c>repeatOf(button)</c> reads a repeat
    /// button's label and look as 'off', 'all' or 'one'.
    /// </summary>
    protected const string Helpers = """
        var t=String.fromCharCode(9),n=String.fromCharCode(10);
        var out=function(f){return f.map(function(x){return String(x==null?'':x).split(t).join(' ').split(n).join(' ').trim();}).join(t);};
        var clock=function(s){var r=0,p=String(s||'').trim().replace(/^-/,'').split(':');for(var i=0;i<p.length;i++){var x=parseInt(p[i],10);if(isNaN(x))return 0;r=r*60+x;}return r;};
        var text=function(e){return e?e.textContent.trim():'';};
        var lit=function(b){if(!b)return false;var a=b.getAttribute('aria-checked')||b.getAttribute('aria-pressed');if(a)return a==='true';return /(--active|--checked|--selected|isActive|isSelected|is-active|m-active|m-shuffling)/.test(String(b.getAttribute('class')||''));};
        var repeatOf=function(b){if(!b)return '';var s=[b.getAttribute('aria-label'),b.getAttribute('title'),b.getAttribute('data-type'),b.getAttribute('class')].join(' ').toLowerCase();
        if(/(m-one|single|repeat ?one|repeat ?track|repeatone|repeat_one)/.test(s))return 'one';if(/(m-all|repeat ?all|repeatall|repeat_all)/.test(s))return 'all';if(/(m-none|repeat ?off)/.test(s))return 'off';return lit(b)?'all':'off';};
        var meta=navigator.mediaSession&&navigator.mediaSession.metadata;
        var metaArt=meta&&meta.artwork&&meta.artwork.length?meta.artwork[meta.artwork.length-1].src:'';
        """;

    private readonly HttpClient _http;

    // The tab last shown, so pausing it and playing it again stays with it while another tab plays.
    private Tab? _tab;

    // False when the last read found none of the site's tabs in any browser (rather than a browser that wouldn't say).
    private bool _found = true;

    protected BrowserTabRemote(HttpClient http) => _http = http;

    public abstract string Name { get; }

    /// <summary>What to tell the user when no tab is playing the site, such as "Play a YouTube video".</summary>
    protected abstract string NotOpen { get; }

    /// <summary>The site's web player, opened by the player's Open button when no tab has it, such as "https://www.youtube.com".</summary>
    protected abstract string Website { get; }

    public bool IsOpen => _found;

    /// <summary>Opens the site in a browser Lucent can reach that's already running (Safari, say), or else the default one.</summary>
    public Task OpenAsync(CancellationToken cancel) => AppOpener.OpenWebsiteAsync(Website, RunningBrowsers().FirstOrDefault(), cancel);

    /// <summary>An AppleScript condition that's true when the address in <paramref name="url"/> is one of the site's player pages.</summary>
    protected abstract string Address(string url);

    /// <summary>
    /// JavaScript that returns 'none' when the tab has no player, and otherwise <c>out</c> of these fields: the state
    /// ('playing' or 'paused'), an ID that tells one song from the next, its title, artist, album, length and position
    /// (in seconds), the cover's address, the ID of the playlist it's playing through (empty when there isn't one), and
    /// the player's volume from 0 to 1 (empty when <see cref="SetVolume"/> can't change it), and, for a site with thumbs,
    /// the song's thumb ('up', 'down' or 'none'; left off, or empty, when the site has none), then whether it shuffles
    /// ('true' or 'false') and what it repeats ('off', 'all' or 'one'), each left off or empty when Lucent can't change
    /// it there (see <see cref="SetShuffle"/>, <see cref="SetRepeat"/>). It runs after <see cref="Helpers"/>.
    /// </summary>
    protected abstract string ReadPlayer { get; }

    /// <summary>JavaScript that finds the site's player and returns 'none' when there isn't one, run before each button's script.</summary>
    protected abstract string FindPlayer { get; }

    protected abstract string Play { get; }

    protected abstract string Pause { get; }

    protected abstract string Next { get; }

    protected abstract string Previous { get; }

    /// <summary>JavaScript that moves the player to <paramref name="seconds"/>, written with a point.</summary>
    protected abstract string Seek(string seconds);

    /// <summary>JavaScript that sets the player's volume to <paramref name="volume"/> (0 to 1, written with a point), or null when it can't.</summary>
    protected virtual string? SetVolume(string volume) => null;

    /// <summary>JavaScript that turns the player's shuffle on (<paramref name="on"/> is 'true') or off, or null when it can't.</summary>
    protected virtual string? SetShuffle(string on) => null;

    /// <summary>
    /// JavaScript that sets what the player repeats to <paramref name="mode"/>, a JavaScript string ('off', 'all' or
    /// 'one', quotes and all), or null when it can't.
    /// </summary>
    protected virtual string? SetRepeat(string mode) => null;

    /// <summary>False when the site can only repeat the song or video playing, as <paramref name="state"/> plays.</summary>
    public virtual bool RepeatsAll(RemoteState state) => true;

    /// <summary>Why the queue window can't list what the site plays next, or null when it can (see <see cref="IRemotePlayer.ListLimit"/>).</summary>
    public virtual string? ListLimit => null;

    /// <summary>Why the site's playlists can't be listed or added to (see <see cref="IRemotePlayer.PlaylistsLimit"/>).</summary>
    public virtual string? PlaylistsLimit => null;

    /// <summary>
    /// The bundle IDs the playing browser's sound comes from (its helpers' IDs start the same way; Safari's sound
    /// comes from WebKit's), so the visuals can listen to just that browser. Empty until the site's tab is found.
    /// </summary>
    public IReadOnlyList<string> BundlePrefixes => _tab?.Browser switch
    {
        "Safari" => ["com.apple.Safari", "com.apple.WebKit"],
        "Google Chrome" => ["com.google.Chrome"],
        "Microsoft Edge" => ["com.microsoft.edgemac"],
        "Brave Browser" => ["com.brave.Browser"],
        _ => [],
    };

    /// <summary>The browser the tab last shown is in, or null before one is found.</summary>
    protected string? Browser => _tab?.Browser;

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var found = new List<(Tab Tab, string[] Song)>();
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
                // Another browser may still have the site in it.
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
        _found = found.Count > 0 || problem is not null;
        if (found.Count == 0)
        {
            _tab = null;
            throw new RemoteProblem(problem ?? $"{NotOpen} in Safari, Chrome, Edge or Brave, then Lucent can show and control it.");
        }

        // The tab playing, then the one shown last, then the first one there is.
        var pick = found.FindIndex(each => each.Song[0] == "playing");
        if (pick < 0)
        {
            pick = Math.Max(0, found.FindIndex(each => each.Tab == _tab));
        }

        var (tab, song) = found[pick];
        _tab = tab;

        var track = new RemoteTrack($"{tab.Browser}|{song[1]}", song[2], song[3], song[4], Seconds(song[5]),
            song[7].StartsWith("http", StringComparison.Ordinal) ? song[7] : null);
        var playlist = song.Length > 8 && song[8].Length > 0 ? $"{tab.Browser}|{song[8]}" : null;
        double? volume = song.Length > 9 && double.TryParse(song[9], NumberStyles.Float, CultureInfo.InvariantCulture, out var level) && double.IsFinite(level)
            ? Math.Clamp(level, 0, 1)
            : null;
        SongRating? rating = song.Length > 10 ? song[10] switch
        {
            "up" => SongRating.Up,
            "down" => SongRating.Down,
            "none" => SongRating.None,
            _ => null,
        } : null;
        bool? shuffle = song.Length > 11 && SetShuffle("true") is not null ? song[11] switch { "true" => true, "false" => false, _ => null } : null;
        RepeatMode? repeat = song.Length > 12 && SetRepeat("'off'") is not null
            ? song[12] switch { "off" => RepeatMode.Off, "all" => RepeatMode.All, "one" => RepeatMode.One, _ => null }
            : null;
        return new RemoteState(track, song[0] == "playing", Seconds(song[6]), readAt, playlist, Volume: volume, Shuffle: shuffle, Repeat: repeat,
            Rating: rating);
    }

    public Task PlayAsync(CancellationToken cancel) => TellAsync(Play, cancel);

    public Task PauseAsync(CancellationToken cancel) => TellAsync(Pause, cancel);

    public Task NextAsync(CancellationToken cancel) => TellAsync(Next, cancel);

    public Task PreviousAsync(CancellationToken cancel) => TellAsync(Previous, cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        TellAsync(Seek(Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)), cancel);

    public Task SetVolumeAsync(double volume, CancellationToken cancel) =>
        SetVolume(Math.Clamp(volume, 0, 1).ToString("0.###", CultureInfo.InvariantCulture)) is { } script ? TellAsync(script, cancel) : Task.CompletedTask;

    public Task SetShuffleAsync(bool on, CancellationToken cancel) =>
        SetShuffle(on ? "true" : "false") is { } script ? TellAsync(script, cancel) : throw new NotSupportedException();

    public Task SetRepeatAsync(RepeatMode mode, CancellationToken cancel) =>
        SetRepeat(mode switch { RepeatMode.One => "'one'", RepeatMode.All => "'all'", _ => "'off'" }) is { } script
            ? TellAsync(script, cancel)
            : throw new NotSupportedException();

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) =>
        track.ArtworkUrl is { } url ? await _http.GetByteArrayAsync(url, cancel) : null;

    // A song's ID is its browser, then what the site calls it (a video's ID, a song page's path).
    public string? WebLink(RemoteTrack track) => track.Id.IndexOf('|') is var bar and >= 0 ? LinkFor(track.Id[(bar + 1)..]) : null;

    /// <summary>The web link of the song the site calls <paramref name="id"/> (a video's ID, a song page's path), or null.</summary>
    protected virtual string? LinkFor(string id) => null;

    /// <summary>Lists each of the site's tabs' window, tab and song, a line each, or "nojs" when the browser won't run JavaScript for Lucent.</summary>
    internal string ReadScript(string browser)
    {
        var js = Quote(Wrap(Helpers + ReadPlayer));
        var run = IsSafari(browser) ? $"do JavaScript {js} in t" : $"execute t javascript {js}";
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
                        if u is not missing value and ({{Address("u")}}) then
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
        if (await AskTabAsync(FindPlayer + action + "return 'ok';", cancel) != "ok")
        {
            _tab = null;
            throw new RemoteProblem($"That {Name} tab has closed or moved on. Lucent will find the next one that plays.");
        }
    }

    /// <summary>Runs the JavaScript (after <see cref="Helpers"/>) in the tab last shown and returns what it returns ("gone" when the tab has).</summary>
    protected async Task<string> AskTabAsync(string script, CancellationToken cancel)
    {
        if (_tab is not { } tab || !RunningBrowsers().Contains(tab.Browser))
        {
            throw new RemoteProblem($"{NotOpen} in Safari, Chrome, Edge or Brave, then Lucent can control it.");
        }

        var js = Quote(Wrap(Helpers + script));
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

    private string AllowJavaScript(string browser) => IsSafari(browser)
        ? $"Safari needs to let Lucent reach {Name}: in Safari's Settings, Advanced, turn on \"Show features for web developers\", then in Developer, turn on \"Allow JavaScript from Apple Events\"."
        : $"{browser} needs to let Lucent reach {Name}: in its menu bar, choose View, Developer, Allow JavaScript from Apple Events.";

    private static bool IsSafari(string browser) => browser == "Safari";

    // A function, so the scripts can stop early with return.
    private static string Wrap(string js) => $"(function(){{{js.ReplaceLineEndings(string.Empty)}}})()";

    /// <summary>The text as an AppleScript string, quotes and backslashes escaped.</summary>
    internal static string Quote(string text) => $"\"{text.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>A JavaScript string, so nothing in <paramref name="text"/> can get out of it.</summary>
    protected static string Json(string text) => System.Text.Json.JsonSerializer.Serialize(text);

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

    /// <summary>A tab: its browser, its window's ID, and the tab's ID (its number in the window, in Safari).</summary>
    private sealed record Tab(string Browser, string Window, string Key);
}
