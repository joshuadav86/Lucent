using System.Globalization;
using Lucent.Editor.Dsp;

namespace Lucent.Editor.Effects;

/// <summary>Changes the pitch by up to an octave either way.</summary>
public sealed class PitchEffect : IAudioEffect
{
    public const double MaxSemitones = 12;

    /// <param name="semitones">How far to move the pitch: +12 is an octave up, -1 a semitone down.</param>
    /// <param name="keepSpeed">
    /// True to keep the tempo and length the same (time-stretching); false to change speed with the
    /// pitch, like a record played faster or slower, which sounds most natural but changes the length.
    /// </param>
    public PitchEffect(double semitones, bool keepSpeed)
    {
        Semitones = Math.Clamp(semitones, -MaxSemitones, MaxSemitones);
        KeepSpeed = keepSpeed;
    }

    public double Semitones { get; }

    public bool KeepSpeed { get; }

    public string Description
    {
        get
        {
            var amount = Semitones.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture);
            return KeepSpeed ? $"Pitch {amount} semitones" : $"Pitch and speed {amount} semitones";
        }
    }

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        if (Math.Abs(Semitones) < 0.001)
        {
            return input;
        }

        if (KeepSpeed)
        {
            return PitchShifter.Shift(input, Semitones, context.Report, context.Cancellation);
        }

        // Tape-style: reading the samples faster raises the pitch and shortens the audio together.
        var step = Math.Pow(2, Semitones / 12);
        return Resampler.Resample(input, step, input.SampleRate, progress: context.Report, cancellation: context.Cancellation);
    }
}
