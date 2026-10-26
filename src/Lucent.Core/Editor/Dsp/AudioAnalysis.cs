namespace Lucent.Editor.Dsp;

/// <summary>Measurements of a piece of audio, as a mastering engineer would look at it.</summary>
public sealed record AudioStats(
    double[] PeakDb,
    double[] TruePeakDb,
    double[] RmsDb,
    double[] DcOffset,
    double? IntegratedLufs,
    double? LoudnessRange,
    double? MaxMomentaryLufs,
    double? MaxShortTermLufs,
    double? Correlation,
    int ClippedSamples)
{
    /// <summary>The highest of the channels' values (the overall peak, say).</summary>
    public static double Highest(double[] values) => values.Length == 0 ? double.NegativeInfinity : values.Max();
}

/// <summary>Measuring audio: levels, loudness, clipping and the frequency spectrum.</summary>
public static class AudioAnalysis
{
    /// <summary>A sample this close to full scale counts as clipped when several come in a row.</summary>
    public const float ClipLevel = 0.9995f;

    /// <summary>How many full-scale samples in a row mean the waveform was cut off rather than just touching 0 dB.</summary>
    public const int ClipRun = 3;

    public static AudioStats Measure(AudioBuffer audio, FrameRange range, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        var channels = audio.ChannelCount;
        var peak = new double[channels];
        var truePeak = new double[channels];
        var rms = new double[channels];
        var dc = new double[channels];
        Parallel.For(0, channels, c =>
        {
            var samples = audio.Channels[c];
            double sum = 0, squares = 0, max = 0;
            for (var i = range.Start; i < range.End; i++)
            {
                double x = samples[i];
                sum += x;
                squares += x * x;
                max = Math.Max(max, Math.Abs(x));
            }

            var n = Math.Max(1, range.Length);
            peak[c] = ToDb(max);
            rms[c] = ToDb(Math.Sqrt(squares / n));
            dc[c] = sum / n;
            truePeak[c] = ToDb(TruePeakDetector.Measure(samples, range.Start, range.Length));
        });

        cancellation.ThrowIfCancellationRequested();
        progress?.Invoke(0.3);
        var slice = audio.Slice(range.Start, range.Length);
        var integrated = LoudnessMeter.IntegratedLoudness(slice, f => progress?.Invoke(0.3 + 0.3 * f));
        cancellation.ThrowIfCancellationRequested();
        var (momentary, shortTerm) = LoudnessBlocks(slice);
        progress?.Invoke(0.9);

        double? correlation = null;
        if (channels == 2 && range.Length > 0)
        {
            double lr = 0, ll = 0, rr = 0;
            var left = audio.Channels[0];
            var right = audio.Channels[1];
            for (var i = range.Start; i < range.End; i++)
            {
                lr += left[i] * (double)right[i];
                ll += left[i] * (double)left[i];
                rr += right[i] * (double)right[i];
            }

            correlation = ll > 0 && rr > 0 ? lr / Math.Sqrt(ll * rr) : null;
        }

        var clipped = FindClipping(audio, range).Sum(r => r.Length);
        progress?.Invoke(1);
        return new AudioStats(peak, truePeak, rms, dc, integrated, LoudnessRange(shortTerm),
            momentary.Count > 0 ? momentary.Max() : null, shortTerm.Count > 0 ? shortTerm.Max() : null, correlation, clipped);
    }

    /// <summary>
    /// The places where the waveform has been cut off flat at full scale: runs of at least <see cref="ClipRun"/>
    /// samples at or above <see cref="ClipLevel"/> in any channel, and any sample above full scale. Runs closer
    /// together than a few milliseconds are joined into one.
    /// </summary>
    public static List<FrameRange> FindClipping(AudioBuffer audio, FrameRange range)
    {
        var runs = new List<FrameRange>();
        foreach (var samples in audio.Channels)
        {
            var runStart = -1;
            for (var i = range.Start; i <= range.End; i++)
            {
                var hot = i < range.End && Math.Abs(samples[i]) >= ClipLevel;
                if (hot && runStart < 0)
                {
                    runStart = i;
                }
                else if (!hot && runStart >= 0)
                {
                    var length = i - runStart;
                    var over = false;
                    for (var k = runStart; k < i && !over; k++)
                    {
                        over = Math.Abs(samples[k]) > 1.0001f;
                    }

                    if (length >= ClipRun || over)
                    {
                        runs.Add(new FrameRange(runStart, length));
                    }

                    runStart = -1;
                }
            }
        }

        // Join runs from both channels, and runs within a millisecond of each other.
        var gap = audio.SampleRate / 1000;
        var joined = new List<FrameRange>();
        foreach (var run in runs.OrderBy(r => r.Start))
        {
            if (joined.Count > 0 && run.Start <= joined[^1].End + gap)
            {
                var last = joined[^1];
                joined[^1] = new FrameRange(last.Start, Math.Max(last.End, run.End) - last.Start);
            }
            else
            {
                joined.Add(run);
            }
        }

        return joined;
    }

