using System.IO;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Export;

/// <summary>Writes audio to a file in the format and quality the user chose.</summary>
public static class AudioEncoder
{
    /// <summary>
    /// Writes an MP3 file, using whichever encoder the app has on its platform. It gets the source,
    /// the path, the quality, a callback for frames written and a cancellation token, and returns a
    /// note for the user if the quality had to change. Each app sets this when it starts.
    /// </summary>
    public static Func<ISampleProvider, string, Mp3Quality, Action<long>, CancellationToken, string?>? Mp3Encoder { get; set; }

    /// <summary>
    /// Reads <paramref name="source"/> to the end and writes it to <paramref name="path"/>, resampling first if
    /// the options ask for another sample rate. Returns a note for the user if something had to change.
    /// </summary>
    /// <param name="expectedFrames">Roughly how long the source is, for progress (0 if unknown).</param>
    public static string? Encode(ISampleProvider source, string path, ExportOptions options, long expectedFrames,
        Action<double>? progress, CancellationToken cancellation)
    {
        string? note = null;
        var maxChannels = options.Format switch
        {
            ExportFormat.Mp3 => 2,
            ExportFormat.Flac => 8,
            _ => 32,
        };
        if (source.WaveFormat.Channels > maxChannels)
        {
            note = $"{options.Format.ToString().ToUpperInvariant()} holds at most {maxChannels} channels, so only the first {maxChannels} were kept.";
            source = new ChannelLimiter(source, maxChannels);
        }

        var rate = options.OutputSampleRate(source.WaveFormat.SampleRate);
        if (rate != source.WaveFormat.SampleRate)
        {
            expectedFrames = (long)(expectedFrames * (double)rate / source.WaveFormat.SampleRate);
            source = new ResamplingSampleProvider(source, rate);
        }

        long written = 0;
        void Advance(long frames)
        {
            written += frames;
            if (expectedFrames > 0)
            {
                progress?.Invoke(Math.Min(1, (double)written / expectedFrames));
            }
        }

        switch (options.Format)
        {
            case ExportFormat.Mp3:
                var mp3 = Mp3Encoder ?? throw new NotSupportedException("MP3 export isn't available on this platform.");
                note = mp3(source, path, options.Mp3Quality, Advance, cancellation) ?? note;
                break;
            case ExportFormat.Flac:
                WriteFlac(source, path, options, Advance, cancellation);
                break;
            default:
                WritePcm(source, path, options, Advance, cancellation);
                break;
        }

        progress?.Invoke(1);
        return note;
    }

    private static void WriteFlac(ISampleProvider source, string path, ExportOptions options, Action<long> advance,
        CancellationToken cancellation)
    {
        var format = source.WaveFormat;
        var bits = options.FlacBitDepth >= 24 ? 24 : 16;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var encoder = new FlacEncoder(stream, format.SampleRate, format.Channels, bits, options.FlacCompression);
        var quantizer = new PcmQuantizer(bits);
        var buffer = new float[16384 * format.Channels];
        var samples = new int[buffer.Length];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            quantizer.Quantize(buffer.AsSpan(0, read), samples);
            encoder.Write(samples.AsSpan(0, read));
            advance(read / format.Channels);
        }

        encoder.Finish();
    }

    /// <summary>WAV or AIFF: plain samples, 16- or 24-bit whole numbers or 32-bit floats.</summary>
    private static void WritePcm(ISampleProvider source, string path, ExportOptions options, Action<long> advance,
        CancellationToken cancellation)
    {
        var input = source.WaveFormat;
        var aiff = options.Format == ExportFormat.Aiff;
        // AIFF has no float samples in its plain form, so 32-bit float becomes 24-bit there.
        var depth = aiff && options.WavDepth == PcmDepth.Float32 ? PcmDepth.Int24 : options.WavDepth;
        var format = depth switch
        {
            PcmDepth.Int16 => new WaveFormat(input.SampleRate, 16, input.Channels),
            PcmDepth.Int24 => new WaveFormat(input.SampleRate, 24, input.Channels),
            _ => WaveFormat.CreateIeeeFloatWaveFormat(input.SampleRate, input.Channels),
        };

        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using Stream writer = aiff ? new AiffFileWriter(stream, format) : new WaveFileWriter(stream, format);
        var quantizer = depth == PcmDepth.Float32 ? null : new PcmQuantizer(format.BitsPerSample);
        var bytesPerSample = format.BitsPerSample / 8;
        var buffer = new float[16384 * input.Channels];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            // Sized exactly, because NAudio's AIFF writer byte-swaps the whole array it is given.
            var bytes = new byte[read * bytesPerSample];
            if (quantizer is null)
            {
                Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
            }
            else
            {
                for (var i = 0; i < read; i++)
                {
                    var value = quantizer.Quantize(buffer[i]);
                    for (var b = 0; b < bytesPerSample; b++)
                    {
                        bytes[i * bytesPerSample + b] = (byte)(value >> (8 * b));
                    }
                }
            }

            writer.Write(bytes, 0, bytes.Length);
            advance(read / input.Channels);
        }
    }

    /// <summary>Keeps only the first few channels of a stream.</summary>
    private sealed class ChannelLimiter : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _sourceChannels;
        private float[] _buffer = Array.Empty<float>();

        public ChannelLimiter(ISampleProvider source, int channels)
        {
            _source = source;
            _sourceChannels = source.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, channels);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var frames = count / WaveFormat.Channels;
            if (_buffer.Length < frames * _sourceChannels)
            {
                _buffer = new float[frames * _sourceChannels];
            }

            var read = _source.Read(_buffer, 0, frames * _sourceChannels) / _sourceChannels;
            for (var i = 0; i < read; i++)
            {
                for (var c = 0; c < WaveFormat.Channels; c++)
                {
                    buffer[offset + i * WaveFormat.Channels + c] = _buffer[i * _sourceChannels + c];
                }
            }

            return read * WaveFormat.Channels;
        }
    }
}
