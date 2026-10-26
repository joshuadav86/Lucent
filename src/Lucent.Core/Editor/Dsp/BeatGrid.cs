using System.Globalization;
using Lucent.Audio;

namespace Lucent.Editor.Dsp;

/// <summary>
/// The beats and bars of a sound in the editor, to the sample: its tempo, where every beat falls, and
/// which beats start a bar. The editor draws them over the waveform, snaps selections to them, and
/// measures loops in bars and beats with them. Music is taken to be in 4/4.
/// </summary>
public sealed class BeatGrid
{
    /// <summary>Beats in a bar. Almost all music made to loop is in 4/4.</summary>
    public const int BeatsPerBar = 4;

    // Tracked beats that stray less than this from a straight line (on average) mean the music keeps
    // one tempo, and the grid is drawn at exactly that tempo; more, and it follows the beats found.
    private const double SteadySeconds = 0.012;

    // How far from a grid line a drum hit can be and still count as that beat, when lining the grid up.
    private const double AlignSeconds = 0.035;

    private readonly double[] _beats;
    private readonly int _firstDownbeat;

    private BeatGrid(double[] beats, int firstDownbeat, int sampleRate, double tempo, bool steady)
    {
        _beats = beats;
        _firstDownbeat = firstDownbeat;
        SampleRate = sampleRate;
        Tempo = tempo;
        IsSteady = steady;
    }

    public int SampleRate { get; }

    /// <summary>Beats per minute.</summary>
    public double Tempo { get; }

    /// <summary>
    /// True when the music keeps one tempo all the way through (most electronic and studio music), so the
    /// beats are evenly spaced; false when it drifts (a live band), and the beats follow it.
    /// </summary>
    public bool IsSteady { get; }

    /// <summary>Every beat, as a frame (with a fraction) from the start of the sound, in order.</summary>
    public IReadOnlyList<double> Beats => _beats;

    /// <summary>"124 BPM", or "93.5 BPM" when the tempo isn't a whole number.</summary>
    public string TempoText => FormatTempo(Tempo);

    /// <summary>The samples from one beat to the next, on average.</summary>
    public double SamplesPerBeat => 60.0 * SampleRate / Tempo;

    public static string FormatTempo(double bpm)
    {
        var rounded = Math.Round(bpm);
        return Math.Abs(bpm - rounded) < 0.05
            ? $"{rounded.ToString("0", CultureInfo.CurrentCulture)} BPM"
            : $"{bpm.ToString("0.0", CultureInfo.CurrentCulture)} BPM";
    }

    /// <summary>Whether beat number <paramref name="index"/> is the first of a bar.</summary>
    public bool IsDownbeat(int index) => Mod(index - _firstDownbeat, BeatsPerBar) == 0;

    /// <summary>The bar a beat is in, counting the first full bar as 1 (beats before it are in bar 0).</summary>
    public int BarOf(int index) => (int)Math.Floor((index - _firstDownbeat) / (double)BeatsPerBar) + 1;

    /// <summary>
    /// Where <paramref name="frame"/> falls among the beats: 0 at the first beat, 2.5 halfway between the third
    /// and fourth. Before the first beat and after the last it carries on at the average tempo.
    /// </summary>
    public double BeatPosition(double frame)
    {
        if (_beats.Length == 1)
        {
            return (frame - _beats[0]) / SamplesPerBeat;
        }

        if (frame <= _beats[0])
        {
            return (frame - _beats[0]) / SamplesPerBeat;
        }

        if (frame >= _beats[^1])
        {
            return _beats.Length - 1 + (frame - _beats[^1]) / SamplesPerBeat;
        }

        var index = Search(frame);
        var from = _beats[index - 1];
        var to = _beats[index];
        return index - 1 + (frame - from) / (to - from);
    }

    /// <summary>The frame at a beat position (see <see cref="BeatPosition"/>), whole or between beats.</summary>
    public double FrameAt(double position)
    {
        if (position <= 0 || _beats.Length == 1)
        {
            return _beats[0] + position * SamplesPerBeat;
        }

        if (position >= _beats.Length - 1)
        {
            return _beats[^1] + (position - (_beats.Length - 1)) * SamplesPerBeat;
        }

        var index = (int)Math.Floor(position);
        return _beats[index] + (position - index) * (_beats[index + 1] - _beats[index]);
    }