    /// <summary>
    /// Momentary (400 ms) and short-term (3 s) loudness every 100 ms, in LUFS, leaving out silent blocks
    /// below the absolute gate. Used for the loudest moment and for the loudness range.
    /// </summary>
    public static (List<double> Momentary, List<double> ShortTerm) LoudnessBlocks(AudioBuffer audio)
    {
        var rate = audio.SampleRate;
        var step = Math.Max(1, (int)Math.Round(0.1 * rate));
        var steps = audio.Frames / step;
        var power = new double[steps];
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            var filter = new LoudnessMeter.KWeighting(rate);
            var samples = audio.Channels[c];
            for (var s = 0; s < steps; s++)
            {
                double sum = 0;
                for (var i = s * step; i < (s + 1) * step; i++)
                {
                    var y = filter.Process(samples[i]);
                    sum += y * y;
                }

                power[s] += sum / step;
            }
        }

        List<double> Blocks(int length)
        {
            var result = new List<double>();
            double sum = 0;
            for (var s = 0; s < steps; s++)
            {
                sum += power[s];
                if (s >= length)
                {
                    sum -= power[s - length];
                }

                if (s >= length - 1)
                {
                    var lufs = ToLufs(sum / length);
                    if (lufs > -70)
                    {
                        result.Add(lufs);
                    }
                }
            }

            return result;
        }

        return (Blocks(4), Blocks(30));
    }

    /// <summary>
    /// Loudness range (LRA) in LU, by EBU Tech 3342: the spread between the 10th and 95th percentiles of the
    /// short-term loudness, after gating out silence and blocks 20 LU below the average. Null for under 3 seconds.
    /// </summary>
    public static double? LoudnessRange(List<double> shortTerm)
    {
        if (shortTerm.Count < 2)
        {
            return null;
        }

        var mean = ToLufs(shortTerm.Average(FromLufs));
        var gated = shortTerm.Where(l => l > mean - 20).Order().ToList();
        if (gated.Count < 2)
        {
            return null;
        }

        double Percentile(double p)
        {
            var position = p * (gated.Count - 1);
            var low = (int)Math.Floor(position);
            var high = Math.Min(gated.Count - 1, low + 1);
            return gated[low] + (gated[high] - gated[low]) * (position - low);
        }

        return Percentile(0.95) - Percentile(0.10);
    }

    /// <summary>
    /// The average spectrum of a range, in dB per bin (bin b is b·rate/size Hz), from Hann-windowed FFTs half
    /// overlapping. Long ranges are sampled at up to <paramref name="maxFrames"/> evenly spread places.
    /// </summary>
    public static double[] AverageSpectrum(AudioBuffer audio, FrameRange range, int size = 8192, int maxFrames = 600)
    {
        var bins = size / 2 + 1;
        var power = new double[bins];
        if (range.Length <= 0)
        {
            return Enumerable.Repeat(-200.0, bins).ToArray();
        }

        var hop = size / 2;
        var count = Math.Max(1, (range.Length - size) / hop + 1);
        var stride = Math.Max(1, count / maxFrames);
        var window = Hann(size);
        var windowSum = window.Sum();
        var positions = Enumerable.Range(0, (count + stride - 1) / stride).Select(k => range.Start + k * stride * hop).ToList();
        var gate = new object();
        Parallel.ForEach(positions, () => (new Fft(size), new double[size], new double[size], new double[bins]), (at, _, local) =>
        {
            var (fft, re, im, sum) = local;
            foreach (var samples in audio.Channels)
            {
                for (var n = 0; n < size; n++)
                {
                    var i = at + n;
                    re[n] = i < range.End ? samples[i] * window[n] : 0;
                    im[n] = 0;
                }

                fft.Transform(re, im, inverse: false);
                for (var b = 0; b < bins; b++)
                {
                    sum[b] += re[b] * re[b] + im[b] * im[b];
                }
            }

            return local;
        }, local =>
        {
            lock (gate)
            {
                for (var b = 0; b < bins; b++)
                {
                    power[b] += local.Item4[b];
                }
            }
        });

        // Scaled so a full-scale sine reads 0 dB at its frequency.
        var scale = 1.0 / ((windowSum / 2) * (windowSum / 2) * positions.Count * audio.ChannelCount);
        return power.Select(p => 10 * Math.Log10(Math.Max(p * scale, 1e-20))).ToArray();
    }

    /// <summary>The frequencies with the most hum in them: 50 Hz (most of the world) or 60 Hz (the Americas), or null if neither stands out.</summary>
    public static double? DetectHum(AudioBuffer audio, FrameRange range)
    {
        const int size = 32768;
        var spectrum = AverageSpectrum(audio, range, size, maxFrames: 120);
        var binHz = audio.SampleRate / (double)size;

        double Prominence(double hz)
        {
            // How far the hum's harmonics stand above the spectrum just either side of them.
            var total = 0.0;
            for (var harmonic = 1; harmonic <= 4; harmonic++)
            {
                var bin = (int)Math.Round(hz * harmonic / binHz);
                if (bin + 12 >= spectrum.Length)
                {
                    break;
                }

                var peak = Enumerable.Range(bin - 1, 3).Max(b => spectrum[b]);
                var around = (Enumerable.Range(bin - 12, 6).Average(b => spectrum[b]) + Enumerable.Range(bin + 7, 6).Average(b => spectrum[b])) / 2;
                total += Math.Max(0, peak - around);
            }

            return total;
        }

        var fifty = Prominence(50);
        var sixty = Prominence(60);
        if (Math.Max(fifty, sixty) < 12)
        {
            return null;
        }

        return fifty >= sixty ? 50 : 60;
    }

    public static double[] Hann(int size)
    {
        var window = new double[size];
        for (var n = 0; n < size; n++)
        {
            window[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / size);
        }

        return window;
    }

    public static double ToDb(double amplitude) => amplitude <= 0 ? double.NegativeInfinity : 20 * Math.Log10(amplitude);

    private static double ToLufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);

    private static double FromLufs(double lufs) => Math.Pow(10, (lufs + 0.691) / 10);
}

