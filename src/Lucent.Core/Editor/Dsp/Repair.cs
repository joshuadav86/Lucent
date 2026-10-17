namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>
/// Finds and repairs clicks and pops (vinyl crackle, digital glitches): short bursts the music around them
/// doesn't predict. Each channel is modelled as an autoregressive signal, block by block; samples the model
/// predicts badly, by far more than usual for that block, are clicks. They're replaced by extending the
/// music from both sides with the same model and blending the two.
/// </summary>
public static class ClickRepair
{
    private const int Order = 24;
    private const int Block = 4096;

    /// <summary>Repairs one channel in place. Returns how many clicks were fixed.</summary>
    /// <param name="sensitivity">1 (only the loudest clicks) to 10 (anything that stands out).</param>
    public static int Repair(float[] samples, int sampleRate, double sensitivity, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        // How many times the block's typical prediction error a click must reach.
        var threshold = 14 - Math.Clamp(sensitivity, 1, 10);
        var maxGap = Math.Max(8, sampleRate * 3 / 1000);
        var flagged = new bool[samples.Length];
        var blocks = (samples.Length + Block - 1) / Block;
        var done = 0;
        Parallel.For(0, blocks, new ParallelOptions { CancellationToken = cancellation }, b =>
        {
            var start = b * Block;
            var end = Math.Min(samples.Length, start + Block);
            var length = end - start;
            if (length <= Order * 4)
            {
                return;
            }

            var x = new double[length];
            for (var i = 0; i < length; i++)
            {
                x[i] = samples[start + i];
            }

            var a = Coefficients(x, Order);
            var error = new double[length];
            for (var n = Order; n < length - Order; n++)
            {
                // A click is mispredicted both from before it and from after it; the music just after a
                // click is only mispredicted from before (its past is damaged), so take the smaller of the two.
                double backward = 0;
                for (var k = 1; k <= Order; k++)
                {
                    backward += a[k] * x[n + k];
                }

                error[n] = Math.Min(Math.Abs(x[n] - Predict(x, a, n)), Math.Abs(x[n] - backward));
            }

            // The typical error, robustly: the median isn't thrown by the clicks themselves.
            var sorted = error.Skip(Order).Take(length - 2 * Order).Order().ToArray();
            var typical = sorted[sorted.Length / 2] * 1.4826 + 1e-7;
            for (var n = Order; n < length - Order; n++)
            {
                if (error[n] > threshold * typical)
                {
                    flagged[start + n] = true;
                }
            }

            progress?.Invoke(0.5 * Interlocked.Increment(ref done) / blocks);
        });

        // Group flagged samples into gaps, a little wider than what was flagged, and fill each one.
        var gaps = new List<(int Start, int End)>();
        for (var i = 0; i < samples.Length; i++)
        {
            if (!flagged[i])
            {
                continue;
            }

            var start = Math.Max(0, i - 2);
            var end = i + 1;
            while (end < samples.Length && end - start < maxGap)
            {
                // Flags within a few samples belong to the same click.
                var next = Array.IndexOf(flagged, true, end, Math.Min(6, samples.Length - end));
                if (next < 0)
                {
                    break;
                }

                end = next + 1;
            }

            end = Math.Min(samples.Length, end + 2);
            gaps.Add((start, end));
            i = end;
        }

        var context = Order * 16;
        for (var g = 0; g < gaps.Count; g++)
        {
            cancellation.ThrowIfCancellationRequested();
            var (start, end) = gaps[g];
            if (start < context || end + context > samples.Length)
            {
                continue;
            }

            Fill(samples, start, end, context);
            if (g % 64 == 0)
            {
                progress?.Invoke(0.5 + 0.5 * g / gaps.Count);
            }
        }

        progress?.Invoke(1);
        return gaps.Count(g => g.Start >= context && g.End + context <= samples.Length);
    }

    /// <summary>
    /// Fills a gap with the samples that fit the music's model best: least-squares autoregressive interpolation
    /// (Janssen, Veldhuis and Vries, 1986), with the model learned from the audio either side of the gap.
    /// </summary>
    private static void Fill(float[] samples, int start, int end, int context)
    {
        var gap = end - start;
        var around = new double[2 * context];
        for (var i = 0; i < context; i++)
        {
            around[i] = samples[start - context + i];
            around[context + i] = samples[end + i];
        }

        // Learned from both sides; the jump where they meet in "around" barely affects the fit.
        var a = Coefficients(around, Order);
        var b = new double[Order + 1];
        b[0] = 1;
        for (var k = 1; k <= Order; k++)
        {
            b[k] = -a[k];
        }

        // Prediction errors e[n] = Σ b[k]·x[n−k] for every n that sees a missing sample. Split into the part from
        // the unknown samples (matrix rows) and from the known ones (right-hand side), then solve the normal equations.
        var rows = gap + Order;
        var normal = new double[gap, gap];
        var rhs = new double[gap];
        var row = new double[gap];
        for (var r = 0; r < rows; r++)
        {
            var n = start + r;
            Array.Clear(row);
            double known = 0;
            for (var k = 0; k <= Order; k++)
            {
                var index = n - k;
                if (index >= start && index < end)
                {
                    row[index - start] += b[k];
                }
                else
                {
                    known += b[k] * samples[index];
                }
            }

            for (var i = 0; i < gap; i++)
            {
                if (row[i] == 0)
                {
                    continue;
                }

                rhs[i] -= row[i] * known;
                for (var j = 0; j < gap; j++)
                {
                    normal[i, j] += row[i] * row[j];
                }
            }
        }

        var solution = SolveSymmetric(normal, rhs);
        if (solution is null)
        {
            return;
        }

        for (var i = 0; i < gap; i++)
        {
            samples[start + i] = (float)solution[i];
        }
    }

