using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Export;

/// <summary>How much detail the encoder keeps, and so how big the file is.</summary>
public enum VideoQuality
{
    Good,
    High,
    Best,
}

/// <summary>What to record: which song (and which part of it), drawn how, into which file.</summary>
public sealed record VideoRecordingOptions
{
    public required string TrackPath { get; init; }

    /// <summary>The MP4 file to make. It's replaced if it exists (the dialog asks first).</summary>
    public required string OutputPath { get; init; }

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>30 or 60.</summary>
    public int FrameRate { get; init; } = 60;

    public VideoQuality Quality { get; init; } = VideoQuality.High;

    /// <summary>Where in the song the video starts.</summary>
    public TimeSpan From { get; init; }

    /// <summary>Where it ends, or null for the end of the song.</summary>
    public TimeSpan? To { get; init; }

    /// <summary>Every visual, as the visuals window lists them, so <see cref="PresetIndex"/> means the same.</summary>
    public required IReadOnlyList<PresetSource> Presets { get; init; }

    /// <summary>The visual to record (with <see cref="Auto"/>, the one to start on).</summary>
    public int PresetIndex { get; init; }

    /// <summary>Change visual every so often, on the beat, as Auto does on screen.</summary>
    public bool Auto { get; init; }

    /// <summary>The visuals Auto picks from (by index), or null for all of them.</summary>
    public IReadOnlyCollection<int>? AutoChoices { get; init; }

    public float AutoCycleSeconds { get; init; } = 25f;

    public float TransitionSeconds { get; init; } = 3f;

    public bool MorphTransitionsOnly { get; init; }

    /// <summary>Detail level for visuals, as on screen: 0 low, 1 normal, 2 high, 3 ultra.</summary>
    public float Detail { get; init; } = 1f;

    /// <summary>Glow, as on screen.</summary>
    public float BloomStrength { get; init; } = 1f;

    /// <summary>
    /// The number of pixels the visuals window draws (0 for its own size). Above the video's size,
    /// each frame is drawn bigger and shrunk, as on screen; it's never drawn smaller than the video.
    /// </summary>
    public int TargetPixels { get; init; }

    /// <summary>The song's bars, if they've been worked out already; with Auto they're worked out otherwise.</summary>
    public BeatMap? Beats { get; init; }

    /// <summary>The video's bitrate, in bits a second: more for bigger, faster pictures and for higher quality.</summary>
    public int VideoBitrate
    {
        get
        {
            var bitsPerPixel = Quality switch
            {
                VideoQuality.Good => 0.12,
                VideoQuality.High => 0.2,
                _ => 0.3,
            };
            var bitrate = (double)Width * Height * FrameRate * bitsPerPixel;
            return (int)Math.Clamp(bitrate, 4_000_000, 160_000_000);
        }
    }

    /// <summary>The soundtrack's bitrate (AAC), in bits a second.</summary>
    public int AudioBitrate => 192_000;
}

/// <summary>How a recording is getting on.</summary>
/// <param name="Stage">What's happening, in words.</param>
/// <param name="Fraction">How far through, 0 to 1 (for the stage that's happening).</param>
/// <param name="Speed">How many seconds of video are made each second (above 1 is faster than real time); 0 until known.</param>
public sealed record VideoRecordingProgress(string Stage, double Fraction, double Speed);

/// <summary>A finished recording.</summary>
/// <param name="Note">How it was encoded, for the user.</param>
public sealed record VideoRecordingResult(string Path, TimeSpan Length, TimeSpan TimeTaken, string Note);
