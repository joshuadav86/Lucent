using System.Globalization;
using System.Net.Http;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// YouTube (or YouTube Music) playing in Safari, Chrome, Edge or Brave: Lucent finds the YouTube tab and
/// runs a little JavaScript in it to read the video (title, channel, cover, where it's up to) and to play,
/// pause, skip and seek it (see <see cref="BrowserTabRemote"/>).
/// </summary>
public sealed class YouTubeRemote : BrowserTabRemote, IRemoteLists
{
    // Run in the tab. The YouTube player's video, or the page's only one on YouTube Music; 'none' when there's none.
    private const string FindVideo = "var v=document.querySelector('#movie_player video')||document.querySelector('video');if(!v)return 'none';";

    // YouTube tells the browser's media controls the title, channel and cover, so they're read from there,
    // with the page's title to fall back on.
    private const string ReadVideo = FindVideo + """
        var id=new URLSearchParams(location.search).get('v')||location.pathname;
        var title=(meta&&meta.title)||document.title.replace(/ - YouTube( Music)?$/,'');
        var list=new URLSearchParams(location.search).get('list')||'';
        return out([v.paused?'paused':'playing',id,title,(meta&&meta.artist)||'',(meta&&meta.album)||'',isFinite(v.duration)?v.duration:0,v.currentTime,metaArt,list]);
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
        var clean=function(x){return String(x||'').trim().split(t).join(' ').split(n).join(' ');};
        var h=p.querySelector('.title')||p.querySelector('h3');
        var lines=[clean(h&&h.textContent)];
        rows.forEach(function(r){var id=idOf(r)[1];if(!id)return;var ti=r.querySelector('#video-title');var by=r.querySelector('#byline');var d=r.querySelector('ytd-thumbnail-overlay-time-status-renderer');
        lines.push([id,clean(ti&&(ti.getAttribute('title')||ti.textContent)),clean(by&&by.textContent),clean(d&&d.textContent)].join(t));});
        return lines.join(n);
        """;

    public YouTubeRemote(HttpClient http)
        : base(http)
    {
    }

    public override string Name => "YouTube";

    protected override string NotOpen => "Play a YouTube video";

    // A playlist's videos, from the panel YouTube shows beside the video. YouTube Music's queue isn't read.
    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel)
    {
        if (state.ListId is null || Browser is not { } browser)
        {
            return null;
        }

        var result = await AskTabAsync(ReadPlaylist, cancel);
        return result == "none" ? null : ParsePlaylist(state.ListId, browser, result);
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

    protected override string ReadPlayer => ReadVideo;

    protected override string FindPlayer => FindVideo;

    protected override string Play => "v.play();";

    protected override string Pause => "v.pause();";

    // YouTube's next button (or YouTube Music's) plays what's next in the playlist or Up next.
    protected override string Next => "var b=document.querySelector('.ytp-next-button')||document.querySelector('.next-button');if(b)b.click();";

    // Back to the start, like any player, or to the video before when it's only just started and there is one.
    protected override string Previous => "var b=document.querySelector('.ytp-prev-button')||document.querySelector('.previous-button');"
        + "if(v.currentTime>3||!b||b.offsetParent===null){v.currentTime=0;}else{b.click();}";

    protected override string Seek(string seconds) => $"v.currentTime={seconds};";

    // YouTube's watch, Shorts and live pages, and YouTube Music. Not the home page, whose previews play too.
    protected override string Address(string url) =>
        $"{url} contains \"youtube.com/watch\" or {url} contains \"youtube.com/shorts/\" or {url} contains \"youtube.com/live/\" or {url} contains \"music.youtube.com/\"";
}
