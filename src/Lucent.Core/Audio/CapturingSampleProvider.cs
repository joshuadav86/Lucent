using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>Passes audio through unchanged while copying a mono mix and the left and right channels into an <see cref="AudioCapture"/>.</summary>
public sealed class CapturingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly AudioCapture _capture;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    public CapturingSampleProvider(ISampleProvider source, AudioCapture capture)
    {
        _source = source;
        _capture = capture;
        _capture.Reset(source.WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        var frames = Split(buffer, offset, read, WaveFormat.Channels, ref _mono, ref _left, ref _right);
        _capture.Write(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
        return read;
    }

    /// <summary>
    /// Splits <paramref name="count"/> interleaved samples into a mono mix and the left and right channels, growing
    /// the arrays if they're too small, and returns how many frames there were.
    /// </summary>
    internal static int Split(float[] buffer, int offset, int count, int channelCount, ref float[] mono, ref float[] left,
        ref float[] right)
    {
        var channels = Math.Max(1, channelCount);
        var frames = count / channels;
        if (mono.Length < frames)
        {
            mono = new float[frames];
            left = new float[frames];
            right = new float[frames];
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += buffer[offset + frame * channels + channel];
            }

            mono[frame] = sum / channels;
            // Mono music plays the same on both sides; anything with more channels uses its front pair.
            left[frame] = buffer[offset + frame * channels];
            right[frame] = buffer[offset + frame * channels + Math.Min(1, channels - 1)];
        }

        return frames;
    }
}
