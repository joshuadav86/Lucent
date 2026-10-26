using NAudio.Wave;

namespace Lucent.Editor;

/// <summary>Reads an <see cref="AudioBuffer"/> from start to end as interleaved samples, for saving it.</summary>
public sealed class AudioBufferSampleProvider : ISampleProvider
{
    private readonly AudioBuffer _audio;
    private int _position;

    public AudioBufferSampleProvider(AudioBuffer audio)
    {
        _audio = audio;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.ChannelCount);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _audio.ChannelCount;
        var frames = Math.Min(count / channels, _audio.Frames - _position);
        for (var i = 0; i < frames; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                buffer[offset + i * channels + c] = _audio.Channels[c][_position + i];
            }
        }

        _position += frames;
        return frames * channels;
    }
}
