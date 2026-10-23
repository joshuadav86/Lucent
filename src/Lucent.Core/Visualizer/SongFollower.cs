using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Follows the song that's playing for the visuals, frame by frame: where it is in its beats, bars and
/// sections, how intense it is, and whether a drop is building or has just hit. A song mapped ahead of time
/// (see <see cref="BeatMap"/>) is followed from its map; anything else (radio, streams, other apps' sound) is
/// followed live, by ear (see <see cref="LiveBeatTracker"/>). Runs on the render thread; <see cref="Update"/>
/// once a frame, after the audio analyzer.
/// </summary>
public sealed class SongFollower
{
    private readonly LiveBeatTracker _live = new();
    private BeatMap? _song;
    private int _beatsBefore;
    private double _position;
    private float _intensity = 0.5f;

    /// <summary>True while the playing song's beats and sections are known.</summary>
    public bool Mapped { get; private set; }

    /// <summary>True while there's no map but a steady beat is being followed live.</summary>
    public bool Live { get; private set; }

    /// <summary>Where the song is now, with its intensity smoothed. Live, the phases are predicted and there are no sections.</summary>
    public SongMoment Moment { get; private set; }

    /// <summary>True when one of the song's beats went by since the last frame (mapped, or predicted live).</summary>
    public bool BeatNow { get; private set; }

    /// <summary>True on the frame a drop is heard live. Mapped drops are known ahead, so Auto plans for them instead.</summary>
    public bool DropNow { get; private set; }

    /// <summary>
    /// Moves on to this frame. <paramref name="song"/> is the playing song's map and position, if known;
    /// otherwise the music is followed live from <paramref name="analyzer"/>, already updated this frame.
    /// </summary>
    public void Update((BeatMap Beats, double Position)? song, float deltaTime, AudioAnalyzer analyzer)
    {
        // Kept listening even while a map is followed, so it's ready the moment there's none.
        _live.Update(analyzer.BassNow, analyzer.PunchNow, analyzer.LoudnessNow, deltaTime);
        DropNow = false;
        if (song is not { } playing)
        {
            Mapped = false;
            _song = null;
            Live = _live.Locked;
            BeatNow = _live.BeatNow;
            DropNow = _live.DropNow;
            var heard = _live.Moment;
            Ease(heard.Intensity, deltaTime, rise: 4f, fall: 1f);
            Moment = heard with { Intensity = _intensity };
            return;
        }

        Live = false;

        var beatsBefore = playing.Beats.BeatsBefore(playing.Position);

        // A beat counts if playing carried on past it; not after a seek, a new song, or a pause.
        var step = playing.Position - _position;
        BeatNow = _song == playing.Beats && beatsBefore > _beatsBefore && step > 0 && step < 0.5;
        _song = playing.Beats;
        _beatsBefore = beatsBefore;
        _position = playing.Position;
        Mapped = true;

        var moment = playing.Beats.At(playing.Position);

        // Into a more intense part at once; out of one over a second or so.
        Ease(moment.Intensity, deltaTime, rise: 12f, fall: 2f);
        Moment = moment with { Intensity = _intensity };
    }

    private void Ease(float target, float deltaTime, float rise, float fall)
    {
        var rate = target > _intensity ? rise : fall;
        _intensity += (target - _intensity) * (1f - MathF.Exp(-deltaTime * rate));
    }
}

/// <summary>
/// How strongly the screen pass shows the song's build-ups and drops, over whatever visual is up (see songMotion
/// in Engine.hlsl): the picture closing in and its colour turning through a build-up, and a shock wave on the drop.
/// </summary>
public static class SongLook
{
    /// <summary>The "Build-ups and drops" option.</summary>
    public static bool On { get; set; } = true;

    /// <summary>
    /// The build-up and drop to show this frame. A mapped song's are shown in full, because they were worked out
    /// ahead; music followed by ear at half strength, because a rising breakdown can look like a build-up there.
    /// </summary>
    public static (float BuildUp, float Drop) Of(SongFollower follower)
    {
        var strength = !On ? 0f : follower.Mapped ? 1f : follower.Live ? 0.5f : 0f;
        return (follower.Moment.BuildUp * strength, follower.Moment.Drop * strength);
    }
}

/// <summary>When Auto mode should next change visual, for a song whose bars and drops are known.</summary>
public static class AutoLanding
{
    /// <summary>How long a change of visual takes when it lands on a drop: a quick cut, not a slow blend.</summary>
    public const double DropCutSeconds = 0.5;

    /// <summary>
    /// When the next change should be fully in, in song seconds, and whether that's on a drop; or null when it
    /// isn't time to plan one yet. A drop wins when it comes after the visual has been up for at least half of
    /// <paramref name="interval"/> and at most one and a half times it; otherwise the change lands on a phrase
    /// start or downbeat once the interval is up. Quiet sections (intros, breakdowns) hold a visual a little longer.
    /// </summary>
    public static (double At, bool OnDrop)? Plan(BeatMap beats, double position, double waited, double interval, double lead)
    {
        if (beats.IntensityAt(position) < 0.35)
        {
            interval *= 1.5;
        }

        if (beats.NextDrop(position + Math.Max(DropCutSeconds, interval * 0.5 - waited)) is { } drop && waited + (drop - position) <= interval * 1.5)
        {
            return (drop, true);
        }

        if (waited + lead < interval)
        {
            return null;
        }

        return beats.LandingAfter(position + lead, preferPhraseWithin: 8) is { } landing ? (landing, false) : null;
    }

    /// <summary>
    /// For music followed live, with no map: whether Auto should change visual this frame, and if so whether it's
    /// a cut onto a drop (true) or an ordinary change (false); null to wait. A drop heard after the visual has been
    /// up for at least 40% of <paramref name="interval"/> gets a cut at once. Through a build-up Auto holds on (up to
    /// one and a half intervals) for the drop. Otherwise, once the interval is up, it changes on a downbeat when the
    /// beat is followed, or on a strong <paramref name="beat"/> pulse when it isn't, and regardless 8 seconds later.
    /// </summary>
    public static bool? Heard(SongFollower follower, double waited, double interval, float beat)
    {
        if (follower.DropNow && waited >= interval * 0.4)
        {
            return true;
        }

        if (follower.Moment.BuildUp > 0.3f && waited < interval * 1.5)
        {
            return null;
        }

        var onTheBeat = follower.Live ? follower.BeatNow && follower.Moment.BarPhase < 0.2f : beat > 0.9f;
        return (waited > interval && onTheBeat) || waited > interval + 8 ? false : null;
    }
}
