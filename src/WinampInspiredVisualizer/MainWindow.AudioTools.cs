using System.Windows;
using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer;

/// <summary>The title bar's Edit button, which opens the audio editor and converter. Kept in its own file.</summary>
public partial class MainWindow
{
    private void OnAudioTools(object sender, RoutedEventArgs e) =>
        AudioTools.ShowMenu(AudioToolsButton, this, _player, _settings);
}
