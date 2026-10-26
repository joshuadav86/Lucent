namespace Lucent.Editor.Dsp;

/// <summary>
/// A musical key, such as A minor, with its Camelot code (8A) that DJs use to find songs that mix well:
/// the same number, a number either side, or the same number with the other letter.
/// </summary>
public readonly record struct MusicalKey(int Tonic, bool IsMinor, double Confidence)
{
    // Flats for the keys usually written with them; sharps for F♯ major and the minor keys written with sharps.
    private static readonly string[] MajorNames = { "C", "D♭", "D", "E♭", "E", "F", "F♯", "G", "A♭", "A", "B♭", "B" };
    private static readonly string[] MinorNames = { "C", "C♯", "D", "E♭", "E", "F", "F♯", "G", "G♯", "A", "B♭", "B" };

    /// <summary>Below this the music hardly leans to one key (drums alone, say), so the key is a guess.</summary>
    public const double SureConfidence = 0.08;

    /// <summary>"A minor".</summary>
    public string Name => $"{Note} {(IsMinor ? "minor" : "major")}";

    /// <summary>"Am", as DJ software writes it in a file's key tag (with # and b).</summary>
    public string TagText => Note.Replace("♯", "#").Replace("♭", "b") + (IsMinor ? "m" : string.Empty);

    /// <summary>"8A": the number around the wheel of fifths, A for minor and B for major.</summary>
    public string Camelot
    {
        get
        {
            // C major and A minor are both 8; each fifth up is one more.
            var major = IsMinor ? (Tonic + 3) % 12 : Tonic;
            var number = (8 + 7 * major) % 12;
            return $"{(number == 0 ? 12 : number)}{(IsMinor ? "A" : "B")}";
        }
    }

    /// <summary>Whether the music leaned clearly enough to one key for it to be worth showing without a "probably".</summary>
    public bool IsSure => Confidence >= SureConfidence;

    private string Note => (IsMinor ? MinorNames : MajorNames)[Tonic];

    public override string ToString() => $"{Name} ({Camelot})";
}

/// <summary>
/// Finds the key music is in: how much of each of the twelve notes it plays over the whole sound (its chroma),
/// matched against how often each note of a key is heard in music in that key (Krumhansl and Kessler's profiles).
/// </summary>
public static class KeyDetector
{
    // How often each note of the scale is heard, from the key note up, in music in a major and a minor key.
    private static readonly double[] MajorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] MinorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };

    /// <summary>The key, or null for sound too short or quiet to tell (under about two seconds of notes).</summary>
    public static MusicalKey? Analyze(AudioBuffer audio, CancellationToken cancel = default) =>
        Analyze(SpectralFrames.Measure(audio, cancel), 0, int.MaxValue);

    /// <summary>The key of some of the frames already measured: from <paramref name="first"/>, up to <paramref name="count"/> of them.</summary>
    public static MusicalKey? Analyze(SpectralFrames frames, int first, int count)
    {
        var chroma = new double[12];
        var heard = 0;
        var end = (int)Math.Min(frames.Count, (long)first + count);
        for (var f = Math.Max(0, first); f < end; f++)
        {
            if (frames.Level[f] <= SpectralFrames.SilentLevel)
            {
                continue;
            }

            heard++;
            var notes = frames.Chroma[f];
            for (var n = 0; n < 12; n++)
            {
                chroma[n] += notes[n];
            }
        }

        if (heard < frames.FramesPerSecond * 2)
        {
            return null;
        }

        var scores = new List<(int Tonic, bool Minor, double Score)>();
        for (var tonic = 0; tonic < 12; tonic++)
        {
            scores.Add((tonic, false, Correlation(chroma, MajorProfile, tonic)));
            scores.Add((tonic, true, Correlation(chroma, MinorProfile, tonic)));
        }

        scores.Sort((a, b) => b.Score.CompareTo(a.Score));
        var best = scores[0];
        // How far ahead of the next best key, other than its relative (C major for A minor), which shares its notes.
        var relative = best.Minor ? (best.Tonic + 3) % 12 : (best.Tonic + 9) % 12;
        var runnerUp = scores.Skip(1).First(s => !(s.Tonic == relative && s.Minor != best.Minor));
        return new MusicalKey(best.Tonic, best.Minor, Math.Max(0, best.Score - runnerUp.Score));
    }

    /// <summary>How well the chroma matches a key's profile turned to start on <paramref name="tonic"/> (Pearson's r).</summary>
    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        double meanChroma = chroma.Average(), meanProfile = profile.Average();
        double both = 0, chromaSpread = 0, profileSpread = 0;
        for (var n = 0; n < 12; n++)
        {
            var c = chroma[n] - meanChroma;
            var p = profile[(n - tonic + 12) % 12] - meanProfile;
            both += c * p;
            chromaSpread += c * c;
            profileSpread += p * p;
        }

        return chromaSpread <= 0 ? 0 : both / Math.Sqrt(chromaSpread * profileSpread);
    }
}

