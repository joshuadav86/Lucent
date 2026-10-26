namespace Lucent.Editor.Dsp;

/// <summary>A compressor's settings.</summary>
public sealed class CompressorSettings
{
    /// <summary>Above this level (dBFS), the compressor starts turning things down.</summary>
    public double ThresholdDb { get; set; } = -18;

    /// <summary>How hard: 4 means every 4 dB over the threshold comes out as 1 dB over.</summary>
    public double Ratio { get; set; } = 3;

    public double AttackMs { get; set; } = 10;

    public double ReleaseMs { get; set; } = 150;

    /// <summary>How gradually it starts working around the threshold, in dB (0 is a hard corner).</summary>
    public double KneeDb { get; set; } = 6;

    /// <summary>Boost after compressing, to make up for the level it took away.</summary>
    public double MakeupDb { get; set; }

    public CompressorSettings Clone() => (CompressorSettings)MemberwiseClone();
}

/// <summary>A limiter's settings.</summary>
public sealed class LimiterSettings
{
    /// <summary>The level nothing goes above, in dBFS (dBTP when <see cref="TruePeak"/> is on).</summary>
    public double CeilingDb { get; set; } = -1;

    /// <summary>Boost before limiting, to make the music louder.</summary>
    public double InputGainDb { get; set; }

    public double ReleaseMs { get; set; } = 80;

    /// <summary>Also catches the peaks between samples, which can clip after converting to MP3 or in a DAC.</summary>
    public bool TruePeak { get; set; } = true;

    public LimiterSettings Clone() => (LimiterSettings)MemberwiseClone();
}

/// <summary>
/// A feed-forward compressor with a soft knee. Both channels are turned down together (stereo-linked),
/// so the stereo image doesn't wander. Its settings can be changed while it plays.
/// </summary>
public sealed class Compressor : IStreamProcessor
{
    private readonly int _sampleRate;
    private CompressorSettings _settings;
    private CompressorSettings? _pending;
    private double _reductionDb;

    public Compressor(CompressorSettings settings, int sampleRate)
    {
        _settings = settings.Clone();
        _sampleRate = sampleRate;
    }

    public int Latency => 0;

    /// <summary>How much it's turning down right now, in dB (positive), for a meter.</summary>
    public double ReductionDb => Volatile.Read(ref _reductionDb);

    public void Update(CompressorSettings settings) => Volatile.Write(ref _pending, settings.Clone());

    public void Reset() => _reductionDb = 0;

    public void Process(float[][] planes, int offset, int count)
    {
        if (Interlocked.Exchange(ref _pending, null) is { } next)
        {
            _settings = next;
        }

        var s = _settings;
        var attack = Coefficient(s.AttackMs);
        var release = Coefficient(s.ReleaseMs);
        var makeup = s.MakeupDb;
        var reduction = _reductionDb;
        for (var i = offset; i < offset + count; i++)
        {
            var peak = 0f;
            foreach (var plane in planes)
            {
                peak = Math.Max(peak, Math.Abs(plane[i]));
            }

            var target = GainReductionDb(peak <= 1e-9f ? -180 : 20 * Math.Log10(peak), s);
            // Clamps down at the attack speed, lets go at the release speed.
            var coefficient = target > reduction ? attack : release;
            reduction = target + (reduction - target) * coefficient;
            var gain = (float)Math.Pow(10, (makeup - reduction) / 20);
            foreach (var plane in planes)
            {
                plane[i] *= gain;
            }
        }

        Volatile.Write(ref _reductionDb, reduction);
    }

    /// <summary>How far a level is turned down, in dB, before smoothing.</summary>
    public static double GainReductionDb(double levelDb, CompressorSettings s)
    {
        var over = levelDb - s.ThresholdDb;
        var knee = Math.Max(0, s.KneeDb);
        var slope = 1 - 1 / Math.Max(1, s.Ratio);
        if (over <= -knee / 2)
        {
            return 0;
        }

        if (knee > 0 && over < knee / 2)
        {
            var x = over + knee / 2;
            return slope * x * x / (2 * knee);
        }

        return slope * over;
    }

    private double Coefficient(double ms) => Math.Exp(-1.0 / Math.Max(1, ms * 0.001 * _sampleRate));
}

