using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Where a song's bars start, found by listening to the whole file ahead of time: its tempo, the
/// beats, which beat is the "one" of each bar (the downbeat), and which downbeats start a new
/// four-bar phrase. Auto mode uses it to land each change of visual on a downbeat.
/// </summary>
public sealed class BeatMap
{
    // The song is measured in short slices, this many a second.
    internal const int FramesPerSecond = 100;

    private BeatMap(double tempo, double[] downbeats, double[] phraseStarts)
    {
        Tempo = tempo;
        Downbeats = downbeats;
        PhraseStarts = phraseStarts;
    }

    /// <summary>Beats per minute.</summary>
    public double Tempo { get; }

    /// <summary>The first beat of every bar, in seconds from the start of the song.</summary>
    public IReadOnlyList<double> Downbeats { get; }

    /// <summary>The downbeats that start a four-bar phrase, where songs tend to change section.</summary>
    public IReadOnlyList<double> PhraseStarts { get; }

    /// <summary>
    /// The downbeat to land a change on, at or after <paramref name="earliest"/> seconds: the start
    /// of a phrase if one comes within <paramref name="preferPhraseWithin"/> seconds, otherwise the
    /// next downbeat. Null near the end of the song, when there are none left.
    /// </summary>
    public double? LandingAfter(double earliest, double preferPhraseWithin)
    {
        var phrase = FirstAtOrAfter(PhraseStarts, earliest);
        if (phrase is { } start && start - earliest <= preferPhraseWithin)
        {
            return start;
        }

        return FirstAtOrAfter(Downbeats, earliest);
    }

    private static double? FirstAtOrAfter(IReadOnlyList<double> times, double earliest)
    {
        foreach (var time in times)
        {
            if (time >= earliest)
            {
                return time;
            }
        }

        return null;
    }

