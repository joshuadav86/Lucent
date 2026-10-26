using Lucent.Audio;

namespace Lucent.Visualizer;

/// <summary>
/// Follows music that can't be listened to ahead of time (online radio, streams, other apps' sound) as it plays:
/// its tempo and where the next beat and bar will fall, how intense it is, a build-up as it rises, and a drop the
/// moment one hits. It hears only what has already played, so it predicts beats and bars from the last few seconds
/// rather than knowing the song's sections. Runs on the render thread; <see cref="Update"/> once a frame.
/// </summary>
public sealed class LiveBeatTracker
{
    // The sound is measured in short slices, this many a second.
    private const int SlicesPerSecond = 100;

    // How many seconds of onsets the tempo is worked out from, and how much of that the beat's timing.
    private const int TempoSeconds = 8;
    private const int PhaseSeconds = 6;

    // Loudness is also kept in quarter-second blocks, for three minutes, to judge intensity, build-ups and drops.
    private const double BlockSeconds = 0.25;
    private const int Blocks = 720;

    // A drop: the bass comes in at least this much harder (0.12 is 7 dB) than over the few seconds before,
    // which weren't intense, and not within this many seconds of the last one.
    private const float DropBass = 0.12f;
    private const double DropGap = 8;

    // Blocks heard before a drop can be told apart from the music simply starting (30 seconds).
    private const int DropHistory = 120;

    private readonly double[] _onsets = new double[TempoSeconds * SlicesPerSecond];
    private readonly float[] _blockBass = new float[Blocks];
    private readonly float[] _blockLoud = new float[Blocks];
    private readonly double[] _barChange = new double[4];
    private long _slices;
    private double _owed;
    private float _lastPunch;
    private double _period;
    private double _anchor;
    private double _sinceTempo;
    private long _beatsCounted;
    private long _lastBeat = long.MinValue;
    private double _sinceBeat;
    private double _beatBass, _beatLoud;
    private int _beatSlices;
    private readonly double[] _recentBeats = new double[4];
    private long _blocks;
    private double _blockSumBass, _blockSumLoud;
    private int _blockSlices;
    private float _fastBass, _fastLoud;
    private double _sinceDrop = DropGap;
    private float _buildUp;
    private long _spreadAt = -1;
    private (float Bottom, float Range) _spread, _bassSpread;
    private double _silent;

    /// <summary>True while a steady beat is being followed.</summary>
    public bool Locked { get; private set; }

    /// <summary>Beats per minute, while <see cref="Locked"/>.</summary>
    public double Tempo => _period > 0 ? 60.0 * SlicesPerSecond / _period : 0;

    /// <summary>True when a predicted beat fell this frame (only while <see cref="Locked"/>).</summary>
    public bool BeatNow { get; private set; }

    /// <summary>True on the frame a drop is heard.</summary>
    public bool DropNow { get; private set; }

    /// <summary>Where the music is now. <see cref="SongMoment.SectionPhase"/> stays 0: sections aren't known live.</summary>
    public SongMoment Moment { get; private set; }

