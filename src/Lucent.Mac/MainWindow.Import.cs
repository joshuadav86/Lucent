using WinampInspiredVisualizer.Import;
using WinampInspiredVisualizer.Library;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace Lucent.Mac;

// Bringing a library over from another player. The import window does the work; these connect it to the library.
public partial class MainWindow
{
    /// <summary>Opens the window for importing from Apple Music, MusicBee, foobar2000, Plex and YouTube Music.</summary>
    public void ShowImport() => ImportWindow.Open(this, _settings);

    public int LibraryFileCount => _library.Items.Count;

    /// <summary>The library's songs, and their tags: the library's own once read, otherwise read from the file.</summary>
    public (IReadOnlyList<string> Files, Func<string, Track?> Tags) LibrarySnapshot()
    {
        var byPath = new Dictionary<string, LibraryItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in _library.Items)
        {
            byPath[item.Path] = item;
        }

        return (byPath.Keys.ToList(), path => byPath.TryGetValue(path, out var item) && item.Track is { } track ? track : Track.FromFile(path));
    }

    public void AddFolderToLibrary(string folder) => LinkLibraryFolder(folder);

    /// <summary>Makes the imported loved songs favorites and shows the new playlists. Returns how many favorites are new.</summary>
    public int ImportFinished(LibraryImportResult result)
    {
        var added = result.Loved.Count(path => !IsFavoriteTrack(path));
        if (added > 0)
        {
            SetFavoriteTracks(result.Loved, favorite: true);
        }

        if (result.Playlists.Count > 0)
        {
            _libraryWindow.PlaylistsChanged();
        }

        return added;
    }

    /// <summary>A song that played to its end counts as a play.</summary>
    private static void CountPlay(string path) => Task.Run(() => TrackStats.Shared.CountPlay(path));
}