    /// <summary>
    /// Listens to the whole song and finds its bars. Takes a second or two, so run it in the
    /// background. Returns null for music without a steady beat to follow (or that can't be read).
    /// </summary>
    public static BeatMap? Analyze(string path, CancellationToken cancel)
    {
        float[] full, low;
        try
        {
            (full, low) = MeasureEnergy(path, cancel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }

        var found = FindBeats(full, low, cancel);
        if (found is null)
        {
            return null;
        }

        var (beats, bar, periodFound) = found.Value;
        var downbeats = beats.Where((_, i) => i % 4 == bar).ToList();

        // Sections change on phrase boundaries: the bar where the loudness of the four bars ahead
        // differs most from the four bars before.
        var phraseStarts = new List<int>();
        if (downbeats.Count >= 16)
        {
            var barLoudness = new double[downbeats.Count - 1];
            for (var i = 0; i < barLoudness.Length; i++)
            {
                barLoudness[i] = Average(Enumerable.Range(downbeats[i], downbeats[i + 1] - downbeats[i]).Select(f => Math.Log(1e-6 + full[f])));
            }

            double Change(int at) => Math.Abs(barLoudness.Skip(at).Take(4).Average() - barLoudness.Skip(at - 4).Take(4).Average());
            var phase = Enumerable.Range(0, 4)
                .Select(offset => (Offset: offset, Strength: Average(Enumerable.Range(4, barLoudness.Length - 8).Where(i => i % 4 == offset).Select(Change))))
                .MaxBy(choice => choice.Strength).Offset;
            phraseStarts = downbeats.Where((_, i) => i % 4 == phase).ToList();
        }

        static double Seconds(int frame) => (double)frame / FramesPerSecond;
        return new BeatMap(60.0 * FramesPerSecond / periodFound,
            downbeats.Select(Seconds).ToArray(), phraseStarts.Select(Seconds).ToArray());
    }

    /// <summary>
    /// The beats in the loudness of a song measured <see cref="FramesPerSecond"/> times a second: each beat's
    /// slice, which of every four beats is the downbeat (0 to 3), and the slices between beats. Null for
    /// music shorter than ten seconds or without a steady beat to follow.
    /// </summary>
    internal static (List<int> Beats, int Bar, double Period)? FindBeats(float[] full, float[] low, CancellationToken cancel)
    {
        if (full.Length < FramesPerSecond * 10)
        {
            return null;
        }

        // How sharply the sound gets louder in each slice: the starts of notes and drum hits.
        var onsets = Onsets(full);
        var kicks = Onsets(low);
        var period = FindBeatPeriod(onsets);
        if (period is null)
        {
            return null;
        }

        cancel.ThrowIfCancellationRequested();
        var beats = TrackBeats(onsets, period.Value);
        if (beats.Count < 16)
        {
            return null;
        }

        // Fast songs can be followed at half speed. If the bass hits about as hard halfway between
        // the beats found, the song is twice as fast and those halfway points are beats too; if it
        // hits harder there, the beats found are on the snare and the halfway points are the beats.
        var between = beats.Zip(beats.Skip(1), (a, b) => (a + b) / 2).ToList();
        var onBeat = Average(beats.Select(beat => Peak(kicks, beat)));
        var offBeat = Average(between.Select(beat => Peak(kicks, beat)));
        var periodFound = period.Value;
        if (offBeat > 0.5 * onBeat && 60.0 * FramesPerSecond / (periodFound / 2) <= 200)
        {
            beats = beats.Zip(between, (beat, half) => new[] { beat, half }).SelectMany(pair => pair).Append(beats[^1]).ToList();
            periodFound /= 2;
        }
        else if (offBeat > 1.2 * onBeat)
        {
            beats = between;
        }

        // In most music the kick drum hits hardest on the one, so the downbeat is whichever of the
        // four beats in a bar has the strongest bass hits on average.
        var bar = Enumerable.Range(0, 4)
            .Select(offset => (Offset: offset, Strength: Average(beats.Where((_, i) => i % 4 == offset).Select(beat => Peak(kicks, beat)))))
            .MaxBy(choice => choice.Strength).Offset;
        return (beats, bar, periodFound);
    }

    /// <summary>
    /// The loudness of each slice of audio already in memory, overall and of the bass alone, as
    /// <see cref="FindBeats"/> takes it, and how many samples make a slice.
    /// </summary>
    internal static (float[] Full, float[] Low, int Hop) MeasureEnergy(float[][] channels, int rate)
    {
        var hop = Math.Max(1, rate / FramesPerSecond);
        var lowPass = (float)(1 - Math.Exp(-2 * Math.PI * 120 / rate));
        var frames = channels[0].Length;
        var count = frames / hop;
        var full = new float[count];
        var low = new float[count];
        float bass = 0, bass2 = 0;
        for (var f = 0; f < count; f++)
        {
            float sumFull = 0, sumLow = 0;
            for (var i = f * hop; i < (f + 1) * hop; i++)
            {
                float mono = 0;
                foreach (var channel in channels)
                {
                    mono += channel[i];
                }

                mono /= channels.Length;
                bass += lowPass * (mono - bass);
                bass2 += lowPass * (bass - bass2);
                sumFull += mono * mono;
                sumLow += bass2 * bass2;
            }

            full[f] = sumFull / hop;
            low[f] = sumLow / hop;
        }

        return (full, low, hop);
    }

    /// <summary>The loudness of each slice of the song, overall and of the bass alone.</summary>
    private static (float[] Full, float[] Low) MeasureEnergy(string path, CancellationToken cancel)
    {
        using var reader = AudioReaderFactory.Open(path);
        if (reader.TotalTime > TimeSpan.FromMinutes(30))
        {
            return (Array.Empty<float>(), Array.Empty<float>());
        }

        var samples = reader.ToSampleProvider();
        var rate = samples.WaveFormat.SampleRate;
        var channels = samples.WaveFormat.Channels;
        var hop = Math.Max(1, rate / FramesPerSecond);
        // A filter that keeps what's below about 120 Hz: the kick drum and bass (two passes, so
        // little of the snare and cymbals gets through).
        var lowPass = (float)(1 - Math.Exp(-2 * Math.PI * 120 / rate));

        var full = new List<float>();
        var low = new List<float>();
        var buffer = new float[rate / 4 * channels];
        float bass = 0, bass2 = 0, sumFull = 0, sumLow = 0;
        var inFrame = 0;
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancel.ThrowIfCancellationRequested();
            for (var i = 0; i + channels <= read; i += channels)
            {
                float mono = 0;
                for (var c = 0; c < channels; c++)
                {
                    mono += buffer[i + c];
                }

                mono /= channels;
                bass += lowPass * (mono - bass);
                bass2 += lowPass * (bass - bass2);
                sumFull += mono * mono;
                sumLow += bass2 * bass2;
                if (++inFrame == hop)
                {
                    full.Add(sumFull / hop);
                    low.Add(sumLow / hop);
                    sumFull = sumLow = 0;
                    inFrame = 0;
                }
            }
        }

        return (full.ToArray(), low.ToArray());
    }

