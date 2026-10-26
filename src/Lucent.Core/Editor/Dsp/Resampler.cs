using System.Numerics;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Lucent.Editor.Dsp;

/// <summary>
/// Band-limited (windowed sinc) resampling: changes the sample rate, or the speed and pitch
/// together like a tape running faster or slower, without adding aliasing.
/// </summary>
/// <remarks>
/// A Kaiser-windowed sinc with 64 zero crossings on each side, about 100 dB of stopband
/// rejection, and a cutoff at 96% of the lower Nyquist frequency. The filter is centred on
/// each output sample, so the result lines up exactly with the input (no added delay).
/// </remarks>
public sealed class SincKernel
{
    private const int ZeroCrossings = 64;
    private const int Phases = 512;
    private const double Beta = 10.0;
    private const double Bandwidth = 0.96;

    private readonly float[] _table;

    /// <param name="step">How many input samples each output sample advances (input rate / output rate).</param>
    public SincKernel(double step)
    {
        // Only lowering the rate needs a lower cutoff; raising it keeps the full band.
        var scale = Math.Min(1.0, 1.0 / step);
        var cutoff = 0.5 * scale * Bandwidth;
        var halfWidth = ZeroCrossings / scale;
        HalfTaps = (int)Math.Ceiling(halfWidth);
        var taps = 2 * HalfTaps;
        // Rows are padded to a whole number of SIMD vectors so the dot products need no tail loop.
        RowLength = (taps + Vector<float>.Count - 1) / Vector<float>.Count * Vector<float>.Count;
        _table = new float[(Phases + 1) * RowLength];

        var besselBeta = BesselI0(Beta);
        for (var p = 0; p <= Phases; p++)
        {
            var frac = (double)p / Phases;
            for (var j = 0; j < taps; j++)
            {
                // Tap j sits at input index floor(t) - HalfTaps + 1 + j.
                var x = j - HalfTaps + 1 - frac;
                var r = x / halfWidth;
                if (Math.Abs(r) >= 1)
                {
                    continue;
                }

                var window = BesselI0(Beta * Math.Sqrt(1 - r * r)) / besselBeta;
                var arg = 2 * cutoff * x;
                var sinc = Math.Abs(arg) < 1e-12 ? 1.0 : Math.Sin(Math.PI * arg) / (Math.PI * arg);
                _table[p * RowLength + j] = (float)(2 * cutoff * sinc * window);
            }
        }
    }

    /// <summary>How many input samples either side of the output time the filter reads.</summary>
    public int HalfTaps { get; }

    private int RowLength { get; }

    /// <summary>
    /// The value at fractional input position <paramref name="time"/>. <paramref name="input"/> holds
    /// input samples starting at absolute index <paramref name="offset"/>; anything outside it counts as silence.
    /// </summary>
    public float Sample(float[] input, long offset, int length, double time)
    {
        var whole = (long)Math.Floor(time);
        var frac = time - whole;
        var phase = frac * Phases;
        var row = (int)phase;
        var mix = (float)(phase - row);
        var first = whole - HalfTaps + 1 - offset;

        ReadOnlySpan<float> window;
        if (first >= 0 && first + RowLength <= length)
        {
            window = input.AsSpan((int)first, RowLength);
        }
        else
        {
            // Near the ends: copy what exists and treat the rest as silence.
            var padded = new float[RowLength];
            for (var j = 0; j < 2 * HalfTaps; j++)
            {
                var index = first + j;
                if (index >= 0 && index < length)
                {
                    padded[j] = input[index];
                }
            }

            window = padded;
        }

        var a = Dot(window, _table.AsSpan(row * RowLength, RowLength));
        var b = mix == 0 ? a : Dot(window, _table.AsSpan((row + 1) * RowLength, RowLength));
        return a + (b - a) * mix;
    }

    private static float Dot(ReadOnlySpan<float> x, ReadOnlySpan<float> h)
    {
        var xs = MemoryMarshal.Cast<float, Vector<float>>(x);
        var hs = MemoryMarshal.Cast<float, Vector<float>>(h);
        var sum = Vector<float>.Zero;
        for (var i = 0; i < xs.Length; i++)
        {
            sum += xs[i] * hs[i];
        }

        return Vector.Dot(sum, Vector<float>.One);
    }

    private static double BesselI0(double x)
    {
        double sum = 1, term = 1;
        for (var k = 1; k < 64; k++)
        {
            term *= x * x / (4.0 * k * k);
            sum += term;
            if (term < sum * 1e-17)
            {
                break;
            }
        }

        return sum;
    }
}

