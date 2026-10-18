using System.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

// Editing songs' info, and the artwork window. The queue and library windows call EditInfo.
public partial class MainWindow
{
    private void OnToggleArtwork(object sender, RoutedEventArgs e) => ToggleArtwork();

    private void ToggleArtwork()
    {
        ArtToggle.IsChecked = ArtworkWindow.Toggle(this, _player, _settings, _current?.Track);
        if (_remote is not null)
        {
            ArtworkWindow.ShowRemote(_remote.State?.Track, _remote.Artwork);
        }
    }

    private void OnArtworkOpenChanged(object? sender, EventArgs e) => ArtToggle.IsChecked = ArtworkWindow.IsOpen;

    private void OnNowPlayingEditInfo(object sender, RoutedEventArgs e)
    {
        if (_current is not null)
        {
            EditInfo(new[] { _current.Track.Path });
        }
    }

    /// <summary>Ctrl+I: edits the songs chosen in the list being used, or else the one playing.</summary>
    private void EditInfoForSelection()
    {
        if (_libraryWindow.LibraryView.IsKeyboardFocusWithin && _libraryWindow.LibraryView.SelectedItems.Count > 0)
        {
            EditInfo(_libraryWindow.SelectedLibraryItems().Select(item => item.Path));
        }
        else if (_queue.PlaylistView.IsKeyboardFocusWithin && _queue.PlaylistView.SelectedItems.Count > 0)
        {
            EditInfo(_queue.SelectedEntries().Select(entry => entry.Track.Path));
        }
        else if (_current is not null)
        {
            EditInfo(new[] { _current.Track.Path });
        }
        else
        {
            EditInfo(_queue.SelectedEntries().Select(entry => entry.Track.Path));
        }
    }

    /// <summary>Opens the info editor for these songs.</summary>
    public void EditInfo(IEnumerable<string> paths)
    {
        var list = paths.ToList();
        if (list.Count > 0)
        {
            TrackInfoWindow.Open(this, _player, _settings, list);
        }
    }

    /// <summary>Shows edited info everywhere: queue rows, the player's title, the artwork window and the library.</summary>
    private async void OnTagsSaved(object? sender, TagsSavedEventArgs e)
    {
        var saved = new HashSet<string>(e.Paths, StringComparer.OrdinalIgnoreCase);
        var entries = _playlist.Where(entry => saved.Contains(entry.Track.Path)).ToList();
        var tracks = await Task.Run(() => entries.Select(entry => Track.FromFile(entry.Track.Path)).ToList());
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Refresh(tracks[i]);
        }

        if (_current is not null && saved.Contains(_current.Track.Path))
        {
            ShowNowPlaying(_current.Track);
        }

        await _library.RereadTagsAsync(saved);
    }
}