    /// <summary>Rises in loudness, slice by slice, measured against the music around them.</summary>
    internal static double[] Onsets(float[] energy)
    {
        var rise = new double[energy.Length];
        for (var f = 1; f < energy.Length; f++)
        {
            rise[f] = Math.Max(0, Math.Log(1 + 1000 * energy[f]) - Math.Log(1 + 1000 * energy[f - 1]));
        }

        // Take away the local average so quiet and loud parts of the song count alike.
        const int window = FramesPerSecond / 2;
        var onsets = new double[rise.Length];
        double sum = 0;
        for (var f = 0; f < rise.Length; f++)
        {
            sum += rise[f];
            if (f >= window)
            {
                sum -= rise[f - window];
            }

            onsets[f] = Math.Max(0, rise[f] - sum / Math.Min(f + 1, window));
        }

        var spread = Math.Sqrt(onsets.Average(x => x * x));
        if (spread > 0)
        {
            for (var f = 0; f < onsets.Length; f++)
            {
                onsets[f] /= spread;
            }
        }

        return onsets;
    }

    /// <summary>
    /// The time between beats, in slices: the spacing at which the onsets line up with themselves
    /// best, leaning towards tempos people tap along to (around 120 BPM). Null if nothing repeats clearly.
    /// </summary>
    private static double? FindBeatPeriod(double[] onsets)
    {
        var shortest = 60 * FramesPerSecond / 200;
        var longest = 60 * FramesPerSecond / 60;
        var mean = onsets.Average();
        var match = new double[longest + 2];
        double self = 0;
        for (var f = 0; f < onsets.Length; f++)
        {
            self += (onsets[f] - mean) * (onsets[f] - mean);
        }

        for (var lag = shortest - 1; lag <= longest + 1; lag++)
        {
            double sum = 0;
            for (var f = lag; f < onsets.Length; f++)
            {
                sum += (onsets[f] - mean) * (onsets[f - lag] - mean);
            }

            match[lag] = sum;
        }

        var best = -1;
        var bestScore = double.MinValue;
        for (var lag = shortest; lag <= longest; lag++)
        {
            var bpm = 60.0 * FramesPerSecond / lag;
            var octaves = Math.Log2(bpm / 120);
            var score = match[lag] * Math.Exp(-0.5 * octaves * octaves);
            if (score > bestScore)
            {
                bestScore = score;
                best = lag;
            }
        }

        if (best < 0 || self <= 0 || match[best] / self < 0.05)
        {
            return null;
        }

        // Between whole slices: fit a curve through the best spacing and its neighbours.
        double before = match[best - 1], at = match[best], after = match[best + 1];
        var bend = before - 2 * at + after;
        var shift = bend < 0 ? Math.Clamp(0.5 * (before - after) / bend, -0.5, 0.5) : 0;
        return best + shift;
    }

    /// <summary>
    /// The beats: a chain of onsets about one beat apart, chosen so the chain lands on strong
    /// onsets while keeping a steady pace (it can drift a little with the music).
    /// </summary>
    private static List<int> TrackBeats(double[] onsets, double period)
    {
        const double steadiness = 100;
        var score = new double[onsets.Length];
        var previous = new int[onsets.Length];
        for (var f = 0; f < onsets.Length; f++)
        {
            var best = 0.0;
            previous[f] = -1;
            for (var back = (int)Math.Round(period / 2); back <= (int)Math.Round(period * 2); back++)
            {
                var from = f - back;
                if (from < 0)
                {
                    break;
                }

                var off = Math.Log(back / period);
                var candidate = score[from] - steadiness * off * off;
                if (previous[f] < 0 || candidate > best)
                {
                    best = candidate;
                    previous[f] = from;
                }
            }

            score[f] = onsets[f] + Math.Max(0, best);
        }

        // Start from the best-scoring beat in the last beat of the song and follow the chain back.
        var last = onsets.Length - 1;
        var end = last;
        for (var f = Math.Max(0, last - (int)period); f <= last; f++)
        {
            if (score[f] > score[end])
            {
                end = f;
            }
        }

        var beats = new List<int>();
        for (var f = end; f >= 0; f = previous[f])
        {
            beats.Add(f);
        }

        beats.Reverse();
        return beats;
    }

    internal static double Peak(double[] values, int frame)
    {
        var peak = 0.0;
        for (var f = Math.Max(0, frame - 2); f <= Math.Min(values.Length - 1, frame + 2); f++)
        {
            peak = Math.Max(peak, values[f]);
        }

        return peak;
    }

    private static double Average(IEnumerable<double> values)
    {
        double sum = 0;
        var count = 0;
        foreach (var value in values)
        {
            sum += value;
            count++;
        }

        return count == 0 ? 0 : sum / count;
    }
}
