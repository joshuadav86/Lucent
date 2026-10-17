using System.Globalization;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Editor.Effects;

/// <summary>The editor's own parametric equalizer.</summary>
public sealed class EqualizerEffect : IAudioEffect
{
    public EqualizerEffect(IReadOnlyList<EqBand> bands) => Bands = EqBand.CloneAll(bands);

    public IReadOnlyList<EqBand> Bands { get; }

    public string Description => "Equalizer";

    public AudioBuffer Process(AudioBuffer input, EffectContext context) =>
        StreamRender.Run(input, new ParametricEq(Bands, input.ChannelCount, input.SampleRate), context.Report, context.Cancellation);
}

/// <summary>Evens out the level: loud parts are turned down, and the whole can then be turned up.</summary>
public sealed class CompressorEffect : IAudioEffect
{
    public CompressorEffect(CompressorSettings settings) => Settings = settings.Clone();

    public CompressorSettings Settings { get; }

    public string Description =>
        $"Compress {Settings.Ratio.ToString("0.#", CultureInfo.CurrentCulture)}:1 at {Settings.ThresholdDb.ToString("0", CultureInfo.CurrentCulture)} dB";

    public AudioBuffer Process(AudioBuffer input, EffectContext context) =>
        StreamRender.Run(input, new Compressor(Settings, input.SampleRate), context.Report, context.Cancellation);
}

/// <summary>Stops the peaks going over a ceiling, so the music can be made louder without clipping.</summary>
public sealed class LimiterEffect : IAudioEffect
{
    public LimiterEffect(LimiterSettings settings) => Settings = settings.Clone();

    public LimiterSettings Settings { get; }

    public string Description
    {
        get
        {
            var ceiling = $"{Settings.CeilingDb.ToString("0.0", CultureInfo.CurrentCulture)} {(Settings.TruePeak ? "dBTP" : "dB")}";
            return Settings.InputGainDb > 0.05
                ? $"Limit to {ceiling}, +{Settings.InputGainDb.ToString("0.0", CultureInfo.CurrentCulture)} dB louder"
                : $"Limit to {ceiling}";
        }
    }

    public AudioBuffer Process(AudioBuffer input, EffectContext context) =>
        StreamRender.Run(input, new Limiter(Settings, input.ChannelCount, input.SampleRate), context.Report, context.Cancellation);
}

/// <summary>Takes out mains hum (50 or 60 Hz and its harmonics) with very narrow, phase-free notch filters.</summary>
public sealed class HumRemovalEffect : IAudioEffect
{
    public const int Harmonics = 8;

    /// <param name="frequency">50 or 60; null to work out which from the audio.</param>
    public HumRemovalEffect(double? frequency) => Frequency = frequency;

    public double? Frequency { get; }

    public string Description => Frequency is { } hz ? $"Remove {hz:0} Hz hum" : "Remove hum";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var hz = Frequency ?? AudioAnalysis.DetectHum(input, new FrameRange(0, input.Frames));
        if (hz is not { } baseHz)
        {
            context.Note = "No mains hum stood out, so nothing was changed. If you can hear hum, choose 50 Hz or 60 Hz yourself.";
            return input;
        }

        var harmonics = Enumerable.Range(1, Harmonics).Select(k => baseHz * k).Where(f => f < input.SampleRate * 0.45).ToList();
        var settle = input.SampleRate;
        for (var c = 0; c < input.ChannelCount; c++)
        {
            foreach (var f in harmonics)
            {
                // About 2 Hz wide whatever the harmonic, so the music around it is left alone.
                Biquad.ZeroPhase(input.Channels[c], () => new Biquad(FilterShape.Notch, f, f / 2.0, 0, input.SampleRate), settle);
            }

            context.Report((c + 1.0) / input.ChannelCount);
        }

        if (Frequency is null)
        {
            context.Note = $"Found {baseHz:0} Hz hum and took it out, with {harmonics.Count - 1} of its harmonics.";
        }

        return input;
    }
}

/// <summary>Repairs clicks, pops and crackle.</summary>
public sealed class ClickRemovalEffect : IAudioEffect
{
    /// <param name="sensitivity">1 (only obvious clicks) to 10 (anything that stands out).</param>
    public ClickRemovalEffect(double sensitivity) => Sensitivity = sensitivity;

    public double Sensitivity { get; }

    public string Description => "Remove clicks";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var fixedCount = 0;
        for (var c = 0; c < input.ChannelCount; c++)
        {
            var channel = c;
            fixedCount += ClickRepair.Repair(input.Channels[c], input.SampleRate, Sensitivity,
                f => context.Report((channel + f) / input.ChannelCount), context.Cancellation);
        }

        context.Note = fixedCount == 0
            ? "No clicks were found. A higher sensitivity finds quieter ones."
            : $"Repaired {fixedCount} click{(fixedCount == 1 ? "" : "s")}. Ctrl+Z undoes it if it touched the music.";
        return input;
    }
}

/// <summary>Removes a DC offset: a constant shift that moves the waveform off centre and wastes headroom.</summary>
public sealed class DcOffsetEffect : IAudioEffect
{
    public string Description => "Remove DC offset";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var largest = 0.0;
        foreach (var samples in input.Channels)
        {
            double sum = 0;
            foreach (var x in samples)
            {
                sum += x;
            }

            var mean = (float)(sum / Math.Max(1, samples.Length));
            largest = Math.Max(largest, Math.Abs(mean));
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] -= mean;
            }
        }

        context.Note = $"The offset was {largest * 100:0.###}% of full scale; it's now centred.";
        return input;
    }
}

