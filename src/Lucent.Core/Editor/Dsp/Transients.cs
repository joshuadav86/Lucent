namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>
/// Finds the transients in a sound (drum hits, plucks, the start of each note), so Tab can jump the
/// cursor from one to the next as Pro Tools does. The sound is measured once (see <see cref="Analyze"/>);
/// the sensitivity then only decides which of the onsets found count.
/// </summary>
public sealed class Transients
{
    /// <summary>The default sensitivity, from 0 (only the hardest hits) to 100 (every little onset).</summary>
    public const double DefaultSensitivity = 50;

    // Levels are measured every 5 ms, and two transients are never closer than 40 ms.
    private const double HopSeconds = 0.005;
    private const double MinGapSeconds = 0.04;

    // How many hops before the one measured make up "how loud it was just before": 20 ms.
    private const int LookBack = 4;

    // The rise in level, in dB, that counts as a transient at sensitivity 100 and at 0.
    private const double MostSensitiveRiseDb = 2;
    private const double LeastSensitiveRiseDb = 18;

    // Onsets this far below the loudest part of the sound (at sensitivity 0 and at 100) are noise.
    private const double LeastSensitiveFloorDb = -30;
    private const double MostSensitiveFloorDb = -60;

    // Within a hop, the attack is found in blocks this long.
    private const int RefineBlock = 16;

    private readonly int[] _frames;
    private readonly double[] _riseDb;
    private readonly double[] _levelDb;
    private double _cachedSensitivity = double.NaN;
    private int[] _cached = Array.Empty<int>();

    private Transients(int[] frames, double[] riseDb, double[] levelDb)
    {
        _frames = frames;
        _riseDb = riseDb;
        _levelDb = levelDb;
    }

    /// <summary>Every onset found, at any sensitivity: how many there could be.</summary>
    public int CandidateCount => _frames.Length;

    /// <summary>Measures where the sound's level jumps. A few tens of milliseconds for a whole song.</summary>
    public static Transients Analyze(AudioBuffer audio)
    {
        var frames = audio.Frames;
        var hop = Math.Max(32, (int)Math.Round(audio.SampleRate * HopSeconds));
        var hops = frames / hop;
        if (hops < LookBack + 2)
        {
            return new Transients(Array.Empty<int>(), Array.Empty<double>(), Array.Empty<double>());
        }

        // The level of the change from one sample to the next: like a gentle high-pass, it hears the
        // click of an attack over the low notes that ring under it.
        var level = new double[hops];
        var previous = Mix(audio, 0);
        for (var h = 0; h < hops; h++)
        {
            double energy = 0;
            var end = (h + 1) * hop;
            for (var i = h * hop; i < end; i++)
            {
                var current = Mix(audio, i);
                var difference = current - previous;
                energy += difference * difference;
                previous = current;
            }

            level[h] = 10 * Math.Log10(energy / hop + 1e-12);
        }

        var loudest = level.Max();

        // How much each hop is louder than the hops just before it.
        var rise = new double[hops];
        for (var h = LookBack; h < hops; h++)
        {
            var before = level[h - 1];
            for (var k = 2; k <= LookBack; k++)
            {
                before = Math.Max(before, level[h - k]);
            }

            rise[h] = level[h] - before;
        }

        // The onsets: hops that rise more than any other nearby, and rise at all.
        var gap = Math.Max(1, (int)Math.Round(MinGapSeconds / HopSeconds));
        var found = new List<(int Frame, double Rise, double Level)>();
        for (var h = LookBack; h < hops; h++)
        {
            if (rise[h] < MostSensitiveRiseDb || level[h] - loudest < MostSensitiveFloorDb)
            {
                continue;
            }

            var isPeak = true;
            for (var k = Math.Max(0, h - gap); k <= Math.Min(hops - 1, h + gap) && isPeak; k++)
            {
                isPeak = k == h || rise[k] < rise[h] || (rise[k] == rise[h] && k > h);
            }

            if (isPeak)
            {
                found.Add((Attack(audio, h, hop), rise[h], level[h] - loudest));
            }
        }

        found.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        return new Transients(
            found.Select(f => f.Frame).ToArray(),
            found.Select(f => f.Rise).ToArray(),
            found.Select(f => f.Level).ToArray());
    }

