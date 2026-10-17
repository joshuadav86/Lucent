using System.Globalization;
using System.Text.Json.Serialization;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Editor.Effects;

namespace WinampInspiredVisualizer.Export;

/// <summary>The kinds of processing the converter can do to every song on the way through.</summary>
public enum ProcessingKind
{
    NormalizePeak,
    NormalizeLoudness,
    Limit,
    Volume,
    Equalizer,
    Compress,
    RemoveDcOffset,
    RemoveHum,
    RemoveClicks,
    TrimSilence,
    FadeIn,
    FadeOut,
    MixToMono,
}

/// <summary>
/// One step of the converter's processing chain, with its one setting (a level, a length, a frequency).
/// Saved with the converter's options, so the chain is there again next time.
/// </summary>
public sealed class ProcessingStep
{
    public ProcessingKind Kind { get; set; }

    /// <summary>dB for normalizing, limiting and volume, LUFS for loudness, seconds for fades, Hz for hum (0 to detect it).</summary>
    public double Value { get; set; }

    /// <summary>The equalizer's bands, for an equalizer step.</summary>
    public List<EqBand>? Bands { get; set; }

    /// <summary>The compressor's settings, for a compress step.</summary>
    public CompressorSettings? Compressor { get; set; }

    [JsonIgnore]
    public string Description => Kind switch
    {
        ProcessingKind.NormalizePeak => $"Normalize peaks to {Number(Value)} dB",
        ProcessingKind.NormalizeLoudness => $"Normalize loudness to {Number(Value)} LUFS",
        ProcessingKind.Limit => $"Limit to {Number(Value)} dBTP",
        ProcessingKind.Volume => $"Volume {Value.ToString("+0.#;-0.#;0", CultureInfo.CurrentCulture)} dB",
        ProcessingKind.Equalizer => "Equalizer (the editor's settings)",
        ProcessingKind.Compress => Compressor is { } c ? $"Compress {Number(c.Ratio)}:1 at {Number(c.ThresholdDb)} dB" : "Compress",
        ProcessingKind.RemoveDcOffset => "Remove DC offset",
        ProcessingKind.RemoveHum => Value > 0 ? $"Remove {Value:0} Hz hum" : "Remove hum (find 50 or 60 Hz)",
        ProcessingKind.RemoveClicks => "Remove clicks",
        ProcessingKind.TrimSilence => "Trim silence from the start and end",
        ProcessingKind.FadeIn => $"Fade in over {Number(Value)} s",
        ProcessingKind.FadeOut => $"Fade out over {Number(Value)} s",
        _ => "Mix to mono",
    };

    /// <summary>
    /// The effect, and the range of the audio it covers (empty for the whole song): fades only cover their
    /// first or last few seconds.
    /// </summary>
    public (IAudioEffect Effect, Editor.FrameRange Range) ToEffect(int frames, int sampleRate)
    {
        Editor.FrameRange All() => new(0, 0);
        int Seconds() => (int)Math.Min(frames, Math.Max(0, Value) * sampleRate);
        return Kind switch
        {
            ProcessingKind.NormalizePeak => (new NormalizeEffect(NormalizeTarget.Peak, Value), All()),
            ProcessingKind.NormalizeLoudness => (new NormalizeEffect(NormalizeTarget.Loudness, Value), All()),
            ProcessingKind.Limit => (new LimiterEffect(new LimiterSettings { CeilingDb = Value }), All()),
            ProcessingKind.Volume => (new GainEffect(Value), All()),
            ProcessingKind.Equalizer => (new EqualizerEffect(Bands ?? EqBand.Defaults()), All()),
            ProcessingKind.Compress => (new CompressorEffect(Compressor ?? new CompressorSettings()), All()),
            ProcessingKind.RemoveDcOffset => (new DcOffsetEffect(), All()),
            ProcessingKind.RemoveHum => (new HumRemovalEffect(Value > 0 ? Value : null), All()),
            ProcessingKind.RemoveClicks => (new ClickRemovalEffect(5), All()),
            ProcessingKind.TrimSilence => (new TrimSilenceEffect(), All()),
            ProcessingKind.FadeIn => (new FadeEffect(FadeDirection.In), new Editor.FrameRange(0, Seconds())),
            ProcessingKind.FadeOut => (new FadeEffect(FadeDirection.Out), new Editor.FrameRange(frames - Seconds(), Seconds())),
            _ => (new MonoMixEffect(), All()),
        };
    }

    /// <summary>A new step of a kind, with its usual setting.</summary>
    public static ProcessingStep Create(ProcessingKind kind) => new()
    {
        Kind = kind,
        Value = kind switch
        {
            ProcessingKind.NormalizePeak => -1,
            ProcessingKind.NormalizeLoudness => -14,
            ProcessingKind.Limit => -1,
            ProcessingKind.Volume => -3,
            ProcessingKind.FadeIn => 2,
            ProcessingKind.FadeOut => 3,
            _ => 0,
        },
    };

    public ProcessingStep Clone() => new()
    {
        Kind = Kind,
        Value = Value,
        Bands = Bands is null ? null : EqBand.CloneAll(Bands),
        Compressor = Compressor?.Clone(),
    };

    private static string Number(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
