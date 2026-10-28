using Avalonia.Threading;
using Lucent.Playlist;
using Track = Lucent.Playlist.Track;

namespace Lucent.Mac;

// The queue is kept between runs: it's saved a moment after it changes and again on closing, and put back
// on the next start with the song that was playing selected (Play starts it). Windows does the same.
public partial class MainWindow
{
    private DispatcherTimer? _queueSaveTimer;

    // Nothing is saved until the saved queue is back, so a start that's still reading it can't wipe it.
    private bool _queueRestored;

    private async void RestoreQueue()
    {
        // The screenshots' made-up queue stays as it is.
        if (_previewing)
        {
            return;
        }

        var (files, current) = await Task.Run(SavedQueue.Read);
        var tracks = await Task.Run(() => files.Select(Track.FromFile).ToList());
        _queueRestored = true;
        if (IsShuttingDown || tracks.Count == 0)
        {
            return;
        }

        // Anything opened meanwhile (a song double-clicked in Finder) stays after the saved queue.
        var entries = tracks.Select(track => new PlaylistEntry(track)).ToList();
        for (var i = 0; i < entries.Count; i++)
        {
            _playlist.Insert(i, entries[i]);
        }

        if (current >= 0 && _current is null)
        {
            _queue.PlaylistView.SelectedItem = entries[current];
            _queue.PlaylistView.ScrollIntoView(entries[current]);
        }
    }

    /// <summary>Saves the queue shortly, once a burst of changes has settled.</summary>
    private void SaveQueueSoon()
    {
        if (!_queueRestored || _previewing)
        {
            return;
        }

        if (_queueSaveTimer is null)
        {
            _queueSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _queueSaveTimer.Tick += (_, _) => SaveQueue();
        }

        _queueSaveTimer.Stop();
        _queueSaveTimer.Start();
    }

    private void SaveQueue()
    {
        _queueSaveTimer?.Stop();
        if (!_queueRestored || _previewing)
        {
            return;
        }

        var current = _current is null ? -1 : _playlist.IndexOf(_current);
        if (current < 0 && _queue.PlaylistView.SelectedItem is PlaylistEntry selected)
        {
            current = _playlist.IndexOf(selected);
        }

        SavedQueue.Write(_playlist.Select(entry => entry.Track).ToList(), current);
    }
}
