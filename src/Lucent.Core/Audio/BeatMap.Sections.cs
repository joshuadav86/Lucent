namespace WinampInspiredVisualizer.Audio;

/// <summary>A stretch of a song that keeps one feel (an intro, a verse, a drop, a breakdown), and how intense it is, 0 to 1.</summary>
public readonly record struct SongSection(double Start, double End, double Intensity);

/// <summary>A big entrance (an EDM drop, a chorus crashing in) at <see cref="At"/> seconds, and where the build-up to it starts.</summary>
public readonly record struct SongDrop(double BuildStart, double At);

/// <summary>
/// Where a song is at one moment, as the visuals see it. <see cref="BeatPhase"/> and <see cref="BarPhase"/> run
/// 0 to 1 through each beat and bar; <see cref="Intensity"/> is how intense the section is, 0 to 1;
/// <see cref="BuildUp"/> climbs 0 to 1 through the build-up to a drop; <see cref="Drop"/> jumps to 1 on a drop
/// and fades; <see cref="SectionPhase"/> runs 0 to 1 through the section.
/// </summary>
public readonly record struct SongMoment(float BeatPhase, float BarPhase, float Intensity, float BuildUp, float Drop, float SectionPhase);

public sealed partial class BeatMap
{
    // Neighbouring stretches whose intensity differs by less than this are taken as one section.
    private const double SameSection = 0.15;

    // A drop: the music gets at least this much more intense, into a section at least this intense,
    // and the bass comes in at least this much harder.
    private const double DropRise = 0.25;
    private const double DropIntensity = 0.55;
    private const double DropBass = 0.25;

    // How long the build-up to a drop can be, in bars.
    private const int BuildBars = 8;

    // How long a drop's flash takes to fade, in seconds.
    private const double DropFade = 4;

    /// <summary>
    /// The song's sections, back to back from the start, split where the feel changes (on a downbeat, or on the drop).
    /// Empty for songs under 16 bars.
    /// </summary>
    public IReadOnlyList<SongSection> Sections { get; }

    /// <summary>The drops, in order: where the music crashes in after quieter or sparser bars.</summary>
    public IReadOnlyList<SongDrop> Drops { get; }

    /// <summary>The first drop at or after <paramref name="earliest"/> seconds, or null when there's none left.</summary>
    public double? NextDrop(double earliest)
    {
        foreach (var drop in Drops)
        {
            if (drop.At >= earliest)
            {
                return drop.At;
            }
        }

        return null;
    }

    /// <summary>How intense the section at <paramref name="position"/> seconds is, 0 to 1 (a half when unknown).</summary>
    public double IntensityAt(double position) => SectionAt(position) is { } index ? Sections[index].Intensity : 0.5;

    /// <summary>Where the song is at <paramref name="position"/> seconds.</summary>
    public SongMoment At(double position)
    {
        var beatLength = 60.0 / Tempo;
        var beatPhase = Phase(Beats, position, beatLength);
        var barPhase = Phase(Downbeats, position, beatLength * 4);

        var intensity = 0.5;
        var sectionPhase = 0.0;
        if (SectionAt(position) is { } index)
        {
            var section = Sections[index];
            intensity = section.Intensity;
            sectionPhase = (position - section.Start) / Math.Max(1e-3, section.End - section.Start);

            // Calming down is felt coming: over the last bar, ease towards a quieter section. Getting more
            // intense lands all at once, on the change.
            var barLength = beatLength * 4;
            if (index + 1 < Sections.Count && Sections[index + 1].Intensity < intensity && position > section.End - barLength)
            {
                var through = (position - (section.End - barLength)) / barLength;
                intensity += (Sections[index + 1].Intensity - intensity) * through * through * (3 - 2 * through);
            }
        }

        double buildUp = 0, dropFlash = 0;
        foreach (var drop in Drops)
        {
            if (position >= drop.BuildStart && position < drop.At)
            {
                buildUp = (position - drop.BuildStart) / Math.Max(1e-3, drop.At - drop.BuildStart);
            }
            else if (position >= drop.At && position < drop.At + DropFade)
            {
                dropFlash = Math.Exp(-1.5 * (position - drop.At));
            }
        }

        return new SongMoment((float)beatPhase, (float)barPhase, (float)Math.Clamp(intensity, 0, 1), (float)buildUp, (float)dropFlash,
            (float)Math.Clamp(sectionPhase, 0, 1));
    }

