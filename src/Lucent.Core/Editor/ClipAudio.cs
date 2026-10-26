using Lucent.Editor.Dsp;

namespace Lucent.Editor;

/// <summary>
/// Copied audio, with a little of what was either side of it, so pasting it can crossfade into its new
/// neighbours the way a cut does: each side of a join plays on past the join and fades out under the other.
/// </summary>
/// <param name="Before">What came just before the copy, <see cref="ContextFrames"/> long (silence at the start of a song).</param>
/// <param name="After">What came just after it, the same length.</param>
public sealed record ClipAudio(AudioBuffer Audio, AudioBuffer Before, AudioBuffer After)
{
    /// <summary>How much of the surroundings a copy keeps: enough for the longest crossfade the editor offers.</summary>
    public static int ContextFrames(int sampleRate) => sampleRate / 20;

    public int Frames => Audio.Frames;

    /// <summary>Copies a range of some audio, with its surroundings.</summary>
    public static ClipAudio From(AudioBuffer audio, FrameRange range)
    {
        var context = ContextFrames(audio.SampleRate);
        return new ClipAudio(audio.Slice(range.Start, range.Length),
            Padded(audio, range.Start - context, context),
            Padded(audio, range.End, context));
    }

    /// <summary>Audio pasted from elsewhere (another program), with nothing known about its surroundings.</summary>
    public static ClipAudio Bare(AudioBuffer audio)
    {
        var context = ContextFrames(audio.SampleRate);
        var silence = AudioBuffer.Silent(audio.ChannelCount, context, audio.SampleRate);
        return new ClipAudio(audio, silence, silence);
    }

    /// <summary>
    /// The same audio with the given channels and sample rate: mono is copied to both sides, stereo is
    /// mixed to mono, and a different rate is resampled, so audio copied from one song can go into another.
    /// </summary>
    public ClipAudio ConvertTo(int channels, int sampleRate, CancellationToken cancellation = default)
    {
        if (Audio.ChannelCount == channels && Audio.SampleRate == sampleRate)
        {
            return this;
        }

        AudioBuffer Convert(AudioBuffer part, int frames)
        {
            var mixed = Remix(part, channels);
            if (mixed.SampleRate == sampleRate)
            {
                return mixed;
            }

            // The context must stay exactly ContextFrames long, whatever the rounding.
            return Resampler.Resample(mixed, mixed.SampleRate / (double)sampleRate, sampleRate,
                frames < 0 ? null : frames, cancellation: cancellation);
        }

        var context = ContextFrames(sampleRate);
        return new ClipAudio(Convert(Audio, -1), Convert(Before, context), Convert(After, context));
    }

    private static AudioBuffer Remix(AudioBuffer audio, int channels)
    {
        if (audio.ChannelCount == channels)
        {
            return audio;
        }

        var frames = audio.Frames;
        var mono = new float[frames];
        foreach (var channel in audio.Channels)
        {
            for (var i = 0; i < frames; i++)
            {
                mono[i] += channel[i] / audio.ChannelCount;
            }
        }

        var result = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            result[c] = c == 0 ? mono : (float[])mono.Clone();
        }

        return new AudioBuffer(result, audio.SampleRate);
    }

    /// <summary><paramref name="length"/> frames from <paramref name="start"/>, with silence where that's outside the audio.</summary>
    private static AudioBuffer Padded(AudioBuffer audio, int start, int length)
    {
        var channels = new float[audio.ChannelCount][];
        var from = Math.Max(0, start);
        var to = Math.Min(audio.Frames, start + length);
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            channels[c] = new float[length];
            if (to > from)
            {
                Array.Copy(audio.Channels[c], from, channels[c], from - start, to - from);
            }
        }

        return new AudioBuffer(channels, audio.SampleRate);
    }
}

/// <summary>A note left at a place in the audio, to find it again. Markers move with the audio around them.</summary>
public sealed record Marker(int Frame, string Name);

/// <summary>Something the editor can't do, with a message that can be shown as it is.</summary>
public sealed class EditException : Exception
{
    public EditException(string message)
        : base(message)
    {
    }
}
