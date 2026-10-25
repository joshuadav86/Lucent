using System.Net.Http;

namespace Lucent.Mac.Streaming;

/// <summary>
/// TIDAL's web player (tidal.com, which was listen.tidal.com) in Safari, Chrome, Edge or Brave: Lucent
/// finds the TIDAL tab and presses the buttons in the player along its bottom (see <see cref="BrowserTabRemote"/>).
/// </summary>
/// <remarks>
/// TIDAL's Mac app has no AppleScript and nothing else other apps may use to control it, so the Mac
/// controls the web player. Windows controls the TIDAL app through Windows' media controls (see Windows'
/// Streaming/TidalRemote.cs). TIDAL's play queue isn't read.
/// </remarks>
public sealed class TidalRemote : BrowserTabRemote
{
    // Run in the tab. The player along the bottom, the page's audio or video element when it has one (it says
    // exactly where the song's up to, and seeks), and the song's position and length in seconds.
    private const string FindFooter = """
        var f=document.querySelector('#footerPlayer')||document.querySelector('[data-test="footer-player"]');if(!f)return 'none';
        var v=[].slice.call(document.querySelectorAll('video,audio')).filter(function(x){return x.currentSrc&&isFinite(x.duration)&&x.duration>0;})[0];
        var press=function(name){var x=f.querySelector('button[data-test="'+name+'"]');if(x){x.click();return true;}return false;};
        var playing=v?!v.paused:!!f.querySelector('button[data-test="pause"]');
        var at=v?v.currentTime:clock(text(f.querySelector('time[data-test="current-time"]')));
        var length=v?v.duration:clock(text(f.querySelector('time[data-test="duration"]')));
        var sh=f.querySelector('button[data-test="shuffle"]'),rp=f.querySelector('button[data-test="repeat"]');
        """;

    // The song's title, artist, album and cover come from the browser's media controls, which TIDAL tells,
    // or the player. The title's link (with the track's ID) tells one song from the next. The cover is asked
    // for at 640 pixels, which TIDAL has for every cover.
    private const string ReadFooter = FindFooter + """
        var ti=f.querySelector('[data-test="footer-track-title"]');
        var title=(meta&&meta.title)||text(ti);
        if(!title)return 'none';
        var link=ti&&(ti.querySelector('a')||ti.closest('a'));
        var by=[].slice.call(f.querySelectorAll('a[data-test="grid-item-detail-text-title-artist"]')).map(text).join(', ');
        var album=[].slice.call(f.querySelectorAll('a[href^="/album/"]')).filter(function(a){return !ti||!ti.contains(a);})[0];
        var img=f.querySelector('figure[data-test="current-media-imagery"] img')||f.querySelector('img');
        var art=(metaArt||(img&&(img.currentSrc||img.src))||'').replace(/\/\d+x\d+\.jpg$/,'/640x640.jpg');
        return out([playing?'playing':'paused',(link&&link.getAttribute('href'))||title,title,(meta&&meta.artist)||by,(meta&&meta.album)||text(album),length,at,art,'',v?(v.muted?0:v.volume):'','',sh?String(lit(sh)):'',rp?repeatOf(rp):'']);
        """;

    public TidalRemote(HttpClient http)
        : base(http)
    {
    }

    public override string Name => "TIDAL";

    protected override string NotOpen => "Play something in TIDAL's web player (tidal.com)";

    // The title's link, /track/123 (or /album/45/track/123).
    protected override string? LinkFor(string id) =>
        System.Text.RegularExpressions.Regex.Match(id, @"/track/(\d+)") is { Success: true } track
            ? $"https://tidal.com/browse/track/{track.Groups[1].Value}"
            : null;

    protected override string ReadPlayer => ReadFooter;

    protected override string FindPlayer => FindFooter;

    protected override string Play => "if(!playing&&!press('play')&&v)v.play();";

    protected override string Pause => "if(playing&&!press('pause')&&v)v.pause();";

    protected override string Next => "press('next');";

    protected override string Previous => "if(!press('previous')&&v)v.currentTime=0;";

    // Only the page's audio or video element can seek; without one, the song carries on where it is.
    protected override string Seek(string seconds) => $"if(v)v.currentTime={seconds};";

    protected override string SetShuffle(string on) => $"if(!sh)return 'none';if(lit(sh)!=={on})sh.click();";

    // The button goes round TIDAL's repeat modes, so it's pressed until it shows the one wanted.
    protected override string SetRepeat(string mode) => $"if(!rp)return 'none';for(var i=0;i<3&&repeatOf(rp)!=={mode};i++)rp.click();";

    public override string ListLimit => "TIDAL's web player keeps its play queue inside its own page, so Lucent can't show it here.";

    public override string PlaylistsLimit => "TIDAL only shares playlists with apps it has approved, which Lucent isn't yet.";

    protected override string SetVolume(string volume) => $"if(v){{v.muted=false;v.volume={volume};}}";

    // The web player, at tidal.com now and listen.tidal.com before.
    protected override string Address(string url) => $"{url} contains \"://tidal.com/\" or {url} contains \"listen.tidal.com\"";
}