/// <summary>
/// A sound measured in short frames for what notes and how much of each part of the spectrum is in each:
/// the key comes from the notes, and the loop maker compares stretches by both.
/// </summary>
public sealed class SpectralFrames
{
    /// <summary>The FFT's length; the frames don't overlap. About 93 ms at 44.1 kHz.</summary>
    public const int Size = 4096;

    /// <summary>Bands of the spectrum measured for <see cref="Bands"/>, from these frequencies up to the next.</summary>
    public static readonly double[] BandEdges = { 20, 120, 400, 1200, 3500, 8000, 20000 };

    /// <summary>A frame quieter than this (its level in dB) counts as silence.</summary>
    public const double SilentLevel = -60;

    // Notes are listened for from C2 to about C7: below, an FFT this long can't tell neighbouring notes apart.
    private const double LowestNote = 65.4;
    private const double HighestNote = 2100;

    // A note's peak stands this many times above the average of the bins this far either side of it.
    private const int PeakReach = 8;
    private const double PeakAbove = 2.5;

    private SpectralFrames(double[][] chroma, double[][] bands, double[] level, int hop, int sampleRate)
    {
        Chroma = chroma;
        Bands = bands;
        Level = level;
        Hop = hop;
        SampleRate = sampleRate;
    }

    /// <summary>For each frame, how much of each note (C first) is in it, scaled so the twelve add up to 1.</summary>
    public double[][] Chroma { get; }

    /// <summary>For each frame, the loudness in dB of each band between <see cref="BandEdges"/>.</summary>
    public double[][] Bands { get; }

    /// <summary>For each frame, its overall loudness in dB.</summary>
    public double[] Level { get; }

    /// <summary>Samples from the start of one frame to the next.</summary>
    public int Hop { get; }

    public int SampleRate { get; }

    public int Count => Level.Length;

    public double FramesPerSecond => (double)SampleRate / Hop;

    /// <summary>Measures every frame of the sound, mixed to mono. A few hundred milliseconds for a song.</summary>
    public static SpectralFrames Measure(AudioBuffer audio, CancellationToken cancel = default)
    {
        var rate = audio.SampleRate;
        var count = audio.Frames / Size;
        var chroma = new double[count][];
        var bands = new double[count][];
        var level = new double[count];

        // Which note (0 is C) and band each FFT bin belongs to, and a Hann window.
        var note = new int[Size / 2];
        var band = new int[Size / 2];
        for (var k = 0; k < Size / 2; k++)
        {
            var frequency = (double)k * rate / Size;
            note[k] = frequency >= LowestNote && frequency <= HighestNote
                ? (int)(((Math.Round(12 * Math.Log2(frequency / 440)) + 9) % 12 + 12) % 12)
                : -1;
            band[k] = -1;
            for (var b = 0; b < BandEdges.Length - 1; b++)
            {
                if (frequency >= BandEdges[b] && frequency < BandEdges[b + 1])
                {
                    band[k] = b;
                }
            }
        }

        var window = new double[Size];
        for (var i = 0; i < Size; i++)
        {
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));
        }

        Parallel.For(0, count, new ParallelOptions { CancellationToken = cancel }, () => (new Fft(Size), new double[Size], new double[Size]),
            (f, _, scratch) =>
            {
                var (fft, re, im) = scratch;
                var start = f * Size;
                double energy = 0;
                for (var i = 0; i < Size; i++)
                {
                    double mono = 0;
                    foreach (var channel in audio.Channels)
                    {
                        mono += channel[start + i];
                    }

                    mono /= audio.ChannelCount;
                    energy += mono * mono;
                    re[i] = mono * window[i];
                    im[i] = 0;
                }

                fft.Transform(re, im, inverse: false);
                var notes = new double[12];
                var power = new double[BandEdges.Length - 1];
                for (var k = 0; k < Size / 2; k++)
                {
                    re[k] = Math.Sqrt(re[k] * re[k] + im[k] * im[k]);
                    if (band[k] >= 0)
                    {
                        power[band[k]] += re[k] * re[k];
                    }
                }

                // Only the peaks that stand well above the spectrum around them are notes; drums and hiss
                // spread across every bin and would otherwise count for every note at once.
                for (var k = PeakReach; k < Size / 2 - PeakReach; k++)
                {
                    var magnitude = re[k];
                    if (note[k] < 0 || magnitude <= re[k - 1] || magnitude < re[k + 1])
                    {
                        continue;
                    }

                    double around = 0;
                    for (var j = k - PeakReach; j <= k + PeakReach; j++)
                    {
                        around += re[j];
                    }

                    if (magnitude > PeakAbove * around / (2 * PeakReach + 1))
                    {
                        notes[note[k]] += magnitude;
                    }
                }

                var total = notes.Sum();
                if (total > 0)
                {
                    for (var n = 0; n < 12; n++)
                    {
                        notes[n] /= total;
                    }
                }

                chroma[f] = notes;
                bands[f] = power.Select(p => 10 * Math.Log10(p / Size + 1e-12)).ToArray();
                level[f] = 10 * Math.Log10(energy / Size + 1e-12);
                return scratch;
            },
            _ => { });

        return new SpectralFrames(chroma, bands, level, Size, rate);
    }
}
