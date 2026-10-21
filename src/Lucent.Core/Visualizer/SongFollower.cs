using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Follows the song that's playing for the visuals, frame by frame: where it is in its beats, bars and
/// sections, how intense it is, and whether a drop is building or has just hit. Runs on the render thread;
/// <see cref="Update"/> once a frame, before the audio analyzer.
/// </summary>
public sealed class SongFollower
{
    private BeatMap? _song;
    private int _beatsBefore;
    private double _position;
    private float _intensity = 0.5f;

    /// <summary>True while the playing song's beats and sections are known.</summary>
    public bool Mapped { get; private set; }

    /// <summary>Where the song is now, with its intensity smoothed. Zeros (and a guessed intensity) while it isn't mapped.</summary>
    public SongMoment Moment { get; private set; }

    /// <summary>True when one of the song's beats went by since the last frame.</summary>
    public bool BeatNow { get; private set; }

    /// <summary>
    /// Moves on to this frame. <paramref name="song"/> is the playing song's map and position, if known;
    /// <paramref name="level"/> is the live loudness, which stands in for intensity when it isn't.
    /// </summary>
    public void Update((BeatMap Beats, double Position)? song, float deltaTime, float level)
    {
        if (song is not { } playing)
        {
            Mapped = false;
            BeatNow = false;
            _song = null;
            Ease(Math.Clamp(level * 1.5f, 0f, 1f), deltaTime, rise: 1f, fall: 0.5f);
            Moment = new SongMoment(0, 0, _intensity, 0, 0, 0);
            return;
        }

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
}
