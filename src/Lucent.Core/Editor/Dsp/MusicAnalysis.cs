namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>
/// What the editor knows about a sound as music: its beats and tempo, its key, and its spectral frames (kept so
/// finding loops again at another length is instant). Either the beats or the key can be missing: a spoken word
/// recording has neither, a drum loop has no key, and a held chord has no beat.
/// </summary>
public sealed record MusicAnalysis(BeatGrid? Grid, MusicalKey? Key, SpectralFrames Frames)
{
    /// <summary>Neither a beat nor a key: what's known about a sound that couldn't be measured.</summary>
    public static MusicAnalysis Nothing { get; } = new(null, null, SpectralFrames.Measure(AudioBuffer.Silent(1, 0, 44100)));

    /// <summary>Measures the whole sound. About a second for a song, so run it in the background.</summary>
    public static MusicAnalysis Analyze(AudioBuffer audio, CancellationToken cancel = default)
    {
        var frames = SpectralFrames.Measure(audio, cancel);
        var key = KeyDetector.Analyze(frames, 0, int.MaxValue);
        cancel.ThrowIfCancellationRequested();
        var grid = BeatGrid.Analyze(audio, cancel);
        return new MusicAnalysis(grid, key, frames);
    }

    /// <summary>"124 BPM · A minor (8A)", leaving out what wasn't found, or null if neither was.</summary>
    public string? Summary
    {
        get
        {
            var parts = new List<string>();
            if (Grid is { } grid)
            {
                parts.Add(grid.TempoText);
            }

            if (Key is { } key)
            {
                parts.Add(key.IsSure ? key.ToString() : $"probably {key}");
            }

            return parts.Count == 0 ? null : string.Join("  ·  ", parts);
        }
    }
}
