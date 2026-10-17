using System.IO;
using System.Text.Json.Serialization;

namespace WinampInspiredVisualizer.Export;

/// <summary>The kinds of file Lucent can write.</summary>
public enum ExportFormat
{
    Mp3,
    Flac,
    Wav,

    /// <summary>Only used to save an edited AIFF file back as AIFF.</summary>
    Aiff,
}

/// <summary>MP3 quality: a constant bitrate, or LAME's variable-bitrate quality levels.</summary>
public enum Mp3Quality
{
    Cbr128,
    Cbr192,
    Cbr256,
    Cbr320,

    /// <summary>VBR V2: about 190 kbps on average, transparent for most listeners.</summary>
    VbrV2,

    /// <summary>VBR V0: about 245 kbps on average, LAME's highest variable quality.</summary>
    VbrV0,
}

/// <summary>How hard the FLAC encoder works to make files smaller. Every level is lossless.</summary>
public enum FlacCompression
{
    Fast,
    Normal,
    Best,
}

/// <summary>How samples are stored in a WAV or AIFF file.</summary>
public enum PcmDepth
{
    Int16,
    Int24,

    /// <summary>32-bit floating point (WAV only).</summary>
    Float32,
}

/// <summary>The sample rate of the new file.</summary>
public enum SampleRateChoice
{
    Keep,
    Rate44100,
    Rate48000,
}

/// <summary>Everything that decides how a file is written. Remembered between runs for the converter.</summary>
public sealed class ExportOptions
{
    public ExportFormat Format { get; set; } = ExportFormat.Mp3;

    public Mp3Quality Mp3Quality { get; set; } = Mp3Quality.Cbr320;

    /// <summary>16 or 24.</summary>
    public int FlacBitDepth { get; set; } = 16;

    public FlacCompression FlacCompression { get; set; } = FlacCompression.Normal;

    public PcmDepth WavDepth { get; set; } = PcmDepth.Int24;

    public SampleRateChoice SampleRate { get; set; } = SampleRateChoice.Keep;

    /// <summary>Where the converter puts new files, or null to put each one next to its original.</summary>
    public string? OutputFolder { get; set; }

    public ExportOptions Clone() => (ExportOptions)MemberwiseClone();

    /// <summary>The file extension for <see cref="Format"/>, with its dot.</summary>
    [JsonIgnore]
    public string Extension => ExtensionFor(Format);

    public static string ExtensionFor(ExportFormat format) => format switch
    {
        ExportFormat.Mp3 => ".mp3",
        ExportFormat.Flac => ".flac",
        ExportFormat.Wav => ".wav",
        _ => ".aiff",
    };

    /// <summary>The sample rate to write, given the audio's own rate.</summary>
    public int OutputSampleRate(int sourceRate)
    {
        var rate = SampleRate switch
        {
            SampleRateChoice.Rate44100 => 44100,
            SampleRateChoice.Rate48000 => 48000,
            _ => sourceRate,
        };

        // MP3 stops at 48 kHz, so higher rates come down to the nearest family member.
        if (Format == ExportFormat.Mp3 && rate > 48000)
        {
            rate = rate % 44100 == 0 ? 44100 : 48000;
        }

        return rate;
    }

    /// <summary>A short summary for the user, like "MP3 · 320 kbps" or "FLAC · 24-bit".</summary>
    [JsonIgnore]
    public string Summary => Format switch
    {
        ExportFormat.Mp3 => $"MP3 · {Mp3Label(Mp3Quality)}",
        ExportFormat.Flac => $"FLAC · {FlacBitDepth}-bit",
        ExportFormat.Wav => $"WAV · {DepthLabel(WavDepth)}",
        _ => $"AIFF · {DepthLabel(WavDepth)}",
    };

    public static string Mp3Label(Mp3Quality quality) => quality switch
    {
        Mp3Quality.Cbr128 => "128 kbps",
        Mp3Quality.Cbr192 => "192 kbps",
        Mp3Quality.Cbr256 => "256 kbps",
        Mp3Quality.Cbr320 => "320 kbps",
        Mp3Quality.VbrV2 => "VBR V2",
        _ => "VBR V0",
    };

    public static string DepthLabel(PcmDepth depth) => depth switch
    {
        PcmDepth.Int16 => "16-bit",
        PcmDepth.Int24 => "24-bit",
        _ => "32-bit float",
    };

    /// <summary>The formats Lucent can write back to, by file extension.</summary>
    public static ExportFormat? WritableFormat(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => ExportFormat.Mp3,
        ".flac" => ExportFormat.Flac,
        ".wav" => ExportFormat.Wav,
        ".aif" or ".aiff" => ExportFormat.Aiff,
        _ => null,
    };
}
