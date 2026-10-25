using System.Net.Http;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// Pandora's web player (pandora.com) in Safari, Chrome, Edge or Brave: Lucent finds the Pandora tab and presses
/// the buttons in the player along its bottom, thumbs up and down included (see <see cref="BrowserTabRemote"/>).
/// </summary>
/// <remarks>
/// Pandora has no Mac app and no public API, so the web player is the only Pandora there is to control. A station
/// can't go back a song or be sought through (Pandora doesn't let it), and Pandora's free tier limits skips (thumbs
/// down skips too), so those buttons do nothing once Pandora says no. Windows reads and presses Pandora's own window
/// (its app, or a Pandora tab) through UI Automation, as screen readers do (see Windows' Streaming/PandoraRemote.cs).
/// </remarks>
public sealed class PandoraRemote : BrowserTabRemote, IRemoteRating
{
    // Run in the tab. The bar along the bottom, a button by Pandora's names for it (or its label, as those change
    // more rarely), whether a button is lit, the page's audio when it has one (it says exactly where the song's up
    // to, and seeks where Pandora lets it), and the song's position and length in seconds. The length can show as
    // what's left ("-1:23").
    private const string FindBar = """
        var f=document.querySelector('.region-bottomBar')||document.querySelector('.Tuner')||document.body;
        if(!f.querySelector('.Tuner__Audio__TrackDetail__title')&&!(meta&&meta.title))return 'none';
        var find=function(names,label){for(var i=0;i<names.length;i++){var x=f.querySelector(names[i]);if(x)return x;}
        var re=new RegExp(label,'i');return [].slice.call(f.querySelectorAll('button,[role="button"]')).filter(function(b){return re.test(b.getAttribute('aria-label')||b.getAttribute('title')||'');})[0]||null;};
        var lit=function(b){if(!b)return false;var a=b.getAttribute('aria-checked')||b.getAttribute('aria-pressed');if(a)return a==='true';return /(--active|--checked|--selected|isActive|isSelected)/.test(String(b.getAttribute('class')||''));};
        var press=function(b){if(b){b.click();return true;}return false;};
        var up=find(['[data-qa="thumbs_up_button"]','[data-qa="thumb_up_button"]','.ThumbUpButton','.Tuner__Control__ThumbUp__Button'],'^thumbs? ?up');
        var down=find(['[data-qa="thumbs_down_button"]','[data-qa="thumb_down_button"]','.ThumbDownButton','.Tuner__Control__ThumbDown__Button'],'^thumbs? ?down');
        var playButton=find(['[data-qa="play_button"]','[data-qa="pause_button"]','.Tuner__Control__Play__Button'],'^(play|pause)$');
        var v=[].slice.call(document.querySelectorAll('audio,video')).filter(function(x){return x.currentSrc&&isFinite(x.duration)&&x.duration>0;})[0];
        var playing=v?!v.paused:(!!f.querySelector('[data-qa="pause_button"]')||(playButton&&playButton.getAttribute('aria-checked')==='true')||(playButton&&/^pause$/i.test(playButton.getAttribute('aria-label')||'')));
        var at=v?v.currentTime:clock(text(f.querySelector('[data-qa="elapsed_time"]')));
        var left=text(f.querySelector('[data-qa="remaining_time"]'));
        var length=v?v.duration:(left.charAt(0)==='-'?at+clock(left):clock(left));
        """;

    // The song's title, artist, album and cover come from the browser's media controls, which Pandora tells, or the
    // bar. The title's link (the song's page) tells one song from the next. The cover is asked for at 500 pixels.
    // The thumbs are empty during an ad, which has none.
    private const string ReadBar = FindBar + """
        var ti=f.querySelector('.Tuner__Audio__TrackDetail__title');
        var title=(meta&&meta.title)||text(ti);
        if(!title)return 'none';
        var link=ti&&(ti.querySelector('a')||ti.closest('a'));
        var by=f.querySelector('.Tuner__Audio__TrackDetail__artist');
        var img=f.querySelector('.Tuner__Audio__TrackDetail__img img');
        var art=(metaArt||(img&&(img.currentSrc||img.src))||'').replace(/\d+W_\d+H/,'500W_500H');
        var thumb=up||down?(lit(up)?'up':lit(down)?'down':'none'):'';
        return out([playing?'playing':'paused',(link&&link.getAttribute('href'))||title,title,(meta&&meta.artist)||text(by),(meta&&meta.album)||'',length,at,art,'',v?(v.muted?0:v.volume):'',thumb]);
        """;

    public PandoraRemote(HttpClient http)
        : base(http)
    {
    }

    public override string Name => "Pandora";

    public override string ListLimit => "Pandora's stations pick each song as they go, and Pandora doesn't say what's next.";

    public override string PlaylistsLimit => "Pandora doesn't let other apps see your stations or playlists.";

    protected override string Website => "https://www.pandora.com";

    protected override string NotOpen => "Play a station in Pandora's web player (pandora.com)";

    // The title's link, the song's page on Pandora (/artist/…/TR…).
    protected override string? LinkFor(string id) =>
        id.StartsWith("/artist/", StringComparison.Ordinal) ? $"https://www.pandora.com{id}" : null;

    protected override string ReadPlayer => ReadBar;

    protected override string FindPlayer => FindBar;

    protected override string Play => "if(!playing&&!press(playButton)&&v)v.play();";

    protected override string Pause => "if(playing&&!press(playButton)&&v)v.pause();";

    protected override string Next => "press(find(['[data-qa=\"skip_button\"]','[data-qa=\"next_button\"]'],'^(skip|next)'));";

    // Pandora's replay (Premium) or previous button, when there is one; a station on the free tier has neither.
    protected override string Previous => "if(!press(find(['[data-qa=\"previous_button\"]','[data-qa=\"replay_button\"]'],'^(previous|replay)'))&&v)v.currentTime=0;";

    // Only the page's audio can seek, and only where Pandora lets it (playlists and albums, not stations).
    protected override string Seek(string seconds) => $"if(v)v.currentTime={seconds};";

    protected override string SetVolume(string volume) => $"if(v){{v.muted=false;v.volume={volume};}}";

    // Thumbs up, thumbs down, or neither: pressing a lit thumb takes it back, as on Pandora.
    public Task RateAsync(SongRating rating, CancellationToken cancel) => TellRatingAsync(rating switch
    {
        SongRating.Up => "if(!up)return 'none';if(!lit(up))up.click();",
        SongRating.Down => "if(!down)return 'none';if(!lit(down))down.click();",
        _ => "if(lit(up))up.click();else if(lit(down))down.click();",
    }, cancel);

    private async Task TellRatingAsync(string action, CancellationToken cancel)
    {
        if (await AskTabAsync(FindBar + action + "return 'ok';", cancel) != "ok")
        {
            throw new RemoteProblem("Pandora has no thumbs for this one (an ad, or the tab moved on).");
        }
    }

    protected override string Address(string url) => $"{url} contains \"pandora.com/\"";
}
