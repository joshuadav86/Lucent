using System.Globalization;

namespace Lucent.Editor.Effects;

/// <summary>Makes the audio louder or quieter by a fixed number of decibels.</summary>
public sealed class GainEffect : IAudioEffect
{
    public GainEffect(double decibels) => Decibels = decibels;

    public double Decibels { get; }

    public string Description => $"Volume {Decibels.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture)} dB";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var factor = (float)Math.Pow(10, Decibels / 20);
        Scale(input, factor, context);
        if (input.Peak(0, input.Frames) > 1f)
        {
            context.Note = "Some peaks are now above 0 dB and will be clipped when saved. Undo and pick a smaller boost to avoid that.";
        }

        return input;
    }

    /// <summary>Multiplies every sample by <paramref name="factor"/>.</summary>
    internal static void Scale(AudioBuffer audio, float factor, EffectContext context)
    {
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            var samples = audio.Channels[c];
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= factor;
            }

            context.Report((c + 1.0) / audio.ChannelCount);
        }
    }
}

/// <summary>Which way a fade goes.</summary>
public enum FadeDirection
{
    In,
    Out,
}

/// <summary>Fades from silence up to full volume, or from full volume down to silence.</summary>
public sealed class FadeEffect : IAudioEffect
{
    public FadeEffect(FadeDirection direction) => Direction = direction;

    public FadeDirection Direction { get; }

    public string Description => Direction == FadeDirection.In ? "Fade in" : "Fade out";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var frames = input.Frames;
        if (frames < 2)
        {
            return input;
        }

        // An S-shaped curve: it starts and ends gently, so the fade has no audible corner.
        var gains = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / (frames - 1);
            var rising = 0.5 - 0.5 * Math.Cos(Math.PI * t);
            gains[i] = (float)(Direction == FadeDirection.In ? rising : 1 - rising);
        }

        for (var c = 0; c < input.ChannelCount; c++)
        {
            var samples = input.Channels[c];
            for (var i = 0; i < frames; i++)
            {
                samples[i] *= gains[i];
            }

            context.Report((c + 1.0) / input.ChannelCount);
        }

        return input;
    }
}

/// <summary>Replaces the audio with silence of the same length.</summary>
public sealed class SilenceEffect : IAudioEffect
{
    public string Description => "Silence";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        foreach (var channel in input.Channels)
        {
            Array.Clear(channel);
        }

        return input;
    }
}

/// <summary>Plays the audio backwards.</summary>
public sealed class ReverseEffect : IAudioEffect
{
    public string Description => "Reverse";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        foreach (var channel in input.Channels)
        {
            Array.Reverse(channel);
        }

        return input;
    }
}