    /// <summary>How many beats have gone by at <paramref name="position"/> seconds.</summary>
    public int BeatsBefore(double position)
    {
        int low = 0, high = Beats.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (Beats[middle] <= position)
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

    private int? SectionAt(double position)
    {
        for (var i = 0; i < Sections.Count; i++)
        {
            if (position < Sections[i].End || i == Sections.Count - 1)
            {
                return position >= Sections[i].Start || i == 0 ? i : null;
            }
        }

        return null;
    }

    /// <summary>How far through the gap between two of <paramref name="times"/> <paramref name="position"/> is, carrying on at <paramref name="length"/> apart beyond them.</summary>
    private static double Phase(IReadOnlyList<double> times, double position, double length)
    {
        if (times.Count == 0)
        {
            return 0;
        }

        var first = times[0];
        var last = times[^1];
        if (position < first || position >= last || times.Count < 2)
        {
            var from = position < first ? first : last;
            var steps = (position - from) / length;
            return steps - Math.Floor(steps);
        }

        // The gap it's in: the last time at or before it.
        int low = 0, high = times.Count - 1;
        while (high - low > 1)
        {
            var middle = (low + high) / 2;
            if (times[middle] <= position)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (position - times[low]) / Math.Max(1e-3, times[high] - times[low]);
    }

    /// <summary>
    /// Which of every four beats starts a bar, judged by where the music changes: songs build, drop and break down
    /// on the one. Null when that isn't clear (the kick drum then decides, as in <see cref="FindBeats"/>), which
    /// matters for music with a kick on every beat, where the kick can't tell the one from the three.
    /// </summary>
    private static int? ChangesOnTheOne(float[] full, float[] low, List<int> beats)
    {
        if (beats.Count < 64)
        {
            return null;
        }

        var loud = BeatLevels(full, beats);
        var bass = BeatLevels(low, beats);
        var score = new double[4];
        for (var i = 4; i + 4 <= beats.Count; i++)
        {
            var change = Math.Abs(Mean(loud, i, i + 2) - Mean(loud, i - 2, i)) + Math.Abs(Mean(bass, i, i + 2) - Mean(bass, i - 2, i));
            score[i % 4] += change * change;
        }

        var ranked = Enumerable.Range(0, 4).OrderByDescending(offset => score[offset]).ToArray();
        return score[ranked[0]] >= 1.3 * score[ranked[1]] ? ranked[0] : null;
    }

    /// <summary>The loudness from each beat to the next, in decibels.</summary>
    private static double[] BeatLevels(float[] energy, List<int> beats)
    {
        var levels = new double[beats.Count];
        for (var i = 0; i < beats.Count; i++)
        {
            var from = beats[i];
            var to = Math.Min(energy.Length, i + 1 < beats.Count ? beats[i + 1] : from + (from - beats[Math.Max(0, i - 1)]));
            double sum = 0;
            for (var f = from; f < to; f++)
            {
                sum += energy[f];
            }

            levels[i] = 10 * Math.Log10(1e-9 + sum / Math.Max(1, to - from));
        }

        return levels;
    }

    /// <summary>The average of <paramref name="values"/> from <paramref name="from"/> up to <paramref name="to"/>, kept within the array.</summary>
    private static double Mean(double[] values, int from, int to)
    {
        from = Math.Clamp(from, 0, values.Length - 1);
        to = Math.Clamp(to, from + 1, values.Length);
        double sum = 0;
        for (var i = from; i < to; i++)
        {
            sum += values[i];
        }

        return sum / (to - from);
    }

    /// <summary>
    /// Splits a song into sections and finds its drops. Each bar gets an intensity from its loudness, how hard its
    /// bass hits, and how busy it is (all measured against the rest of the song). A section changes where the four
    /// bars ahead differ most from the four before, and neighbouring sections that feel alike join up. A drop is a
    /// change up into an intense section with the bass coming in, timed to the beat where the bass jumps most.
    /// </summary>
    private static (SongSection[] Sections, SongDrop[] Drops) FindSections(float[] full, float[] low, List<int> beats, List<int> downbeats)
    {
        if (downbeats.Count < 16)
        {
            return (Array.Empty<SongSection>(), Array.Empty<SongDrop>());
        }

        var onsets = Onsets(full);
        var bars = downbeats.Count - 1;
        var loud = new double[bars];
        var bass = new double[bars];
        var busy = new double[bars];
        for (var b = 0; b < bars; b++)
        {
            double sumLoud = 0, sumBass = 0, sumBusy = 0;
            var count = downbeats[b + 1] - downbeats[b];
            for (var f = downbeats[b]; f < downbeats[b + 1]; f++)
            {
                sumLoud += Math.Log(1e-7 + full[f]);
                sumBass += Math.Log(1e-7 + low[f]);
                sumBusy += onsets[f];
            }

            loud[b] = sumLoud / count;
            bass[b] = sumBass / count;
            busy[b] = sumBusy / count;
        }

        // Loudness and bass are in natural logs of power, so differences under about 6 dB never span the whole range.
        Spread(loud, 1.5);
        Spread(bass, 1.5);
        Spread(busy, 0.3);
        var level = new double[bars];
        for (var b = 0; b < bars; b++)
        {
            level[b] = 0.45 * loud[b] + 0.4 * bass[b] + 0.15 * busy[b];
        }

        // Where the feel changes: bars where the four ahead differ from the four before by enough, and by more than
        // at the bars around them.
        var change = new double[bars];
        for (var b = 1; b < bars; b++)
        {
            change[b] = Math.Abs(Mean(level, b, b + 4) - Mean(level, b - 4, b));
        }

        var starts = new List<int> { 0 };
        for (var b = 1; b < bars; b++)
        {
            var strongest = Enumerable.Range(Math.Max(1, b - 3), Math.Min(bars, b + 4) - Math.Max(1, b - 3)).MaxBy(x => change[x]);
            if (change[b] >= SameSection && strongest == b)
            {
                starts.Add(b);
            }
        }

        // Join neighbours that feel alike, the most alike first.
        double Intensity(int s) => Mean(level, starts[s], s + 1 < starts.Count ? starts[s + 1] : bars);
        while (starts.Count > 1)
        {
            var closest = Enumerable.Range(1, starts.Count - 1).MinBy(s => Math.Abs(Intensity(s) - Intensity(s - 1)));
            if (Math.Abs(Intensity(closest) - Intensity(closest - 1)) >= SameSection)
            {
                break;
            }

            starts.RemoveAt(closest);
        }

        static double Seconds(int frame) => (double)frame / FramesPerSecond;
        var sections = Enumerable.Range(0, starts.Count).Select(s => new SongSection(s == 0 ? 0 : Seconds(downbeats[starts[s]]),
            s + 1 < starts.Count ? Seconds(downbeats[starts[s + 1]]) : Seconds(full.Length), Intensity(s))).ToArray();

        var beatLoud = BeatLevels(full, beats);
        var beatBass = BeatLevels(low, beats);
        var barSeconds = Seconds(downbeats[^1] - downbeats[0]) / (downbeats.Count - 1);
        var drops = new List<SongDrop>();
        for (var s = 1; s < starts.Count; s++)
        {
            var at = starts[s];
            var rise = sections[s].Intensity - sections[s - 1].Intensity;
            var bassIn = Mean(bass, at, at + 2) - Mean(bass, at - 2, at);
            if (rise < DropRise || sections[s].Intensity < DropIntensity || bassIn < DropBass)
            {
                continue;
            }

            // The drop itself: the beat from a bar before the change to two after where the bass jumps most.
            var first = beats.FindIndex(beat => beat >= downbeats[Math.Max(0, at - 1)]);
            var last = beats.FindLastIndex(beat => beat < downbeats[Math.Min(bars, at + 2)]);
            if (first < 0 || last < first)
            {
                continue;
            }

            var hit = Enumerable.Range(first, last - first + 1)
                .MaxBy(i => Mean(beatBass, i, i + 4) - Mean(beatBass, i - 4, i) + 0.5 * (Mean(beatLoud, i, i + 4) - Mean(beatLoud, i - 4, i)));
            var dropAt = Seconds(beats[hit]);
            var buildFrom = Math.Max(sections[s - 1].Start, dropAt - BuildBars * barSeconds);
            drops.Add(new SongDrop(buildFrom, dropAt));

            // The section starts with the drop.
            sections[s - 1] = sections[s - 1] with { End = dropAt };
            sections[s] = sections[s] with { Start = dropAt };
        }

        return (sections, drops.ToArray());
    }

    /// <summary>
    /// Rescales values so the quietest tenth of the song sits at 0 and the loudest tenth at 1, or less than 1 when they're
    /// closer together than <paramref name="smallest"/> (so a song that barely changes doesn't look like it changes a lot).
    /// </summary>
    private static void Spread(double[] values, double smallest)
    {
        var sorted = values.Order().ToArray();
        var bottom = sorted[(int)(0.1 * (sorted.Length - 1))];
        var top = sorted[(int)Math.Ceiling(0.9 * (sorted.Length - 1))];
        var range = Math.Max(top - bottom, smallest);
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Math.Clamp((values[i] - bottom) / range, 0, 1);
        }
    }
}
