using Avalonia.Controls;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;

namespace Lucent.Mac;

// STUB: owned by player worker. The MainWindow members the library and queue windows call, with the
// Windows app's signatures, so this area builds before the real player port lands. The integrator deletes this file.
public sealed partial class MainWindow
{
    // STUB: owned by player worker
    public void FillPlaylistsMenu(ContextMenu menu, Window owner)
    {
    }

    // STUB: owned by player worker
    public MenuItem FavoriteMenuItem(IReadOnlyList<string> paths) => new() { Header = "Add to favorites", IsEnabled = paths.Count > 0 };

    // STUB: owned by player worker
    public MenuItem AddToPlaylistMenu(Window owner, Func<Task<List<Track>>> songs) => new() { Header = "Add to playlist" };

    // STUB: owned by player worker
    public void CreatePlaylist(Window owner)
    {
    }

    // STUB: owned by player worker
    public void RenamePlaylist(Window owner, string name)
    {
    }

    // STUB: owned by player worker
    public void DeletePlaylist(Window owner, string name)
    {
    }

    // STUB: owned by player worker
    public void RemoveFromPlaylist(string name, IReadOnlyCollection<string> paths)
    {
    }

    // STUB: owned by player worker
    public void AddFilesFromDialog(Window owner)
    {
    }

    // STUB: owned by player worker
    public void AddFolderFromDialog(Window owner)
    {
    }

    // STUB: owned by player worker
    public void ClearQueue()
    {
    }

    // STUB: owned by player worker
    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
    }

    // STUB: owned by player worker
    public void ReplaceQueue(IReadOnlyList<Track> tracks, int startIndex)
    {
    }

    // STUB: owned by player worker
    public void RemoveFromQueue(IEnumerable<PlaylistEntry> entries)
    {
    }

    // STUB: owned by player worker
    public void PlayEntry(PlaylistEntry entry)
    {
    }

    // STUB: owned by player worker
    public void PlayFromLibrary(LibraryItem item)
    {
    }

    // STUB: owned by player worker
    public void EditInfo(IEnumerable<string> paths)
    {
    }

    // STUB: owned by player worker
    public void OpenInEditor(Window owner, string path)
    {
    }

    // STUB: owned by player worker
    public void ConvertFiles(Window owner, IReadOnlyList<string> paths)
    {
    }

    // STUB: owned by player worker
    public void ShowInExplorer(string path)
    {
    }

    // STUB: owned by player worker
    public void ShowStores()
    {
    }
}