/// <summary>Turns down background noise (hiss, hum, room noise) learned from a stretch of just noise.</summary>
public sealed class NoiseReductionEffect : IAudioEffect
{
    public NoiseReductionEffect(NoiseProfile profile, double reductionDb, double sensitivityDb)
    {
        Profile = profile;
        ReductionDb = reductionDb;
        SensitivityDb = sensitivityDb;
    }

    public NoiseProfile Profile { get; }

    public double ReductionDb { get; }

    public double SensitivityDb { get; }

    public string Description => $"Reduce noise by {ReductionDb:0} dB";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        if (Profile.SampleRate != input.SampleRate || Profile.Magnitudes.Length == 0)
        {
            throw new EditException("The noise was learned from audio with a different sample rate. Learn it again from this sound.");
        }

        return NoiseReducer.Process(input, Profile, ReductionDb, SensitivityDb, context.Report, context.Cancellation);
    }
}

/// <summary>Swaps the left and right channels.</summary>
public sealed class SwapChannelsEffect : IAudioEffect
{
    public string Description => "Swap left and right";

    public AudioBuffer Process(AudioBuffer input, EffectContext context) =>
        input.ChannelCount < 2 ? input : new AudioBuffer(new[] { input.Channels[1], input.Channels[0] }, input.SampleRate);
}

/// <summary>Puts the same mix of both channels in each, so it sounds mono but stays a stereo file.</summary>
public sealed class MonoMixEffect : IAudioEffect
{
    public string Description => "Mix to mono";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        if (input.ChannelCount < 2)
        {
            return input;
        }

        var left = input.Channels[0];
        var right = input.Channels[1];
        for (var i = 0; i < left.Length; i++)
        {
            var mid = (left[i] + right[i]) * 0.5f;
            left[i] = mid;
            right[i] = mid;
        }

        return input;
    }
}

/// <summary>Flips the waveform upside down (polarity), in one channel or both.</summary>
public sealed class InvertEffect : IAudioEffect
{
    public string Description => "Invert polarity";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        foreach (var samples in input.Channels)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] = -samples[i];
            }
        }

        return input;
    }
}

/// <summary>Makes the stereo image narrower (towards mono) or wider, by scaling the difference between the channels.</summary>
public sealed class StereoWidthEffect : IAudioEffect
{
    /// <param name="width">1 leaves it as it is, 0 is mono, 2 is twice as wide.</param>
    public StereoWidthEffect(double width) => Width = Math.Clamp(width, 0, 2);

    public double Width { get; }

    public string Description => $"Stereo width {Width * 100:0}%";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        if (input.ChannelCount < 2)
        {
            return input;
        }

        var left = input.Channels[0];
        var right = input.Channels[1];
        var width = (float)Width;
        for (var i = 0; i < left.Length; i++)
        {
            var mid = (left[i] + right[i]) * 0.5f;
            var side = (left[i] - right[i]) * 0.5f * width;
            left[i] = mid + side;
            right[i] = mid - side;
        }

        if (Width > 1 && input.Peak(0, input.Frames) > 1f)
        {
            context.Note = "Widening pushed some peaks above 0 dB. Normalize or the limiter will bring them back.";
        }

        return input;
    }
}

/// <summary>Changes the tempo without changing the pitch (or both together, like a tape).</summary>
public sealed class TempoEffect : IAudioEffect
{
    public const double MinSpeed = 0.5;
    public const double MaxSpeed = 2;

    /// <param name="speed">1.25 plays 25% faster (and shorter).</param>
    public TempoEffect(double speed, bool keepPitch)
    {
        Speed = Math.Clamp(speed, MinSpeed, MaxSpeed);
        KeepPitch = keepPitch;
    }

    public double Speed { get; }

    public bool KeepPitch { get; }

    public string Description => KeepPitch ? $"Tempo {Speed * 100:0}%" : $"Speed {Speed * 100:0}%";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        if (Math.Abs(Speed - 1) < 0.0005)
        {
            return input;
        }

        return KeepPitch
            ? PitchShifter.TimeStretch(input, 1 / Speed, context.Report, context.Cancellation)
            : Resampler.Resample(input, Speed, input.SampleRate, progress: context.Report, cancellation: context.Cancellation);
    }
}

/// <summary>Cuts the silence (or near silence) off the start and end.</summary>
public sealed class TrimSilenceEffect : IAudioEffect
{
    public TrimSilenceEffect(double thresholdDb = -60) => ThresholdDb = thresholdDb;

    public double ThresholdDb { get; }

    public string Description => "Trim silence";

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var level = (float)Math.Pow(10, ThresholdDb / 20);
        bool Loud(int i) => input.Channels.Any(channel => Math.Abs(channel[i]) > level);
        var start = 0;
        while (start < input.Frames && !Loud(start))
        {
            start++;
        }

        var end = input.Frames;
        while (end > start && !Loud(end - 1))
        {
            end--;
        }

        // A few milliseconds of lead-in either side, so the first note's attack isn't clipped.
        var margin = input.SampleRate / 100;
        start = Math.Max(0, start - margin);
        end = Math.Min(input.Frames, end + margin);
        if (end <= start)
        {
            context.Note = "It's all silence, so nothing was trimmed.";
            return input;
        }

        return start == 0 && end == input.Frames ? input : input.Slice(start, end - start);
    }
}
