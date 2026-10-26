using Avalonia.Controls;
using WinampInspiredVisualizer.Export;

namespace Lucent.Mac;

/// <summary>Recording the song that's playing as a video, from the visuals' right-click menu. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>True when there's a song loaded, and visuals to draw it with.</summary>
    public bool CanRecordVideo => _player.HasTrack && _player.CurrentPath is not null && _visualizer.Presets.Count > 0;

    /// <summary>
    /// Opens the Record video window for the song that's loaded, starting from the visual on screen
    /// and the graphics settings in Options.
    /// </summary>
    public void RecordVideo(Window owner)
    {
        if (!CanRecordVideo)
        {
            return;
        }

        var path = _player.CurrentPath!;
        var presets = _visualizer.Presets;
        var autoChoices = _visualizer.AutoCycleNames is { } names
            ? Enumerable.Range(0, presets.Count).Where(index => names.Contains(presets[index].Name, StringComparer.OrdinalIgnoreCase)).ToList()
            : null;
        // The bars, when they've been worked out for Auto already (otherwise the recording works them out).
        _beatMaps.TryGetValue(Path.GetFullPath(path), out var beats);

        var template = new VideoRecordingOptions
        {
            TrackPath = path,
            OutputPath = string.Empty,
            Presets = presets,
            PresetIndex = _visualizer.PresetIndex,
            Auto = _visualizer.AutoCycle,
            AutoChoices = autoChoices is { Count: > 0 } ? autoChoices : null,
            AutoCycleSeconds = _visualizer.AutoCycleSeconds,
            TransitionSeconds = _visualizer.TransitionSeconds,
            MorphTransitionsOnly = _visualizer.MorphTransitionsOnly,
            Detail = _visualizer.Quality,
            BloomStrength = _visualizer.BloomStrength,
            TargetPixels = _visualizer.TargetPixels,
            Beats = beats,
        };
        var title = string.IsNullOrWhiteSpace(NowPlayingText.Text) ? Path.GetFileNameWithoutExtension(path) : NowPlayingText.Text;
        VideoExportWindow.Open(owner, template, title, _player.Duration, _player.Position);
    }
}
