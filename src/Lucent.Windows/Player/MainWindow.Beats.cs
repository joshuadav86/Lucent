using System.IO;
using Lucent.Audio;

namespace Lucent;

/// <summary>
/// Finds each song's bars in the background, so Auto mode can change visual right on a downbeat.
/// There's nothing to switch on: it happens by itself whenever a song has a beat to follow.
/// </summary>
public partial class MainWindow
{
    // Songs already listened to, so playing one again doesn't mean working out its bars again.
    private readonly Dictionary<string, BeatMap?> _beatMaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _beatMapOrder = new();
    private CancellationTokenSource? _beatSearch;

    /// <summary>Tells the visuals where the song is: where the speakers are, a little behind the decoder.</summary>
    private void UpdateSongClock()
    {
        var heard = _player.Position.TotalSeconds - AudioPlayer.OutputLatencyMs / 1000.0;
        _visualizer.SongClock.Update(_player.CurrentPath, Math.Max(0, heard), _player.IsPlaying);
    }

    /// <summary>Works out the bars of a song that's just started, unless that's been done already.</summary>
    private async void FindBeats(string path)
    {
        _beatSearch?.Cancel();
        var key = Path.GetFullPath(path);
        if (_beatMaps.TryGetValue(key, out var known))
        {
            _visualizer.SongClock.SetBeats(path, known);
            return;
        }

        var search = _beatSearch = new CancellationTokenSource();
        BeatMap? beats;
        try
        {
            beats = await Task.Run(() => BeatMap.Analyze(path, search.Token), search.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (_beatSearch == search)
            {
                _beatSearch = null;
            }

            search.Dispose();
        }

        if (!_beatMaps.ContainsKey(key))
        {
            _beatMapOrder.Enqueue(key);
        }

        _beatMaps[key] = beats;
        while (_beatMapOrder.Count > 50 && _beatMapOrder.TryDequeue(out var oldest))
        {
            _beatMaps.Remove(oldest);
        }

        _visualizer.SongClock.SetBeats(path, beats);
    }
}