    /// <summary>
    /// Takes this frame's bass, punch and loudness (each 0..1 across 60 dB, unsmoothed; see <see cref="AudioAnalyzer.BassNow"/>,
    /// <see cref="AudioAnalyzer.PunchNow"/> and <see cref="AudioAnalyzer.LoudnessNow"/>) and moves on <paramref name="deltaTime"/> seconds.
    /// Beats are found from jumps in the punch, which drum hits make; intensity and drops from the bass and loudness.
    /// </summary>
    public void Update(float bass, float punch, float loud, float deltaTime)
    {
        BeatNow = false;
        DropNow = false;
        if (deltaTime > 0.5f)
        {
            // A long stall (a pause or a hitch): what came before no longer lines up.
            Reset();
            return;
        }

        // Silence: nothing to follow, and what comes after it may be another song.
        _silent = loud < 0.1f ? _silent + deltaTime : 0;
        if (_silent > 2)
        {
            Reset();
            Moment = default;
            return;
        }

        _owed += deltaTime * SlicesPerSecond;
        while (_owed >= 1)
        {
            _owed -= 1;
            AddSlice(bass, punch, loud);
        }

        _sinceTempo += deltaTime;
        if (_sinceTempo >= 0.5 && _slices >= 6 * SlicesPerSecond)
        {
            _sinceTempo = 0;
            FindTempo();
        }

        var now = (double)_slices / SlicesPerSecond;
        double beatPhase = 0, barPhase = 0;
        if (Locked)
        {
            var periodSeconds = _period / SlicesPerSecond;
            var beats = (now - _anchor) / periodSeconds;
            var beat = (long)Math.Floor(beats);
            beatPhase = beats - beat;
            _sinceBeat += deltaTime;
            if (beat > _lastBeat)
            {
                // A new beat (not straight after locking on). Every beat is counted, so the bars stay in step, but
                // one the timing has just been nudged onto, right after the last, isn't sounded again.
                if (_lastBeat != long.MinValue && beat - _lastBeat < 8)
                {
                    _beatsCounted += beat - _lastBeat - 1;
                    EndBeat();
                    if (_sinceBeat >= 0.5 * periodSeconds)
                    {
                        BeatNow = true;
                        _sinceBeat = 0;
                    }
                }

                _lastBeat = beat;
            }

            var one = Enumerable.Range(0, 4).MaxBy(offset => _barChange[offset]);
            barPhase = (((_beatsCounted - one) % 4 + 4) % 4 + beatPhase) / 4;
        }

        // The bass and loudness of the last tenth of a second or so, against the few seconds before.
        var fast = 1f - MathF.Exp(-deltaTime / 0.08f);
        _fastBass += (bass - _fastBass) * fast;
        _fastLoud += (loud - _fastLoud) * fast;
        _sinceDrop += deltaTime;

        var intensity = 0.5f;
        if (_blocks >= 16)
        {
            if (_spreadAt != _blocks)
            {
                // Only worth working out again once a block has been added.
                _spreadAt = _blocks;
                _spread = Spread(bassOnly: false);
                _bassSpread = Spread(bassOnly: true);
            }

            var (bottom, range) = _spread;
            var (bassBottom, bassRange) = _bassSpread;
            float Level(long from, long to) => (float)Math.Clamp((Mean(_blockBass, from, to) * 0.5 + Mean(_blockLoud, from, to) * 0.5 - bottom) / range, 0, 1);
            intensity = Level(_blocks - 8, _blocks);

            // A drop: the bass jumps well above the seconds before (from 8 s to 0.75 s ago), which were calmer, and
            // the music is now about as intense as it's been.
            var before = Mean(_blockBass, _blocks - 32, _blocks - 3);
            var levelNow = Math.Clamp((0.5f * (_fastBass + _fastLoud) - bottom) / range, 0f, 1f);
            if (_blocks >= DropHistory && _sinceDrop >= DropGap && _fastBass - before >= DropBass && levelNow >= 0.8f
                && _fastLoud - Mean(_blockLoud, _blocks - 32, _blocks - 3) >= 0.05f && Level(_blocks - 32, _blocks - 3) <= 0.55f)
            {
                DropNow = true;
                _sinceDrop = 0;
                _buildUp = 0;
            }

            // A build-up: getting steadily louder over the last eight seconds while the bass holds back.
            var rise = Mean(_blockLoud, _blocks - 8, _blocks) - Mean(_blockLoud, _blocks - 32, _blocks - 24);
            var bassBack = (Mean(_blockBass, _blocks - 8, _blocks) - bassBottom) / bassRange < 0.35;
            var target = _blocks >= 32 && bassBack && _sinceDrop > 2 ? Math.Clamp((rise - 0.025f) / 0.05f, 0f, 1f) : 0f;
            _buildUp += (target - _buildUp) * (1f - MathF.Exp(-deltaTime * 2f));
        }

        var dropPulse = _sinceDrop < 4 ? (float)Math.Exp(-1.5 * _sinceDrop) : 0f;
        Moment = new SongMoment((float)beatPhase, (float)barPhase, intensity, _buildUp, dropPulse, 0);
    }