    /// <summary>
    /// The nearest beat to <paramref name="frame"/>, as a whole frame, or with <paramref name="division"/> above 1
    /// the nearest half beat (2), quarter beat (4) and so on.
    /// </summary>
    public int Nearest(int frame, int division = 1)
    {
        var position = BeatPosition(frame);
        var snapped = Math.Round(position * division) / division;
        return (int)Math.Round(FrameAt(snapped));
    }

    /// <summary>How long a stretch is in beats (with a fraction), so in bars and beats.</summary>
    public MusicalLength Measure(FrameRange range) =>
        new(BeatPosition(range.End) - BeatPosition(range.Start));

    /// <summary>
    /// A loop <paramref name="bars"/> bars long that starts on the bar at <paramref name="frame"/>: the downbeat at
    /// or just before it (or just after, if that's nearer than half a beat). Null when there aren't enough bars
    /// left in the sound from there.
    /// </summary>
    public FrameRange? BarsAt(int frame, int bars, int totalFrames)
    {
        var position = BeatPosition(frame);
        var bar = Math.Floor((position + 0.5 - _firstDownbeat) / BeatsPerBar);
        var start = _firstDownbeat + bar * BeatsPerBar;
        while (FrameAt(start) < -0.5)
        {
            start += BeatsPerBar;
        }

        return Range(start, start + bars * BeatsPerBar, totalFrames);
    }

    /// <summary>
    /// The loop moved along by its own length in beats (back for a negative <paramref name="direction"/>), so the
    /// next or previous phrase of the same length. Null when that would run past either end of the sound.
    /// </summary>
    public FrameRange? Move(FrameRange loop, int direction, int totalFrames)
    {
        var start = Math.Round(BeatPosition(loop.Start) * 4) / 4;
        var end = Math.Round(BeatPosition(loop.End) * 4) / 4;
        var length = Math.Max(1, end - start);
        var from = start + Math.Sign(direction) * length;
        return FrameAt(from) < -0.5 ? null : Range(from, from + length, totalFrames);
    }

    /// <summary>
    /// The loop made <paramref name="factor"/> times as long (2 doubles it, 0.5 halves it), keeping its start.
    /// Null if it would run past the end of the sound or become shorter than a beat.
    /// </summary>
    public FrameRange? Resize(FrameRange loop, double factor, int totalFrames)
    {
        var start = Math.Round(BeatPosition(loop.Start) * 4) / 4;
        var length = Math.Round((BeatPosition(loop.End) - start) * factor);
        return length < 1 ? null : Range(start, start + length, totalFrames);
    }

    private FrameRange? Range(double fromBeat, double toBeat, int totalFrames)
    {
        var start = (int)Math.Round(FrameAt(fromBeat));
        var end = (int)Math.Round(FrameAt(toBeat));
        return start < 0 || end > totalFrames || end <= start ? null : new FrameRange(start, end - start);
    }

    /// <summary>The downbeats from the first bar on, as beat numbers, for drawing and finding loops.</summary>
    public IEnumerable<int> Downbeats()
    {
        for (var i = _firstDownbeat; i < _beats.Length; i += BeatsPerBar)
        {
            yield return i;
        }
    }

    /// <summary>
    /// Finds the beats in the sound. Takes about a second for a song, so run it in the background.
    /// Null for sound shorter than ten seconds or without a beat to follow.
    /// </summary>
    public static BeatGrid? Analyze(AudioBuffer audio, CancellationToken cancel = default)
    {
        var rate = audio.SampleRate;
        var (full, low, hop) = BeatMap.MeasureEnergy(audio.Channels, rate);
        cancel.ThrowIfCancellationRequested();
        if (BeatMap.FindBeats(full, low, cancel) is not { } found)
        {
            return null;
        }

        var (slices, bar, period) = found;
        var steady = FitSteady(full, low, slices, period);
        cancel.ThrowIfCancellationRequested();

        // The loudness is measured in 10 ms slices, so line the beats up with the drum hits themselves.
        var hits = Transients.Analyze(audio).At(70);
        double[] beats;
        int firstDownbeat;
        if (steady is { } grid)
        {
            beats = Enumerable.Range(0, grid.Count).Select(i => (grid.Start + i * grid.Period) * hop).ToArray();
            firstDownbeat = grid.FirstDownbeat;
        }
        else
        {
            beats = slices.Select(slice => (double)slice * hop).ToArray();
            firstDownbeat = bar;
        }

        var shift = Offset(beats, hits, AlignSeconds * rate);
        for (var i = 0; i < beats.Length; i++)
        {
            beats[i] += shift;
        }

        // The shift can push the first beat off the start, or a new one could fit there.
        if (steady is not null)
        {
            while (beats.Length > 1 && beats[0] < 0)
            {
                beats = beats[1..];
                firstDownbeat = Mod(firstDownbeat - 1, BeatsPerBar);
            }
        }

        var tempo = 60.0 * rate / (steady is { } fitted ? fitted.Period * hop : (beats[^1] - beats[0]) / Math.Max(1, beats.Length - 1));
        return new BeatGrid(beats, firstDownbeat, rate, tempo, steady is not null);
    }