/// <summary>Resamples whole buffers in memory.</summary>
public static class Resampler
{
    /// <summary>
    /// Resamples every channel by <paramref name="step"/> input samples per output sample.
    /// A step above 1 shortens the audio (faster and higher, like a tape sped up); below 1 lengthens it.
    /// </summary>
    /// <param name="outputFrames">The exact output length, or null for the natural length (input / step).</param>
    public static AudioBuffer Resample(AudioBuffer input, double step, int outputSampleRate, int? outputFrames = null,
        Action<double>? progress = null, CancellationToken cancellation = default)
    {
        var frames = outputFrames ?? (int)Math.Ceiling(input.Frames / step);
        var kernel = new SincKernel(step);
        var output = new float[input.ChannelCount][];
        var total = (double)input.ChannelCount * frames;
        var done = 0L;

        for (var c = 0; c < input.ChannelCount; c++)
        {
            var source = input.Channels[c];
            var result = new float[frames];
            const int chunk = 1 << 16;
            for (var start = 0; start < frames; start += chunk)
            {
                cancellation.ThrowIfCancellationRequested();
                var end = Math.Min(frames, start + chunk);
                // Chunks are independent, so use every core.
                Parallel.For(start / 4096, (end + 4095) / 4096, block =>
                {
                    var from = Math.Max(start, block * 4096);
                    var to = Math.Min(end, block * 4096 + 4096);
                    for (var n = from; n < to; n++)
                    {
                        result[n] = kernel.Sample(source, 0, source.Length, n * step);
                    }
                });
                done += end - start;
                progress?.Invoke(done / total);
            }

            output[c] = result;
        }

        return new AudioBuffer(output, outputSampleRate);
    }
}

/// <summary>Changes the sample rate of a stream of audio as it is read, for converting files.</summary>
public sealed class ResamplingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly double _step;
    private readonly SincKernel _kernel;
    private readonly float[][] _history;
    private readonly float[] _readBuffer;
    private long _historyStart;
    private int _historyLength;
    private long _sourceFrames;
    private bool _sourceEnded;
    private long _outputIndex;

    public ResamplingSampleProvider(ISampleProvider source, int outputSampleRate)
    {
        _source = source;
        _channels = source.WaveFormat.Channels;
        _step = (double)source.WaveFormat.SampleRate / outputSampleRate;
        _kernel = new SincKernel(_step);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputSampleRate, _channels);
        // Enough room for the filter plus one read from the source.
        var capacity = 4 * _kernel.HalfTaps + 16384;
        _history = Enumerable.Range(0, _channels).Select(_ => new float[capacity]).ToArray();
        _readBuffer = new float[4096 * _channels];
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / _channels;
        var written = 0;
        while (written < frames)
        {
            var time = _outputIndex * _step;
            if (_sourceEnded && time >= _sourceFrames)
            {
                break;
            }

            var needed = (long)Math.Floor(time) + _kernel.HalfTaps + 1;
            if (!_sourceEnded && needed > _historyStart + _historyLength)
            {
                Fill((long)Math.Floor(time) - _kernel.HalfTaps);
                continue;
            }

            for (var c = 0; c < _channels; c++)
            {
                buffer[offset + written * _channels + c] = _kernel.Sample(_history[c], _historyStart, _historyLength, time);
            }

            written++;
            _outputIndex++;
        }

        return written * _channels;
    }

    /// <summary>Drops samples the filter no longer needs, then reads more from the source.</summary>
    private void Fill(long keepFrom)
    {
        var drop = (int)Math.Clamp(keepFrom - _historyStart, 0, _historyLength);
        if (drop > 0)
        {
            foreach (var channel in _history)
            {
                Array.Copy(channel, drop, channel, 0, _historyLength - drop);
            }

            _historyStart += drop;
            _historyLength -= drop;
        }

        var room = Math.Min(_history[0].Length - _historyLength, _readBuffer.Length / _channels);
        var read = _source.Read(_readBuffer, 0, room * _channels) / _channels;
        if (read == 0)
        {
            _sourceEnded = true;
            return;
        }

        for (var i = 0; i < read; i++)
        {
            for (var c = 0; c < _channels; c++)
            {
                _history[c][_historyLength + i] = _readBuffer[i * _channels + c];
            }
        }

        _historyLength += read;
        _sourceFrames += read;
    }
}
