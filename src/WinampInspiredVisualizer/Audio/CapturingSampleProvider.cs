using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

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
        var channels = Math.Max(1, WaveFormat.Channels);
        var frames = read / channels;
        if (_mono.Length < frames)
        {
            _mono = new float[frames];
            _left = new float[frames];
            _right = new float[frames];
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += buffer[offset + frame * channels + channel];
            }

            _mono[frame] = sum / channels;
            // Mono music plays the same on both sides; anything with more channels uses its front pair.
            _left[frame] = buffer[offset + frame * channels];
            _right[frame] = buffer[offset + frame * channels + Math.Min(1, channels - 1)];
        }

        _capture.Write(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
        return read;
    }
}