    private void Reset()
    {
        _slices = 0;
        _owed = 0;
        _period = 0;
        _blocks = 0;
        _spreadAt = -1;
        _blockSlices = 0;
        _blockSumBass = _blockSumLoud = 0;
        _beatSlices = 0;
        _beatBass = _beatLoud = 0;
        _lastBeat = long.MinValue;
        Array.Clear(_barChange);
        _sinceDrop = DropGap;
        _buildUp = 0;
        _silent = 0;
        Locked = false;
    }

    private void AddSlice(float bass, float punch, float loud)
    {
        var at = (int)(_slices % _onsets.Length);
        _onsets[at] = _slices == 0 ? 0 : Math.Max(0, punch - _lastPunch);
        _lastPunch = punch;
        _slices++;

        _beatBass += bass;
        _beatLoud += loud;
        _beatSlices++;

        _blockSumBass += bass;
        _blockSumLoud += loud;
        if (++_blockSlices >= BlockSeconds * SlicesPerSecond)
        {
            var block = (int)(_blocks % Blocks);
            _blockBass[block] = (float)(_blockSumBass / _blockSlices);
            _blockLoud[block] = (float)(_blockSumLoud / _blockSlices);
            _blocks++;
            _blockSumBass = _blockSumLoud = 0;
            _blockSlices = 0;
        }
    }

    /// <summary>
    /// The time between beats: the spacing at which the last few seconds of onsets line up with themselves best,
    /// leaning towards tempos people tap along to, kept unless another is clearly better. Then the beat's timing:
    /// where in that spacing the onsets of the last few seconds bunch up.
    /// </summary>
    private void FindTempo()
    {
        var count = (int)Math.Min(_slices, _onsets.Length);
        var onsets = new double[count];
        for (var i = 0; i < count; i++)
        {
            onsets[i] = _onsets[(int)((_slices - count + i) % _onsets.Length)];
        }

        var mean = onsets.Average();
        double self = 0;
        foreach (var onset in onsets)
        {
            self += (onset - mean) * (onset - mean);
        }

        var shortest = 60 * SlicesPerSecond / 200;
        var longest = 60 * SlicesPerSecond / 60;
        var match = new double[longest + 2];
        for (var lag = shortest - 1; lag <= longest + 1; lag++)
        {
            double sum = 0;
            for (var f = lag; f < count; f++)
            {
                sum += (onsets[f] - mean) * (onsets[f - lag] - mean);
            }

            match[lag] = sum;
        }

        double Score(int lag)
        {
            var octaves = Math.Log2(60.0 * SlicesPerSecond / lag / 120);
            return match[lag] * Math.Exp(-0.5 * octaves * octaves);
        }

        var best = Enumerable.Range(shortest, longest - shortest + 1).MaxBy(Score);
        if (self <= 0 || match[best] / self < 0.08)
        {
            Locked = false;
            return;
        }

        // Between whole slices: fit a curve through the best spacing and its neighbours.
        double before = match[best - 1], at = match[best], after = match[best + 1];
        var bend = before - 2 * at + after;
        var found = best + (bend < 0 ? Math.Clamp(0.5 * (before - after) / bend, -0.5, 0.5) : 0);

        if (_period > 0 && Locked)
        {
            var held = (int)Math.Clamp(Math.Round(_period), shortest, longest);
            if (Math.Abs(found - _period) <= 0.04 * _period)
            {
                found = 0.8 * _period + 0.2 * found;
            }
            else if (match[held] / self >= 0.08 && (Score(best) < 1.3 * Score(held) || Related(found / _period)))
            {
                // The tempo held still fits. Stick with it over a clearly better one, but never jump to a half, a
                // double, or two or three times as fast or slow: that's the same music heard another way (a snare
                // roll in a build-up, say), not a new tempo.
                found = _period;
            }
        }

        // Every onset of the last few seconds folded into one beat; the busiest spot is where the beats fall.
        var bins = new double[(int)Math.Ceiling(found)];
        var from = Math.Max(0, count - PhaseSeconds * SlicesPerSecond);
        for (var f = from; f < count; f++)
        {
            var absolute = _slices - count + f;
            bins[(int)(absolute % found) % bins.Length] += onsets[f];
        }

        var peak = Enumerable.Range(0, bins.Length)
            .MaxBy(b => bins[b] + 0.5 * (bins[(b + 1) % bins.Length] + bins[(b + bins.Length - 1) % bins.Length]));
        var measured = (peak + 0.5) / SlicesPerSecond;
        var periodSeconds = found / SlicesPerSecond;
        if (!Locked || _period <= 0)
        {
            // Locking on afresh: the count of beats starts again, so which beat starts the bar is learnt again.
            _anchor = measured;
            _lastBeat = long.MinValue;
            Array.Clear(_barChange);
        }
        else
        {
            // Keep the beat the timing is counted from recent, so a change of tempo doesn't shift every beat since.
            var now = (double)_slices / SlicesPerSecond;
            var oldPeriod = _period / SlicesPerSecond;
            var whole = (long)Math.Floor((now - _anchor) / oldPeriod);
            _anchor += whole * oldPeriod;
            _lastBeat -= whole;

            // Ease the timing towards what was measured, so the beat doesn't jump (or lose count of the bar)
            // when a change in the music throws one measurement off.
            var error = measured - _anchor;
            error -= Math.Round(error / periodSeconds) * periodSeconds;
            _anchor += 0.3 * error;
        }

        _period = found;
        Locked = true;
    }