/// <summary>
/// A brickwall limiter that looks ahead: it sees each peak a few milliseconds before it plays, and has the
/// level down by the time it arrives, so nothing passes the ceiling and there's no distortion from clamping.
/// With true peak on, it also catches the peaks between samples (measured 4× oversampled, as BS.1770 does).
/// </summary>
public sealed class Limiter : IStreamProcessor
{
    private readonly int _sampleRate;
    private readonly int _lookahead;
    private readonly int _delay;
    private readonly float[][] _delayLine;
    private readonly TruePeakDetector[] _detectors;
    // The lowest gain needed over the last lookahead + 1 frames, kept as a monotonic queue.
    private readonly (long Index, double Gain)[] _minQueue;
    private int _queueHead, _queueCount;
    // The running average of the held gain over the lookahead, so the gain glides down in time for each peak.
    private readonly double[] _averageRing;
    private double _averageSum;
    private long _position;
    private double _held = 1;
    private LimiterSettings _settings;
    private LimiterSettings? _pending;
    private double _reductionDb;

    public Limiter(LimiterSettings settings, int channels, int sampleRate)
    {
        _settings = settings.Clone();
        _sampleRate = sampleRate;
        _lookahead = Math.Max(8, (int)(0.005 * sampleRate));
        _delay = _lookahead + TruePeakDetector.Delay;
        _delayLine = Enumerable.Range(0, channels).Select(_ => new float[_delay + 1]).ToArray();
        _detectors = Enumerable.Range(0, channels).Select(_ => new TruePeakDetector()).ToArray();
        _minQueue = new (long, double)[_lookahead + 2];
        _averageRing = new double[_lookahead];
        Reset();
    }

    public int Latency => _delay;

    public double ReductionDb => Volatile.Read(ref _reductionDb);

    public void Update(LimiterSettings settings) => Volatile.Write(ref _pending, settings.Clone());

    public void Reset()
    {
        foreach (var line in _delayLine)
        {
            Array.Clear(line);
        }

        foreach (var detector in _detectors)
        {
            detector.Reset();
        }

        _queueHead = 0;
        _queueCount = 0;
        Array.Fill(_averageRing, 1.0);
        _averageSum = _lookahead;
        _position = 0;
        _held = 1;
        _reductionDb = 0;
    }

    public void Process(float[][] planes, int offset, int count)
    {
        if (Interlocked.Exchange(ref _pending, null) is { } next)
        {
            _settings = next;
        }

        var s = _settings;
        var ceiling = Math.Pow(10, s.CeilingDb / 20);
        var input = (float)Math.Pow(10, s.InputGainDb / 20);
        var release = Math.Exp(-1.0 / Math.Max(1, s.ReleaseMs * 0.001 * _sampleRate));
        var channels = Math.Min(planes.Length, _delayLine.Length);
        var ringLength = _delay + 1;
        var gain = 1.0;
        for (var i = offset; i < offset + count; i++)
        {
            // How loud this moment is, and the gain it needs.
            var slot = (int)(_position % ringLength);
            double peak = 0;
            for (var c = 0; c < channels; c++)
            {
                var x = planes[c][i] * input;
                _delayLine[c][slot] = x;
                var level = s.TruePeak ? _detectors[c].Next(x) : Math.Abs(_detectors[c].Delayed(x));
                peak = Math.Max(peak, level);
            }

            var needed = peak > ceiling ? ceiling / peak : 1.0;

            // The lowest needed gain over the window.
            while (_queueCount > 0 && _minQueue[(_queueHead + _queueCount - 1) % _minQueue.Length].Gain >= needed)
            {
                _queueCount--;
            }

            _minQueue[(_queueHead + _queueCount) % _minQueue.Length] = (_position, needed);
            _queueCount++;
            while (_minQueue[_queueHead].Index <= _position - _lookahead - 1)
            {
                _queueHead = (_queueHead + 1) % _minQueue.Length;
                _queueCount--;
            }

            var lowest = _minQueue[_queueHead].Gain;
            // Down at once (the averaging below makes that a glide), back up at the release speed.
            _held = lowest < _held ? lowest : lowest + (_held - lowest) * release;
            var ring = (int)(_position % _lookahead);
            _averageSum += _held - _averageRing[ring];
            _averageRing[ring] = _held;
            gain = Math.Min(1.0, _averageSum / _lookahead);

            // Out comes the audio from the delay ago, turned down by the gain worked out for it.
            var outSlot = (int)((_position + 1) % ringLength);
            for (var c = 0; c < channels; c++)
            {
                planes[c][i] = (float)(_delayLine[c][outSlot] * gain);
            }

            _position++;
        }

        Volatile.Write(ref _reductionDb, -20 * Math.Log10(Math.Max(gain, 1e-9)));
    }
}

/// <summary>
/// Estimates the true (between-sample) peak of a stream by interpolating three points between each pair
/// of samples with a 48-tap windowed-sinc filter, as ITU-R BS.1770 describes.
/// </summary>
public sealed class TruePeakDetector
{
    private const int Phases = 4;
    private const int TapsPerPhase = 12;

