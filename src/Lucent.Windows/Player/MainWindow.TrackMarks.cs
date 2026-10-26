using Lucent.Recording;

namespace Lucent;

/// <summary>
/// Songs inside a long recording, such as a DJ set made with the Record window: when the file has its songs
/// marked (cue points in a WAV, or a .cue sheet beside it), Next and Previous skip between them before
/// moving on to the next file in the queue. Kept in its own file.
/// </summary>
public partial class MainWindow
{
    private string? _marksPath;
    private IReadOnlyList<TrackMark> _marks = Array.Empty<TrackMark>();

    /// <summary>Moves to the next or previous marked song in the file playing. False when it has none there.</summary>
    private bool SkipWithinTrack(bool forward)
    {
        if (!_player.HasTrack || _player.CurrentPath is not { } path || _player.Duration < TrackMarkers.MinimumLength)
        {
            return false;
        }

        if (!string.Equals(path, _marksPath, StringComparison.Ordinal))
        {
            _marksPath = path;
            _marks = TrackMarkers.Read(path);
        }

        if (_marks.Count < 2)
        {
            return false;
        }

        var to = forward ? TrackMarkers.Next(_marks, _player.Position) : TrackMarkers.Previous(_marks, _player.Position);
        if (to is not { } start)
        {
            return false;
        }

        _player.Position = start;
        return true;
    }
}