    private static bool Related(double ratio) =>
        new[] { 0.5, 2, 2.0 / 3, 1.5, 1.0 / 3, 3, 0.25, 4 }.Any(simple => Math.Abs(ratio / simple - 1) <= 0.04);

    /// <summary>Closes the beat just gone: its loudness counts towards which beat of four starts the bar.</summary>
    private void EndBeat()
    {
        var ended = _beatsCounted++;
        _recentBeats[ended % 4] = _beatSlices == 0 ? 0 : (_beatBass + _beatLoud) / _beatSlices;
        _beatBass = _beatLoud = 0;
        _beatSlices = 0;
        if (ended < 3)
        {
            return;
        }

        // Music changes on the one: how much the two beats up to this one differ from the two before them.
        double Level(long beat) => _recentBeats[beat % 4];
        var change = Math.Abs(Level(ended) + Level(ended - 1) - Level(ended - 2) - Level(ended - 3));
        for (var i = 0; i < 4; i++)
        {
            _barChange[i] *= 0.97;
        }

        _barChange[(ended - 1) % 4] += change * change;
    }

    /// <summary>
    /// Where the quietest tenth of the last three minutes sits, and the span up to the loudest tenth (at least 6 dB): of the
    /// bass and loudness together, or of the bass alone.
    /// </summary>
    private (float Bottom, float Range) Spread(bool bassOnly)
    {
        var count = (int)Math.Min(_blocks, Blocks);
        var levels = new float[count];
        for (var i = 0; i < count; i++)
        {
            var block = (int)((_blocks - count + i) % Blocks);
            levels[i] = bassOnly ? _blockBass[block] : 0.5f * (_blockBass[block] + _blockLoud[block]);
        }

        Array.Sort(levels);
        var bottom = levels[(int)(0.1 * (count - 1))];
        var top = levels[(int)Math.Ceiling(0.9 * (count - 1))];
        return (bottom, Math.Max(top - bottom, 0.1f));
    }

    /// <summary>The average of the blocks from <paramref name="from"/> up to <paramref name="to"/>, of those still kept.</summary>
    private float Mean(float[] values, long from, long to)
    {
        from = Math.Max(Math.Max(0, from), _blocks - Blocks);
        to = Math.Min(to, _blocks);
        if (to <= from)
        {
            return 0;
        }

        double sum = 0;
        for (var b = from; b < to; b++)
        {
            sum += values[(int)(b % Blocks)];
        }

        return (float)(sum / (to - from));
    }
}
