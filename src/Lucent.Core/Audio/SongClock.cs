using System.Diagnostics;

namespace Lucent.Audio;

/// <summary>
/// Where the song that's playing has got to, and its <see cref="BeatMap"/> once that's been
/// worked out. The player updates it a few times a second; the visuals read it every frame
/// from their own thread and fill in the time since.
/// </summary>
public sealed class SongClock
{
    private readonly object _gate = new();
    private string? _path;
    private BeatMap? _beats;
    private double _position;
    private long _stamp;
    private bool _playing;

    /// <summary>Records the song, how far into it the speakers are (in seconds), and whether it's playing.</summary>
    public void Update(string? path, double position, bool playing)
    {
        lock (_gate)
        {
            if (!string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
            {
                _path = path;
                _beats = null;
            }

            _position = position;
            _playing = playing && path is not null;
            _stamp = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Hands over a song's bars, if that song is still the one playing.</summary>
    public void SetBeats(string path, BeatMap? beats)
    {
        lock (_gate)
        {
            if (string.Equals(path, _path, StringComparison.OrdinalIgnoreCase))
            {
                _beats = beats;
            }
        }
    }

    /// <summary>The playing song's bars and where it is now, or null when nothing's playing or its bars aren't known.</summary>
    public (BeatMap Beats, double Position)? Read()
    {
        lock (_gate)
        {
            if (!_playing || _beats is null)
            {
                return null;
            }

            return (_beats, _position + Stopwatch.GetElapsedTime(_stamp).TotalSeconds);
        }
    }
}