    /// <summary>Makes a grid at a known tempo, for tests: a beat every so often from <paramref name="firstBeat"/>.</summary>
    public static BeatGrid Steady(double tempo, double firstBeat, int frames, int sampleRate, int firstDownbeat = 0)
    {
        var perBeat = 60.0 * sampleRate / tempo;
        var count = Math.Max(1, (int)Math.Floor((frames - 1 - firstBeat) / perBeat) + 1);
        var beats = Enumerable.Range(0, count).Select(i => firstBeat + i * perBeat).ToArray();
        return new BeatGrid(beats, Mod(firstDownbeat, BeatsPerBar), sampleRate, tempo, true);
    }

    /// <summary>
    /// Most music keeps one tempo: the evenly spaced grid that lands on the most onsets, if the beats tracked mostly
    /// keep to it. The tracked beats can slip onto the off-beat for a while (a breakdown with only hi-hats, or a
    /// drop with off-beat bass), which the grid rides through. In slices of <see cref="BeatMap.FramesPerSecond"/>:
    /// the first beat, the spacing, how many beats fit in the sound, and which of the first four is the downbeat
    /// (the one the kick drum hits hardest). Null when the music speeds up or slows down.
    /// </summary>
    internal static (double Start, double Period, int Count, int FirstDownbeat)? FitSteady(float[] full, float[] low, List<int> slices, double period)
    {
        var onsets = BeatMap.Onsets(full);
        var (gridPeriod, phase) = FitGrid(onsets, MedianGap(slices) is > 0 and var gap ? gap : period);
        var near = slices.Count(slice => Math.Abs(Wrap(slice - phase, gridPeriod)) <= SteadySeconds * 2.5 * BeatMap.FramesPerSecond);
        if (near < slices.Count / 2)
        {
            return null;
        }

        // From the first beat in the sound to the end.
        var start = Wrap(phase, gridPeriod);
        start = start < 0 ? start + gridPeriod : start;
        var count = Math.Max(1, (int)Math.Floor((full.Length - 1 - start) / gridPeriod) + 1);
        var kicks = BeatMap.Onsets(low);
        var firstDownbeat = Enumerable.Range(0, BeatsPerBar)
            .Select(offset => (Offset: offset, Strength: Enumerable.Range(0, count).Where(i => i % BeatsPerBar == offset)
                .Select(i => BeatMap.Peak(kicks, (int)Math.Round(start + i * gridPeriod))).DefaultIfEmpty(0).Average()))
            .MaxBy(choice => choice.Strength).Offset;
        return (start, gridPeriod, count, firstDownbeat);
    }

    /// <summary>
    /// The evenly spaced grid, in slices of <see cref="BeatMap.FramesPerSecond"/>, that lines up with the most onsets:
    /// its spacing (searched within 2% of <paramref name="guess"/>, finely enough to stay in time across a long song)
    /// and where one of its lines falls.
    /// </summary>
    private static (double Period, double Phase) FitGrid(double[] onsets, double guess)
    {
        (double Score, double Phase) Fold(double period)
        {
            // Every onset folded into one beat, in quarter slices, then the busiest spot (smoothed a little).
            var bins = Math.Max(8, (int)(period * 4));
            var folded = new double[bins];
            for (var f = 0; f < onsets.Length; f++)
            {
                var at = f / period;
                folded[(int)((at - Math.Floor(at)) * bins) % bins] += onsets[f];
            }

            var best = (Score: double.MinValue, Phase: 0.0);
            for (var b = 0; b < bins; b++)
            {
                var score = folded[b] + 0.5 * (folded[(b + 1) % bins] + folded[(b + bins - 1) % bins]);
                if (score > best.Score)
                {
                    best = (score, (b + 0.5) / bins * period);
                }
            }

            return best;
        }

        double Search(double from, double to, double step)
        {
            var bestPeriod = from;
            var bestScore = double.MinValue;
            for (var period = from; period <= to; period += step)
            {
                var score = Fold(period).Score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPeriod = period;
                }
            }

            return bestPeriod;
        }

