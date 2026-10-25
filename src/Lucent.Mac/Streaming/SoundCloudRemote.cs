using System.Net.Http;

namespace Lucent.Mac.Streaming;

/// <summary>
/// SoundCloud playing in Safari, Chrome, Edge or Brave: Lucent finds the SoundCloud tab and presses the
/// buttons in the player bar along its bottom (see <see cref="BrowserTabRemote"/>).
/// </summary>
/// <remarks>
/// SoundCloud keeps its audio out of the page, so the song and where it's up to are read from the player
/// bar (and the browser's media controls, which SoundCloud tells the song and cover), and seeking clicks the
/// bar's timeline where the position is. Shuffle and repeat are the bar's own buttons. Its "Next up" list
/// only exists inside SoundCloud's page while it's open there, so it isn't read. Windows has no SoundCloud of its own:
/// its browser item controls SoundCloud like any site, through Windows' media controls.
/// </remarks>
public sealed class SoundCloudRemote : BrowserTabRemote
{
    // Run in the tab. The player bar, its play button (which has "playing" on it while it plays), its timeline,
    // and the song's position and length in seconds. The length shows as what's left ("-1:23") when that's clicked.
    private const string FindBar = """
        var c=document.querySelector('.playControls');if(!c)return 'none';
        var b=c.querySelector('.playControl');if(!b)return 'none';
        var playing=b.classList.contains('playing');
        var at=clock(text(c.querySelector('.playbackTimeline__timePassed > span:nth-child(2)')));
        var left=text(c.querySelector('.playbackTimeline__duration > span:nth-child(2)'));
        var length=left.charAt(0)==='-'?at+clock(left):clock(left);
        var sh=c.querySelector('.shuffleControl'),rp=c.querySelector('.repeatControl');
        """;

    // The song's title and artist come from the browser's media controls, or the player bar's badge. Its
    // address tells one song from the next. The cover is the badge's small one, asked for at 500 pixels.
    private const string ReadBar = FindBar + """
        var link=c.querySelector('a.playbackSoundBadge__titleLink');
        var title=(meta&&meta.title)||(link&&(link.getAttribute('title')||text(link.querySelector('span:nth-child(2)'))||text(link)))||'';
        if(!title)return 'none';
        var by=c.querySelector('.playbackSoundBadge__titleContextContainer > a')||c.querySelector('.playbackSoundBadge__lightLink');
        var art=metaArt;
        if(!art){var s=c.querySelector('span.sc-artwork');var m=s&&/url\(["']?([^"')]+)/.exec(s.style.backgroundImage);art=m?m[1]:'';}
        art=art.replace(/-t\d+x\d+\./,'-t500x500.');
        return out([playing?'playing':'paused',(link&&link.getAttribute('href'))||title,title,(meta&&meta.artist)||text(by),(meta&&meta.album)||'',length,at,art,'','','',sh?(sh.classList.contains('m-shuffling')?'true':'false'):'',rp?repeatOf(rp):'']);
        """;

    public SoundCloudRemote(HttpClient http)
        : base(http)
    {
    }

    public override string Name => "SoundCloud";

    protected override string Website => "https://soundcloud.com";

    protected override string NotOpen => "Play something on SoundCloud";

    // The badge's link, /artist/song, without what follows it (?in=a playlist).
    protected override string? LinkFor(string id) =>
        id.StartsWith('/') && id.Split('?')[0] is var page && page.Count(c => c == '/') >= 2 ? $"https://soundcloud.com{page}" : null;

    protected override string ReadPlayer => ReadBar;

    protected override string FindPlayer => FindBar;

    protected override string Play => "if(!playing)b.click();";

    protected override string Pause => "if(playing)b.click();";

    protected override string Next => "var x=c.querySelector('.skipControl__next');if(x)x.click();";

    // SoundCloud's own back button goes to the start of the song, or to the one before when it's only just started.
    protected override string Previous => "var x=c.querySelector('.skipControl__previous');if(x)x.click();";

    // A click on the timeline where the position is, as the timeline listens for the mouse.
    protected override string SetShuffle(string on) => $"if(!sh)return 'none';if(sh.classList.contains('m-shuffling')!=={on})sh.click();";

    // The button goes round SoundCloud's repeat modes, so it's pressed until it shows the one wanted.
    protected override string SetRepeat(string mode) => $"if(!rp)return 'none';for(var i=0;i<3&&repeatOf(rp)!=={mode};i++)rp.click();";

    public override string ListLimit => "SoundCloud keeps its Next up list inside its own page, so Lucent can't show it here.";

    public override string PlaylistsLimit => "SoundCloud isn't letting new apps in, so Lucent can't see or add to your playlists.";

    protected override string Seek(string seconds) => $$"""
        var w=c.querySelector('.playbackTimeline__progressWrapper');
        if(w&&length>0){var r=w.getBoundingClientRect();var x=r.left+r.width*Math.min(1,{{seconds}}/length),y=r.top+r.height/2;
        ['mousedown','mousemove','mouseup','click'].forEach(function(k){w.dispatchEvent(new MouseEvent(k,{bubbles:true,cancelable:true,clientX:x,clientY:y,button:0,view:window}));});}
        """;

    // Any SoundCloud page: the player bar stays along the bottom while the user looks around the site.
    protected override string Address(string url) => $"{url} contains \"soundcloud.com\"";
}
