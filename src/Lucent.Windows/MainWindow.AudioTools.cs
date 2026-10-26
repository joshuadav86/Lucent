using System.Windows;
using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer;

/// <summary>The audio editor and converter: the title bar's waveform button and the song menus. Kept in its own file.</summary>
public partial class MainWindow
{
    private void OnAudioTools(object sender, RoutedEventArgs e) =>
        AudioTools.ShowMenu(AudioToolsButton, this, _player, _settings);

    // The editor opens again with the player if it was left open, docked where it was.
    private void ReopenEditor() => AudioTools.ReopenEditor(this, _player);

    /// <summary>Opens a song in the audio editor, for the queue and library menus.</summary>
    public void OpenInEditor(Window owner, string path) => AudioTools.OpenEditor(owner, _player, path);

    /// <summary>Lets a window snap to Lucent's other windows and move with the player when docked to it.</summary>
    public void AddToSnapping(Window window) => _snapper.Add(window);

    /// <summary>Opens the converter for some songs, for the queue and library menus.</summary>
    public void ConvertFiles(Window owner, IReadOnlyList<string> paths) => AudioTools.Convert(owner, _settings, paths);

    /// <summary>Asks for songs and opens the converter for them (the editor's Batch button).</summary>
    public void ChooseFilesToConvert(Window owner) => AudioTools.ChooseFilesToConvert(owner, _settings);
}
