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
    // YouTube tells the browser's media controls the title, channel and cover, so they're read from there,
    // with the page's title to fall back on. YouTube Music's queue counts as its playlist (named "music:"), as it
    // always has one. Shuffle is the playlist panel's button (YouTube Music's only shuffles its queue once, so it
    // isn't offered), and repeat is YouTube Music's button, or the video looping on YouTube.
    private const string ReadVideo = FindVideo + FindModes + """
        var id=new URLSearchParams(location.search).get('v')||location.pathname;
        var title=(meta&&meta.title)||document.title.replace(/ - YouTube( Music)?$/,'');
        var list=new URLSearchParams(location.search).get('list')||'';
        if(ym)list='music:'+(list||'queue');
        var mp=document.getElementById('movie_player');
        var volume=mp&&mp.getVolume?(mp.isMuted&&mp.isMuted()?0:mp.getVolume()/100):(v.muted?0:v.volume);
        return out([v.paused?'paused':'playing',id,title,(meta&&meta.artist)||'',(meta&&meta.album)||'',isFinite(v.duration)?v.duration:0,v.currentTime,metaArt,list,volume,'',sh?String(lit(sh)):'',repeat()]);
        """;

    // Run in the tab, after FindVideo. YouTube Music's player bar (ym) and repeat button, the playlist panel's shuffle
    // button on YouTube (sh, null when not playing through a playlist), and repeat() as 'off', 'all' or 'one'.
    private const string FindModes = """
        var ym=document.querySelector('ytmusic-player-bar');
        var yr=ym&&(ym.querySelector('.repeat')||ym.querySelector('[aria-label*="epeat"]'));
        var sh=null;
        if(!ym){var pp=[].slice.call(document.querySelectorAll('ytd-playlist-panel-renderer')).filter(function(x){return x.offsetParent!==null&&x.querySelector('ytd-playlist-panel-video-renderer');})[0];
        sh=pp?[].slice.call(pp.querySelectorAll('button')).filter(function(b){return /shuffle/i.test(b.getAttribute('aria-label')||'');})[0]||null:null;}
        var repeat=function(){if(!ym)return v.loop?'one':'off';var m=String(ym.getAttribute('repeat-mode')||ym.getAttribute('repeat-mode_')||'').toUpperCase();
        if(m==='ONE')return 'one';if(m==='ALL')return 'all';if(m==='NONE')return 'off';return yr?repeatOf(yr):'';};
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

    // Run in the tab. YouTube Music's queue beside its player, or null when it isn't YouTube Music; a song's ID is
    // its video's, from YouTube Music's own data for the row, or its place in the queue when that isn't there.
    private const string FindQueue = """
        var q=document.querySelector('ytmusic-player-queue');
        var qrows=q?[].slice.call(q.querySelectorAll('ytmusic-player-queue-item')):[];
        var qid=function(r,i){var d=r.data||(r.__data&&r.__data.data)||{};return d.videoId||('#'+i);};
        """;

    // Lines: the playlist's name, then a line a video: its ID, title, channel and length (as YouTube shows it, 3:45),
    // between tabs. YouTube Music's queue, or the playlist panel beside a YouTube video.
    private const string ReadPlaylist = FindQueue + """
        var clean=function(x){return String(x||'').trim().split(t).join(' ').split(n).join(' ');};
        var attr=function(e){return clean(e&&(e.getAttribute('title')||e.textContent));};
        if(qrows.length){var ql=['Up next'];qrows.forEach(function(r,i){ql.push([qid(r,i),attr(r.querySelector('.song-title')),attr(r.querySelector('.byline')),attr(r.querySelector('.duration'))].join(t));});return ql.join(n);}
        """ + FindPlaylist + """
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

    // A video's ID is 11 letters, digits, dashes and underscores; anything else is a page without one.
    protected override string? LinkFor(string id) =>
        id.Length == 11 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? $"https://www.youtube.com/watch?v={id}" : null;

    // A playlist's videos, from the panel YouTube shows beside the video, or YouTube Music's queue. The queue is read
    // again with each song, as YouTube Music adds to it (its radio and autoplay) while it plays.
    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel)
    {
        if (state.ListId is null || Browser is not { } browser)
        {
            return null;
        }

        var result = await AskTabAsync(ReadPlaylist, cancel);
        return result == "none" ? null : ParsePlaylist(state.ListId, browser, result, followsSong: IsMusic(state));
    }

    // Clicking the video in the panel (or the song's play button in YouTube Music's queue), as the user would, plays
    // it and carries on through the playlist.
    public async Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel)
    {
        var video = Json(track.Id[(track.Id.IndexOf('|') + 1)..]);
        var result = await AskTabAsync(FindQueue
            + $"if(qrows.length){{var qr=qrows.filter(function(r,i){{return qid(r,i)==={video};}})[0];if(!qr)return 'none';"
            + "(qr.querySelector('ytmusic-play-button-renderer')||qr.querySelector('.song-title')||qr).click();return 'ok';}"
            + FindPlaylist
            + $"var r=rows.filter(function(r){{return idOf(r)[1]==={video};}})[0];if(!r)return 'none';idOf(r)[0].click();return 'ok';", cancel);
        if (result != "ok")
        {
            throw new RemoteProblem("That video isn't in the YouTube tab's playlist any more.");
        }
    }

    // YouTube Music's queue, which ReadVideo names "music:".
    private static bool IsMusic(RemoteState state) => state.ListId?.Contains("|music:", StringComparison.Ordinal) == true;

    // YouTube Music's repeat button goes round all three; YouTube loops just the video.
    public override bool RepeatsAll(RemoteState state) => IsMusic(state);

    public override string PlaylistsLimit => "YouTube only shares playlists with apps Google has approved, which Lucent isn't yet.";

    /// <summary>The playlist <see cref="ReadPlaylist"/> prints, in <paramref name="browser"/>, for <paramref name="id"/>.</summary>
    internal static RemoteList ParsePlaylist(string id, string browser, string text, bool followsSong = false)
    {
        var lines = text.Split('\n');
        var tracks = lines.Skip(1)
            .Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(fields => fields.Length >= 4)
            .Select(fields => new RemoteTrack($"{browser}|{fields[0]}", fields[1], fields[2], string.Empty, Clock(fields[3])))
            .ToList();
        return new RemoteList(id, lines[0].Length > 0 ? lines[0] : "Playlist", tracks, FollowsSong: followsSong);
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

    protected override string FindPlayer => FindVideo + FindModes;

    protected override string Play => "v.play();";

    protected override string Pause => "v.pause();";

    // YouTube's next button (or YouTube Music's) plays what's next in the playlist or Up next.
    protected override string Next => "var b=document.querySelector('.ytp-next-button')||document.querySelector('.next-button');if(b)b.click();";

    // Back to the start, like any player, or to the video before when it's only just started and there is one.
    protected override string Previous => "var b=document.querySelector('.ytp-prev-button')||document.querySelector('.previous-button');"
        + "if(v.currentTime>3||!b||b.offsetParent===null){v.currentTime=0;}else{b.click();}";

    protected override string Seek(string seconds) => $"v.currentTime={seconds};";

    protected override string SetShuffle(string on) => $"if(!sh)return 'none';if(lit(sh)!=={on})sh.click();";

    // YouTube Music's button goes round its repeat modes, so it's pressed until it shows the one wanted. YouTube
    // loops the video itself, as its right-click Loop does.
    protected override string SetRepeat(string mode) =>
        $"if(ym){{if(!yr)return 'none';for(var i=0;i<3&&repeat()!=={mode};i++)yr.click();}}else{{v.loop={mode}==='one';}}";

    // YouTube's player keeps its own volume (and puts it back on the video), so it's set there, with the video's for YouTube Music.
    protected override string SetVolume(string volume) =>
        $"var mp=document.getElementById('movie_player');if(mp&&mp.setVolume){{if(mp.unMute)mp.unMute();mp.setVolume(Math.round({volume}*100));}}else{{v.muted=false;v.volume={volume};}}";

    // YouTube's watch, Shorts and live pages, and YouTube Music. Not the home page, whose previews play too.
    protected override string Address(string url) =>
        $"{url} contains \"youtube.com/watch\" or {url} contains \"youtube.com/shorts/\" or {url} contains \"youtube.com/live/\" or {url} contains \"music.youtube.com/\"";
}
