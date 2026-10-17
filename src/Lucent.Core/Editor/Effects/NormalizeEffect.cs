using System.Globalization;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Editor.Effects;

/// <summary>What normalizing aims for.</summary>
public enum NormalizeTarget
{
    /// <summary>The loudest sample reaches the target level, in dB below full scale.</summary>
    Peak,

    /// <summary>The overall loudness reaches the target level, in LUFS (BS.1770 integrated loudness).</summary>
    Loudness,
}

/// <summary>Changes the volume so the audio reaches a target peak level or loudness.</summary>
public sealed class NormalizeEffect : IAudioEffect
{
    /// <summary>Loudness normalizing never pushes peaks above this, so the result doesn't clip.</summary>
    public const double LoudnessPeakCeilingDb = -1.0;

    public NormalizeEffect(NormalizeTarget target, double level)
    {
        Target = target;
        Level = level;
    }

    public NormalizeTarget Target { get; }

    /// <summary>The peak level in dB (like -1) or the loudness in LUFS (like -14).</summary>
    public double Level { get; }

    public string Description => Target == NormalizeTarget.Peak
        ? $"Normalize to {Format(Level)} dB"
        : $"Normalize to {Format(Level)} LUFS";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var peak = input.Peak(0, input.Frames);
        if (peak <= 0f)
        {
            context.Note = "That part is silent, so there was nothing to normalize.";
            return input;
        }

        double gainDb;
        if (Target == NormalizeTarget.Peak)
        {
            gainDb = Level - ToDb(peak);
        }
        else
        {
            var loudness = LoudnessMeter.IntegratedLoudness(input, fraction => context.Report(fraction * 0.5));
            if (loudness is null)
            {
                context.Note = "That part is too short or too quiet to measure its loudness. Select at least half a second of music.";
                return input;
            }

            gainDb = Level - loudness.Value;
            var headroomDb = LoudnessPeakCeilingDb - ToDb(peak);
            if (gainDb > headroomDb)
            {
                // Reaching the target would clip; get as close as the peaks allow and say so.
                gainDb = headroomDb;
                context.Note = $"Reached {Format(loudness.Value + gainDb)} LUFS: any louder and the peaks would clip.";
            }
        }

        GainEffect.Scale(input, (float)Math.Pow(10, gainDb / 20), context);
        return input;
    }

    private static double ToDb(double amplitude) => 20 * Math.Log10(amplitude);

    private static string Format(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
