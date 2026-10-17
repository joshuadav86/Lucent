using System.Windows;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Playlist;

namespace WinampInspiredVisualizer;

/// <summary>
/// The main window's ways into the audio editor and converter: the title bar's Edit button and the
/// "Open in editor" and "Convert to…" menu items. Kept in its own file so the window's layout code stays apart.
/// </summary>
public partial class MainWindow
{
    private EditorHost? _editorHost;

    /// <summary>Lets the editor pause the player, and have it let go of a file while it's being saved over.</summary>
    private EditorHost EditorHost => _editorHost ??= new EditorHost(_player, () => _current?.Track.Path, UpdatePlayPauseButton);

    private void OnAudioTools(object sender, RoutedEventArgs e) =>
        AudioTools.ShowMenu(AudioToolsButton, this, EditorHost, _settings, _current?.Track.Path);

    private void OnLibraryOpenInEditor(object sender, RoutedEventArgs e)
    {
        // One at a time: each open song takes a fair amount of memory.
        if (SelectedLibraryItems().FirstOrDefault() is { } item)
        {
            AudioTools.OpenEditor(this, item.Path, EditorHost);
        }
    }

    private void OnLibraryConvert(object sender, RoutedEventArgs e) =>
        AudioTools.Convert(this, SelectedLibraryItems().Select(item => item.Path).ToList(), _settings);

    private void OnQueueOpenInEditor(object sender, RoutedEventArgs e)
    {
        if (SelectedQueuePaths().FirstOrDefault() is { } path)
        {
            AudioTools.OpenEditor(this, path, EditorHost);
        }
    }

    private void OnQueueConvert(object sender, RoutedEventArgs e) =>
        AudioTools.Convert(this, SelectedQueuePaths(), _settings);

    /// <summary>The selected queue entries' files, in queue order.</summary>
    private List<string> SelectedQueuePaths()
    {
        var selected = new HashSet<PlaylistEntry>(PlaylistView.SelectedItems.Cast<PlaylistEntry>());
        return _playlist.Where(selected.Contains).Select(entry => entry.Track.Path).ToList();
    }
}