    /// <summary>The transients that count at a sensitivity from 0 (fewest) to 100 (most), in order.</summary>
    public IReadOnlyList<int> At(double sensitivity)
    {
        sensitivity = Math.Clamp(sensitivity, 0, 100);
        if (sensitivity != _cachedSensitivity)
        {
            var share = sensitivity / 100;
            var minRise = LeastSensitiveRiseDb + (MostSensitiveRiseDb - LeastSensitiveRiseDb) * share;
            var floor = LeastSensitiveFloorDb + (MostSensitiveFloorDb - LeastSensitiveFloorDb) * share;
            _cached = Enumerable.Range(0, _frames.Length)
                .Where(i => _riseDb[i] >= minRise && _levelDb[i] >= floor)
                .Select(i => _frames[i])
                .ToArray();
            _cachedSensitivity = sensitivity;
        }

        return _cached;
    }

    /// <summary>The first transient after <paramref name="frame"/>, or null at the last one.</summary>
    public int? Next(int frame, double sensitivity)
    {
        var frames = At(sensitivity);
        var index = Search(frames, frame + 1);
        return index < frames.Count ? frames[index] : null;
    }

    /// <summary>The last transient before <paramref name="frame"/>, or null at the first one.</summary>
    public int? Previous(int frame, double sensitivity)
    {
        var frames = At(sensitivity);
        var index = Search(frames, frame) - 1;
        return index >= 0 ? frames[index] : null;
    }

    // The index of the first frame at or after the one given.
    private static int Search(IReadOnlyList<int> frames, int frame)
    {
        int low = 0, high = frames.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (frames[middle] < frame)
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

    /// <summary>
    /// Where the attack in hop <paramref name="h"/> starts, to within a millisecond or so: the first
    /// short block that's a quarter of the way from the level before it up to the hit's peak. Like the
    /// detection, it measures the change from sample to sample, so a loud bass note under the hit doesn't hide it.
    /// </summary>
    private static int Attack(AudioBuffer audio, int h, int hop)
    {
        var from = Math.Max(1, (h - 2) * hop);
        var to = Math.Min(audio.Frames, (h + 1) * hop);
        var blocks = (to - from) / RefineBlock;
        if (blocks < 2)
        {
            return h * hop;
        }

        var peaks = new double[blocks];
        for (var b = 0; b < blocks; b++)
        {
            double peak = 0;
            var start = from + b * RefineBlock;
            for (var i = start; i < start + RefineBlock; i++)
            {
                peak = Math.Max(peak, Math.Abs(Mix(audio, i) - Mix(audio, i - 1)));
            }

            peaks[b] = peak;
        }

        // The quietest block before the loudest one is the level the hit rises from.
        var loudest = Array.IndexOf(peaks, peaks.Max());
        var floor = peaks.Take(Math.Max(1, loudest)).Min();
        var threshold = floor + (peaks[loudest] - floor) * 0.25;
        for (var b = 0; b <= loudest; b++)
        {
            if (peaks[b] >= threshold && (b == 0 || peaks[b - 1] < threshold))
            {
                // The last crossing before the peak, in case the level wobbled up and back first.
                var last = b;
                for (var c = b + 1; c <= loudest; c++)
                {
                    if (peaks[c - 1] < threshold && peaks[c] >= threshold)
                    {
                        last = c;
                    }
                }

                return from + last * RefineBlock;
            }
        }

        return h * hop;
    }

    private static double Mix(AudioBuffer audio, int frame)
    {
        double sum = 0;
        foreach (var channel in audio.Channels)
        {
            sum += channel[frame];
        }

        return sum;
    }
}