    /// <summary>How many samples behind the input the interpolated points are.</summary>
    public const int Delay = TapsPerPhase / 2;

    private static readonly double[][] Kernel = BuildKernel();
    private readonly double[] _history = new double[TapsPerPhase];
    private int _at;

    public void Reset()
    {
        Array.Clear(_history);
        _at = 0;
    }

    /// <summary>Feeds a sample and returns the highest absolute level around <see cref="Delay"/> samples ago.</summary>
    public double Next(double x)
    {
        _history[_at] = x;
        _at = (_at + 1) % TapsPerPhase;
        var peak = Math.Abs(_history[(_at + TapsPerPhase - 1 - Delay) % TapsPerPhase]);
        for (var p = 1; p < Phases; p++)
        {
            double sum = 0;
            var taps = Kernel[p];
            for (var k = 0; k < TapsPerPhase; k++)
            {
                // Tap k reads the sample k steps back from the newest.
                sum += taps[k] * _history[(_at + TapsPerPhase - 1 - k) % TapsPerPhase];
            }

            peak = Math.Max(peak, Math.Abs(sum));
        }

        return peak;
    }

    /// <summary>Feeds a sample and returns the one from <see cref="Delay"/> samples ago, without interpolating.</summary>
    public double Delayed(double x)
    {
        _history[_at] = x;
        _at = (_at + 1) % TapsPerPhase;
        return _history[(_at + TapsPerPhase - 1 - Delay) % TapsPerPhase];
    }

    /// <summary>The true peak of a whole channel, as a linear level.</summary>
    public static double Measure(float[] samples, int start, int length)
    {
        var detector = new TruePeakDetector();
        double peak = 0;
        var end = start + length;
        for (var i = start; i < end + Delay; i++)
        {
            peak = Math.Max(peak, detector.Next(i < end ? samples[i] : 0));
        }

        return peak;
    }

    /// <summary>
    /// The polyphase interpolation filter: phase p gives the point p/4 of the way from one sample to the next,
    /// for the samples around <see cref="Delay"/> ago.
    /// </summary>
    private static double[][] BuildKernel()
    {
        var kernel = new double[Phases][];
        for (var p = 0; p < Phases; p++)
        {
            kernel[p] = new double[TapsPerPhase];
            double sum = 0;
            for (var k = 0; k < TapsPerPhase; k++)
            {
                // The point wanted sits at (Delay - p/4) samples behind the newest; tap k is k samples behind.
                var x = k - (Delay - p / (double)Phases);
                var sinc = Math.Abs(x) < 1e-12 ? 1.0 : Math.Sin(Math.PI * x) / (Math.PI * x);
                var r = x / (TapsPerPhase / 2.0 + 0.5);
                var window = Math.Abs(r) >= 1 ? 0 : Math.Pow(Math.Cos(Math.PI * r / 2), 2);
                kernel[p][k] = sinc * window;
                sum += kernel[p][k];
            }

            // Each phase passes a steady level unchanged.
            for (var k = 0; k < TapsPerPhase; k++)
            {
                kernel[p][k] /= sum;
            }
        }

        return kernel;
    }
}

/// <summary>Runs an <see cref="IStreamProcessor"/> over a whole piece of audio.</summary>
public static class StreamRender
{
    /// <summary>
    /// Processes the audio in place, in blocks, allowing for the processor's latency (it's fed silence at the
    /// end and its output is moved back), so the result lines up with the input exactly.
    /// </summary>
    public static AudioBuffer Run(AudioBuffer audio, IStreamProcessor processor, Action<double>? progress, CancellationToken cancellation)
    {
        const int block = 4096;
        processor.Reset();
        var latency = processor.Latency;
        var frames = audio.Frames;
        var total = frames + latency;
        var planes = Enumerable.Range(0, audio.ChannelCount).Select(_ => new float[block]).ToArray();
        for (var start = 0; start < total; start += block)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = Math.Min(block, total - start);
            for (var c = 0; c < planes.Length; c++)
            {
                var available = Math.Clamp(frames - start, 0, count);
                Array.Copy(audio.Channels[c], start, planes[c], 0, available);
                Array.Clear(planes[c], available, count - available);
            }

            processor.Process(planes, 0, count);
            for (var c = 0; c < planes.Length; c++)
            {
                // Output frame j belongs to input frame j - latency.
                var to = start - latency;
                var from = Math.Max(0, -to);
                var length = Math.Min(count, frames - to) - from;
                if (length > 0)
                {
                    Array.Copy(planes[c], from, audio.Channels[c], to + from, length);
                }
            }

            progress?.Invoke((double)Math.Min(total, start + count) / total);
        }

        return audio;
    }
}