/// <summary>
/// Meters for what's playing: sample and true peak per channel, and momentary, short-term and integrated
/// loudness (BS.1770) since playback started. Fed on the audio thread, read on the UI thread.
/// </summary>
public sealed class LiveMeter
{
    private readonly object _gate = new();
    private readonly int _channels;
    private readonly int _step;
    private readonly LoudnessMeter.KWeighting[] _weighting;
    private readonly TruePeakDetector[] _truePeak;
    private readonly double[] _stepPower = new double[30];
    private readonly float[] _peak;
    private readonly double[] _maxTruePeak;
    private double _accumulated;
    private int _inStep;
    private long _steps;
    // Gated-block bookkeeping for the integrated value: every 400 ms block above -70 LUFS.
    private readonly List<double> _blocks = new();

    public LiveMeter(int channels, int sampleRate)
    {
        _channels = channels;
        _step = Math.Max(1, sampleRate / 10);
        _weighting = Enumerable.Range(0, channels).Select(_ => new LoudnessMeter.KWeighting(sampleRate)).ToArray();
        _truePeak = Enumerable.Range(0, channels).Select(_ => new TruePeakDetector()).ToArray();
        _peak = new float[channels];
        _maxTruePeak = new double[channels];
    }

    /// <summary>Measures interleaved samples as they go to the sound card.</summary>
    public void Feed(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            var frames = count / _channels;
            for (var i = 0; i < frames; i++)
            {
                for (var c = 0; c < _channels; c++)
                {
                    var x = buffer[offset + i * _channels + c];
                    var level = Math.Abs(x);
                    if (level > _peak[c])
                    {
                        _peak[c] = level;
                    }

                    _maxTruePeak[c] = Math.Max(_maxTruePeak[c], _truePeak[c].Next(x));
                    var y = _weighting[c].Process(x);
                    _accumulated += y * y;
                }

                if (++_inStep == _step)
                {
                    _stepPower[_steps % _stepPower.Length] = _accumulated / _step;
                    _steps++;
                    _accumulated = 0;
                    _inStep = 0;
                    if (_steps >= 4 && Mean(4) is var block && ToLufs(block) > -70)
                    {
                        _blocks.Add(block);
                    }
                }
            }
        }
    }

    /// <summary>
    /// What to show now. Peaks are the highest since the last reading (so the meter falls back between
    /// readings); true peak is the highest since playback started.
    /// </summary>
    public MeterReading Read()
    {
        lock (_gate)
        {
            var peaks = _peak.Select(p => (double)p).ToArray();
            Array.Clear(_peak);
            double? integrated = null;
            if (_blocks.Count > 0)
            {
                var ungated = ToLufs(_blocks.Average());
                var gated = _blocks.Where(b => ToLufs(b) > ungated - 10).ToList();
                integrated = gated.Count > 0 ? ToLufs(gated.Average()) : null;
            }

            return new MeterReading(peaks, _maxTruePeak.Max(),
                _steps >= 4 ? ToLufs(Mean(4)) : null,
                _steps >= 30 ? ToLufs(Mean(30)) : null,
                integrated);
        }
    }

    private double Mean(int count)
    {
        double sum = 0;
        for (var k = 1; k <= count; k++)
        {
            sum += _stepPower[(_steps - k) % _stepPower.Length];
        }

        return sum / count;
    }

    private static double ToLufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);
}

/// <param name="Peaks">Linear sample peak per channel since the last reading.</param>
/// <param name="TruePeak">Linear true peak since playback started.</param>
public sealed record MeterReading(double[] Peaks, double TruePeak, double? Momentary, double? ShortTerm, double? Integrated);
