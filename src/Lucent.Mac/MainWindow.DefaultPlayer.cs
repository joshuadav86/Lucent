using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Interop;

namespace Lucent.Mac;

// Lucent as the player that opens music from the Finder: songs macOS hands it (see App), and the
// Default player choice in Options > Updates.
public partial class MainWindow
{
    private bool _readyToOpenFiles;
    private IReadOnlyList<string>? _filesToOpen;

    /// <summary>After the player opens: plays what the Finder asked Lucent to open as it started.</summary>
    private void StartOpeningFiles()
    {
        _readyToOpenFiles = true;
        if (_filesToOpen is { } paths)
        {
            _filesToOpen = null;
            OpenFiles(paths);
        }
    }

    /// <summary>Plays music opened from the Finder, in place of the queue, with the player in front.</summary>
    public void OpenFiles(IReadOnlyList<string> paths)
    {
        if (!_readyToOpenFiles)
        {
            _filesToOpen = paths;
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        PlayDroppedPaths(paths);
    }

    private void OnMakeDefaultPlayer(object? sender, RoutedEventArgs e)
    {
        DefaultPlayerStatusText.Text = LaunchServices.MakeDefaultForMusic()
            ? "Done: music you double-click in the Finder now opens in Lucent."
            : "Couldn't make Lucent the default. In the Finder, choose a song, press ⌘I, pick Lucent under " +
              "Open with, and click Change All.";
        DefaultPlayerStatusText.IsVisible = true;
    }
}