        var coarse = Search(guess * 0.98, guess * 1.02, 0.002);
        var fine = Search(coarse - 0.004, coarse + 0.004, 0.0002);
        return (fine, Fold(fine).Phase);
    }

    private static double MedianGap(List<int> slices)
    {
        var gaps = slices.Zip(slices.Skip(1), (a, b) => (double)(b - a)).OrderBy(gap => gap).ToList();
        return gaps.Count == 0 ? 0 : gaps[gaps.Count / 2];
    }

    /// <summary>How far <paramref name="offset"/> is from the nearest whole number of <paramref name="period"/>s, either way.</summary>
    private static double Wrap(double offset, double period) => offset - Math.Round(offset / period) * period;

    /// <summary>
    /// How far the beats sit from the drum hits nearest them, typically: the median of the gaps, for beats with
    /// a hit within <paramref name="reach"/>. 0 if too few beats have one.
    /// </summary>
    private static double Offset(double[] beats, IReadOnlyList<int> hits, double reach)
    {
        if (hits.Count == 0)
        {
            return 0;
        }

        var gaps = new List<double>();
        var h = 0;
        foreach (var beat in beats)
        {
            while (h + 1 < hits.Count && hits[h + 1] <= beat)
            {
                h++;
            }

            var nearest = hits[h];
            if (h + 1 < hits.Count && Math.Abs(hits[h + 1] - beat) < Math.Abs(nearest - beat))
            {
                nearest = hits[h + 1];
            }

            if (Math.Abs(nearest - beat) <= reach)
            {
                gaps.Add(nearest - beat);
            }
        }

        if (gaps.Count < Math.Max(8, beats.Length / 4))
        {
            return 0;
        }

        gaps.Sort();
        return gaps[gaps.Count / 2];
    }

    // The index of the first beat after the frame.
    private int Search(double frame)
    {
        int low = 0, high = _beats.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (_beats[middle] <= frame)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static int Mod(int value, int by) => ((value % by) + by) % by;
}

/// <summary>A length in beats, read as bars and beats of 4/4.</summary>
public readonly record struct MusicalLength(double Beats)
{
    // Lengths this close to a whole beat count as whole: a sample or two either way after snapping.
    private const double WholeWithin = 0.02;

    /// <summary>Whole bars in it.</summary>
    public int Bars => (int)Math.Floor(Rounded / BeatGrid.BeatsPerBar);

    /// <summary>Beats left over after the whole bars, with a fraction.</summary>
    public double ExtraBeats => Rounded - Bars * BeatGrid.BeatsPerBar;

    /// <summary>True when it's a whole number of beats, as a loop snapped to the beat is.</summary>
    public bool IsWholeBeats => Math.Abs(Beats - Math.Round(Beats)) < WholeWithin;

    private double Rounded => IsWholeBeats ? Math.Round(Beats) : Math.Max(0, Beats);

    /// <summary>"4 bars", "2 bars 1 beat", "3 beats", or "about 1 bar 2.4 beats" between beats.</summary>
    public string Text
    {
        get
        {
            var parts = new List<string>();
            if (Bars > 0)
            {
                parts.Add(Bars == 1 ? "1 bar" : $"{Bars} bars");
            }

            var beats = ExtraBeats;
            if (IsWholeBeats)
            {
                var whole = (int)Math.Round(beats);
                if (whole > 0 || parts.Count == 0)
                {
                    parts.Add(whole == 1 ? "1 beat" : $"{whole} beats");
                }

                return string.Join(" ", parts);
            }

            parts.Add($"{beats.ToString("0.0", CultureInfo.CurrentCulture)} beats");
            return "about " + string.Join(" ", parts);
        }
    }
}