    /// <summary>Solves a symmetric positive-definite system by Cholesky decomposition; null if it isn't one.</summary>
    private static double[]? SolveSymmetric(double[,] m, double[] rhs)
    {
        var n = rhs.Length;
        var l = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j <= i; j++)
            {
                var sum = m[i, j];
                for (var k = 0; k < j; k++)
                {
                    sum -= l[i, k] * l[j, k];
                }

                if (i == j)
                {
                    if (sum <= 0)
                    {
                        return null;
                    }

                    l[i, i] = Math.Sqrt(sum);
                }
                else
                {
                    l[i, j] = sum / l[j, j];
                }
            }
        }

        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = rhs[i];
            for (var k = 0; k < i; k++)
            {
                sum -= l[i, k] * y[k];
            }

            y[i] = sum / l[i, i];
        }

        var x = new double[n];
        for (var i = n - 1; i >= 0; i--)
        {
            var sum = y[i];
            for (var k = i + 1; k < n; k++)
            {
                sum -= l[k, i] * x[k];
            }

            x[i] = sum / l[i, i];
        }

        return x;
    }

    private static double Predict(double[] x, double[] a, int n)
    {
        double sum = 0;
        for (var k = 1; k < a.Length; k++)
        {
            sum += a[k] * x[n - k];
        }

        return sum;
    }

    /// <summary>
    /// Linear prediction coefficients (x[n] ≈ Σ a[k]·x[n−k], k from 1) by the autocorrelation method and the
    /// Levinson–Durbin recursion, with a little regularisation so silence doesn't divide by zero.
    /// </summary>
    public static double[] Coefficients(double[] x, int order)
    {
        var r = new double[order + 1];
        for (var lag = 0; lag <= order; lag++)
        {
            double sum = 0;
            for (var n = lag; n < x.Length; n++)
            {
                sum += x[n] * x[n - lag];
            }

            r[lag] = sum;
        }

        r[0] = r[0] * 1.0001 + 1e-12;
        var a = new double[order + 1];
        var previous = new double[order + 1];
        var energy = r[0];
        for (var i = 1; i <= order; i++)
        {
            var acc = r[i];
            for (var k = 1; k < i; k++)
            {
                acc -= previous[k] * r[i - k];
            }

            var reflection = acc / energy;
            a[i] = reflection;
            for (var k = 1; k < i; k++)
            {
                a[k] = previous[k] - reflection * previous[i - k];
            }

            energy *= 1 - reflection * reflection;
            Array.Copy(a, previous, order + 1);
            if (energy <= 0)
            {
                break;
            }
        }

        return a;
    }
}

/// <summary>What background noise sounds like: its average level at each frequency, learned from a stretch of just noise.</summary>
public sealed class NoiseProfile
{
    public const int Size = 2048;
    public const int Hop = Size / 4;

    private NoiseProfile(double[][] magnitudes, int sampleRate)
    {
        Magnitudes = magnitudes;
        SampleRate = sampleRate;
    }

    /// <summary>Average magnitude per FFT bin, per channel.</summary>
    public double[][] Magnitudes { get; }

    public int SampleRate { get; }

    /// <summary>Learns the noise from a range that has nothing but noise in it (at least a quarter of a second).</summary>
    public static NoiseProfile? Learn(AudioBuffer audio, FrameRange range)
    {
        if (range.Length < Size * 2)
        {
            return null;
        }

        var bins = Size / 2 + 1;
        var window = AudioAnalysis.Hann(Size);
        var fft = new Fft(Size);
        var re = new double[Size];
        var im = new double[Size];
        var magnitudes = new double[audio.ChannelCount][];
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            var sum = new double[bins];
            var frames = 0;
            var samples = audio.Channels[c];
            for (var at = range.Start; at + Size <= range.End; at += Hop)
            {
                for (var n = 0; n < Size; n++)
                {
                    re[n] = samples[at + n] * window[n];
                    im[n] = 0;
                }

                fft.Transform(re, im, inverse: false);
                for (var b = 0; b < bins; b++)
                {
                    sum[b] += Math.Sqrt(re[b] * re[b] + im[b] * im[b]);
                }

                frames++;
            }

            magnitudes[c] = sum.Select(s => s / Math.Max(1, frames)).ToArray();
        }

        return new NoiseProfile(magnitudes, audio.SampleRate);
    }
}

