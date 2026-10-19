namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>A loop the loop maker found: where it is, and how well it loops (0 to 1).</summary>
public sealed record LoopCandidate(FrameRange Range, int Bars, double Score)
{
    /// <summary>"96%": how seamless it should sound going round.</summary>
    public string ScoreText => $"{Score * 100:0}%";
}

/// <summary>
/// Finds the stretches of a song that make the best loops, bar-aligned and a set number of bars long. A
/// stretch loops well when the music around its end sounds like the music around its start (so going
/// back to the start sounds like carrying on), and better still when the song itself plays the same
/// stretch again straight after it. Quiet stretches are left out.
/// </summary>
public static class LoopFinder
{
    // A loop counts as quiet this many dB below the song's typical loudness.
    private const double QuietBelowDb = 12;

    /// <summary>
    /// The best <paramref name="count"/> loops of <paramref name="bars"/> bars that don't overlap, best first.
    /// <paramref name="frames"/> are the sound's spectral frames (measured once, and kept for the next search).
    /// </summary>
    public static IReadOnlyList<LoopCandidate> Find(BeatGrid grid, SpectralFrames frames, int totalFrames, int bars, int count,
        CancellationToken cancel = default)
    {
        var beats = grid.Beats;
        var length = bars * BeatGrid.BeatsPerBar;
        if (frames.Count == 0 || beats.Count < length + 2)
        {
            return Array.Empty<LoopCandidate>();
        }

        // What each beat sounds like: its notes and the balance of its spectrum, scaled to compare by direction.
        var features = new double[beats.Count][];
        var loudness = new double[beats.Count];
        for (var b = 0; b < beats.Count; b++)
        {
            var from = (int)(beats[b] / frames.Hop);
            var to = b + 1 < beats.Count ? (int)(beats[b + 1] / frames.Hop) : from + 1;
            (features[b], loudness[b]) = Describe(frames, from, Math.Max(from + 1, to));
        }

        Standardize(features);
        var typical = Median(loudness.Where(level => level > SpectralFrames.SilentLevel));
        var found = new List<LoopCandidate>();
        foreach (var start in grid.Downbeats())
        {
            cancel.ThrowIfCancellationRequested();
            var end = start + length;
            if (start < 1 || end + 1 >= beats.Count || beats[end] > totalFrames)
            {
                continue;
            }

            var level = Average(Enumerable.Range(start, length).Select(b => loudness[b]));
            if (level < typical - QuietBelowDb)
            {
                continue;
            }

            // Going round: the beats either side of the end against the beats either side of the start.
            var seam = (Similarity(features[end - 1], features[start - 1]) + Similarity(features[end], features[start])) / 2;

            // The song repeating the loop: each beat of it against the same beat one loop later, where the song goes on that long.
            double repeat;
            if (end + length < beats.Count)
            {
                repeat = Average(Enumerable.Range(start, length).Select(b => Similarity(features[b], features[b + length])));
            }
            else
            {
                repeat = seam;
            }

            var score = Math.Clamp(0.55 * seam + 0.45 * repeat, 0, 1);
            var range = new FrameRange((int)Math.Round(beats[start]), (int)Math.Round(beats[end]) - (int)Math.Round(beats[start]));
            found.Add(new LoopCandidate(range, bars, score));
        }

        // The best ones that don't overlap, then in the order they come in the song.
        var chosen = new List<LoopCandidate>();
        foreach (var candidate in found.OrderByDescending(c => c.Score))
        {
            if (chosen.All(c => candidate.Range.End <= c.Range.Start || candidate.Range.Start >= c.Range.End))
            {
                chosen.Add(candidate);
                if (chosen.Count == count)
                {
                    break;
                }
            }
        }

        return chosen;
    }

    /// <summary>A beat's notes and spectrum as one vector, and its loudness in dB.</summary>
    private static (double[] Feature, double Level) Describe(SpectralFrames frames, int from, int to)
    {
        from = Math.Clamp(from, 0, frames.Count - 1);
        to = Math.Clamp(to, from + 1, frames.Count);
        var bands = SpectralFrames.BandEdges.Length - 1;
        var feature = new double[12 + bands];
        double level = 0;
        for (var f = from; f < to; f++)
        {
            for (var n = 0; n < 12; n++)
            {
                feature[n] += frames.Chroma[f][n];
            }

            for (var b = 0; b < bands; b++)
            {
                // dB above silence, so louder bands count for more without one band swamping the rest.
                feature[12 + b] += Math.Max(0, frames.Bands[f][b] - SpectralFrames.SilentLevel) / 60;
            }

            level += frames.Level[f];
        }

        return (feature, level / (to - from));
    }

    /// <summary>
    /// Measures every beat against the song's typical beat (each measurement less its average across the song,
    /// over its spread), then scales each to length 1, so two beats compare by what sets them apart from the rest.
    /// </summary>
    private static void Standardize(double[][] features)
    {
        var size = features[0].Length;
        for (var d = 0; d < size; d++)
        {
            var mean = features.Average(f => f[d]);
            var spread = Math.Sqrt(features.Average(f => (f[d] - mean) * (f[d] - mean)));
            foreach (var feature in features)
            {
                feature[d] = spread > 0 ? (feature[d] - mean) / spread : 0;
            }
        }

        foreach (var feature in features)
        {
            var length = Math.Sqrt(feature.Sum(x => x * x));
            for (var d = 0; length > 0 && d < size; d++)
            {
                feature[d] /= length;
            }
        }
    }

    private static double Similarity(double[] a, double[] b)
    {
        double sum = 0;
        for (var i = 0; i < a.Length; i++)
        {
            sum += a[i] * b[i];
        }

        return sum;
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

    private static double Median(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted.Count == 0 ? SpectralFrames.SilentLevel : sorted[sorted.Count / 2];
    }
}
