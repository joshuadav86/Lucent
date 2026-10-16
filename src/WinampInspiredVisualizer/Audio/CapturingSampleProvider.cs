using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>Passes audio through unchanged while copying a mono mix into an <see cref="AudioCapture"/>.</summary>
public sealed class CapturingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly AudioCapture _capture;
    private float[] _mono = Array.Empty<float>();

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
        }

        for (var frame = 0; frame < frames; frame++)
        {
            var sum = 0f;
            for (var channel = 0; channel < channels; channel++)
            {
                sum += buffer[offset + frame * channels + channel];
            }

            _mono[frame] = sum / channels;
        }

        _capture.Write(_mono.AsSpan(0, frames));
        return read;
    }
}
