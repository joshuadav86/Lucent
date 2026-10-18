using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// A song's sound for a video, decoded from its file from start to end: 16-bit stereo for the
/// video's soundtrack, and the newest moments as a mono mix for the visuals to react to, just as
/// the visuals on screen hear what's going to the speakers.
/// </summary>
/// <remarks>
/// The sound comes out at 44.1 or 48 kHz, the rates Windows' AAC encoder takes: songs at either keep
/// theirs, anything else (96 kHz, say) is resampled to 48 kHz. Mono songs play on both sides, and
/// songs with more than two channels keep their front pair.
/// </remarks>
public sealed class RecordingAudio : IDisposable
{
    private readonly WaveStream _reader;
    private readonly ISampleProvider _stereo;
    private readonly float[] _latest = new float[AudioAnalyzer.WindowSamples];
    private float[] _scratch = Array.Empty<float>();
    private bool _ended;

    public RecordingAudio(string path)
    {
        _reader = AudioReaderFactory.Open(path);
        try
        {
            var source = _reader.ToSampleProvider();
            var stereo = source.WaveFormat.Channels switch
            {
                1 => new MonoToStereoSampleProvider(source),
                2 => source,
                _ => new MultiplexingSampleProvider(new[] { source }, 2),
            };
            var rate = stereo.WaveFormat.SampleRate;
            _stereo = rate is 44100 or 48000 ? stereo : new WdlResamplingSampleProvider(stereo, 48000);
            Duration = _reader.TotalTime;
        }
        catch (Exception)
        {
            _reader.Dispose();
            throw;
        }
    }

    /// <summary>44100 or 48000.</summary>
    public int SampleRate => _stereo.WaveFormat.SampleRate;

    public TimeSpan Duration { get; }

    /// <summary>The newest <see cref="AudioAnalyzer.WindowSamples"/> samples read, as a mono mix (silence before the song starts).</summary>
    public ReadOnlySpan<float> Latest => _latest;

    /// <summary>Reads past the first <paramref name="samples"/> samples (per channel).</summary>
    public void Skip(long samples, CancellationToken cancellation)
    {
        // Decoding through is exact, where seeking some formats only gets near.
        var buffer = Array.Empty<byte>();
        const int Chunk = 1 << 15;
        while (samples > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            var count = (int)Math.Min(samples, Chunk);
            Read(count, ref buffer);
            samples -= count;
        }
    }

    /// <summary>
    /// Reads the next <paramref name="samples"/> samples (per channel) into <paramref name="pcm"/> as
    /// 16-bit stereo, growing it if need be, and returns how many bytes that is. Past the end of the
    /// song it's silence, so the soundtrack always runs as long as the pictures.
    /// </summary>
    public int Read(int samples, ref byte[] pcm)
    {
        var values = samples * 2;
        if (_scratch.Length < values)
        {
            _scratch = new float[values];
        }

        var filled = 0;
        while (filled < values && !_ended)
        {
            var read = _stereo.Read(_scratch, filled, values - filled);
            if (read <= 0)
            {
                _ended = true;
                break;
            }

            filled += read;
        }

        Array.Clear(_scratch, filled, values - filled);

        var bytes = values * 2;
        if (pcm.Length < bytes)
        {
            pcm = new byte[bytes];
        }

        for (var i = 0; i < values; i++)
        {
            var value = (short)Math.Round(Math.Clamp(_scratch[i], -1f, 1f) * short.MaxValue);
            pcm[i * 2] = (byte)value;
            pcm[i * 2 + 1] = (byte)(value >> 8);
        }

        RememberLatest(samples);
        return bytes;
    }

    /// <summary>Moves the mono mix of the samples just read onto the end of <see cref="Latest"/>.</summary>
    private void RememberLatest(int samples)
    {
        var length = _latest.Length;
        var kept = Math.Max(0, length - samples);
        Array.Copy(_latest, length - kept, _latest, 0, kept);
        var first = samples - (length - kept);
        for (var i = 0; i < length - kept; i++)
        {
            var frame = first + i;
            _latest[kept + i] = (_scratch[frame * 2] + _scratch[frame * 2 + 1]) * 0.5f;
        }
    }

    public void Dispose() => _reader.Dispose();
}
