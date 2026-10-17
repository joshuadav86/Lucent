namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// A piece of audio held in memory as 32-bit float samples, one array per channel
/// (one channel for mono, two for stereo). Samples are nominally between -1 and 1.
/// </summary>
public sealed class AudioBuffer
{
    public AudioBuffer(float[][] channels, int sampleRate)
    {
        if (channels.Length == 0)
        {
            throw new ArgumentException("Audio needs at least one channel.", nameof(channels));
        }

        var frames = channels[0].Length;
        if (channels.Any(channel => channel.Length != frames))
        {
            throw new ArgumentException("Every channel must have the same length.", nameof(channels));
        }

        Channels = channels;
        SampleRate = sampleRate;
    }

    /// <summary>The samples, one array per channel.</summary>
    public float[][] Channels { get; }

    public int ChannelCount => Channels.Length;

    public int SampleRate { get; }

    /// <summary>The length in frames (one sample per channel).</summary>
    public int Frames => Channels[0].Length;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / SampleRate);

    /// <summary>Roughly how much memory the samples take, for keeping the undo history in check.</summary>
    public long SizeInBytes => (long)Frames * ChannelCount * sizeof(float);

    /// <summary>Silent audio of the given length.</summary>
    public static AudioBuffer Silent(int channelCount, int frames, int sampleRate) =>
        new(Enumerable.Range(0, channelCount).Select(_ => new float[frames]).ToArray(), sampleRate);

    /// <summary>A copy of part of this audio.</summary>
    public AudioBuffer Slice(int start, int length)
    {
        CheckRange(start, length);
        var channels = new float[ChannelCount][];
        for (var c = 0; c < ChannelCount; c++)
        {
            channels[c] = new float[length];
            Array.Copy(Channels[c], start, channels[c], 0, length);
        }

        return new AudioBuffer(channels, SampleRate);
    }

    /// <summary>
    /// Replaces <paramref name="length"/> frames starting at <paramref name="start"/> with
    /// <paramref name="replacement"/>, which may be longer or shorter. Returns the new audio
    /// (this one is reused when the length doesn't change).
    /// </summary>
    public AudioBuffer Splice(int start, int length, AudioBuffer replacement)
    {
        CheckRange(start, length);
        if (replacement.ChannelCount != ChannelCount)
        {
            throw new ArgumentException("The replacement has a different number of channels.", nameof(replacement));
        }

        if (replacement.Frames == length)
        {
            for (var c = 0; c < ChannelCount; c++)
            {
                Array.Copy(replacement.Channels[c], 0, Channels[c], start, length);
            }

            return this;
        }

        var newFrames = (long)Frames - length + replacement.Frames;
        if (newFrames > Array.MaxLength)
        {
            throw new InvalidOperationException("The audio would be too long to hold in memory.");
        }

        var channels = new float[ChannelCount][];
        for (var c = 0; c < ChannelCount; c++)
        {
            var source = Channels[c];
            var result = new float[newFrames];
            Array.Copy(source, 0, result, 0, start);
            Array.Copy(replacement.Channels[c], 0, result, start, replacement.Frames);
            Array.Copy(source, start + length, result, start + replacement.Frames, Frames - start - length);
            channels[c] = result;
        }

        return new AudioBuffer(channels, SampleRate);
    }

    /// <summary>The highest absolute sample value in a range, across all channels.</summary>
    public float Peak(int start, int length)
    {
        CheckRange(start, length);
        var peak = 0f;
        foreach (var channel in Channels)
        {
            var span = channel.AsSpan(start, length);
            for (var i = 0; i < span.Length; i++)
            {
                var value = Math.Abs(span[i]);
                if (value > peak)
                {
                    peak = value;
                }
            }
        }

        return peak;
    }

    private void CheckRange(int start, int length)
    {
        if (start < 0 || length < 0 || (long)start + length > Frames)
        {
            throw new ArgumentOutOfRangeException(nameof(start), $"Range {start}+{length} is outside 0..{Frames}.");
        }
    }
}