/// <summary>
/// Spectral noise reduction: each moment's spectrum is compared with the noise profile, and frequencies not
/// clearly louder than the noise are turned down. The gains are smoothed across neighbouring frequencies
/// and over time, which avoids the warbly "musical noise" that a plain gate leaves behind.
/// </summary>
public static class NoiseReducer
{
    /// <param name="reductionDb">How far the noise is turned down (12 is gentle, 24 strong).</param>
    /// <param name="sensitivityDb">How far above the noise's average a frequency must be to count as music.</param>
    public static AudioBuffer Process(AudioBuffer input, NoiseProfile profile, double reductionDb, double sensitivityDb,
        Action<double>? progress = null, CancellationToken cancellation = default)
    {
        const int size = NoiseProfile.Size;
        const int hop = NoiseProfile.Hop;
        const int bins = size / 2 + 1;
        var channels = input.ChannelCount;
        var floor = Math.Pow(10, -Math.Abs(reductionDb) / 20);
        var factor = Math.Pow(10, sensitivityDb / 20);
        // Gains fall back towards the floor over about 60 ms, and rise at once.
        var releasePerHop = Math.Pow(floor, hop / (0.06 * input.SampleRate));
        var window = AudioAnalysis.Hann(size);
        var noise = Enumerable.Range(0, channels).Select(c => profile.Magnitudes[Math.Min(c, profile.Magnitudes.Length - 1)]).ToArray();

        var padded = input.Frames + 2 * size;
        var frameCount = (padded - size) / hop + 1;
        var output = Enumerable.Range(0, channels).Select(_ => new float[padded]).ToArray();
        var norm = new float[padded];
        var fft = new Fft(size);
        var re = Enumerable.Range(0, channels).Select(_ => new double[size]).ToArray();
        var im = Enumerable.Range(0, channels).Select(_ => new double[size]).ToArray();
        var gains = new double[bins];
        var ratios = new double[bins];
        var smoothed = new double[bins];
        var previous = Enumerable.Repeat(1.0, bins).ToArray();

        for (var f = 0; f < frameCount; f++)
        {
            if (f % 64 == 0)
            {
                cancellation.ThrowIfCancellationRequested();
                progress?.Invoke((double)f / frameCount);
            }

            var at = f * hop;
            for (var c = 0; c < channels; c++)
            {
                var samples = input.Channels[c];
                for (var n = 0; n < size; n++)
                {
                    var index = at + n - size;
                    re[c][n] = index >= 0 && index < samples.Length ? samples[index] * window[n] : 0;
                    im[c][n] = 0;
                }

                fft.Transform(re[c], im[c], inverse: false);
            }

            // One gain per frequency for all channels (the loudest channel's say), so the stereo image holds still.
            for (var b = 0; b < bins; b++)
            {
                var music = 0.0;
                for (var c = 0; c < channels; c++)
                {
                    var magnitude = Math.Sqrt(re[c][b] * re[c][b] + im[c][b] * im[c][b]);
                    music = Math.Max(music, magnitude / (noise[c][b] * factor + 1e-12));
                }

                ratios[b] = music;
            }

            // Judged on a few neighbouring frequencies together: a lone noise spike doesn't open the gate, a tone does.
            for (var b = 0; b < bins; b++)
            {
                var lo = Math.Max(0, b - 1);
                var hi = Math.Min(bins - 1, b + 1);
                double sum = 0;
                for (var k = lo; k <= hi; k++)
                {
                    sum += ratios[k];
                }

                gains[b] = sum / (hi - lo + 1) >= 1 ? 1 : floor;
            }

            for (var b = 0; b < bins; b++)
            {
                double sum = 0;
                var count = 0;
                for (var k = Math.Max(0, b - 3); k <= Math.Min(bins - 1, b + 3); k++)
                {
                    sum += gains[k];
                    count++;
                }

                var target = sum / count;
                smoothed[b] = Math.Max(Math.Max(target, floor), previous[b] * releasePerHop);
                previous[b] = smoothed[b];
            }

            for (var c = 0; c < channels; c++)
            {
                for (var b = 0; b < bins; b++)
                {
                    var g = smoothed[b];
                    re[c][b] *= g;
                    im[c][b] *= g;
                    if (b > 0 && b < bins - 1)
                    {
                        re[c][size - b] *= g;
                        im[c][size - b] *= g;
                    }
                }

                fft.Transform(re[c], im[c], inverse: true);
                var result = output[c];
                for (var n = 0; n < size; n++)
                {
                    result[at + n] += (float)(re[c][n] / size * window[n]);
                }
            }

            for (var n = 0; n < size; n++)
            {
                norm[at + n] += (float)(window[n] * window[n]);
            }
        }

        var channelsOut = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var result = new float[input.Frames];
            for (var i = 0; i < input.Frames; i++)
            {
                result[i] = output[c][i + size] / Math.Max(norm[i + size], 1e-3f);
            }

            channelsOut[c] = result;
        }

        progress?.Invoke(1);
        return new AudioBuffer(channelsOut, input.SampleRate);
    }
}
