using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Interop;
using Lucent.Library;

namespace Lucent.Mac;

// Lucent as the player that opens music and videos from the Finder: songs macOS hands it (see App), and the
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

    /// <summary>Says whether double-clicked music and videos open in Lucent now, as Options shows the Updates tab.</summary>
    private void ShowDefaultPlayerState()
    {
        try
        {
            DefaultPlayerStatusText.Text = DefaultPlayerStatus.Describe(LaunchServices.TypesElsewhere());
            DefaultPlayerStatusText.IsVisible = true;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Checking the default player", ex);
            DefaultPlayerStatusText.IsVisible = false;
        }
    }

    private void OnMakeDefaultPlayer(object? sender, RoutedEventArgs e)
    {
        // Says what macOS opens music and videos with afterwards, not just that it was asked, so a type that
        // didn't take is named.
        DefaultPlayerStatusText.Text = LaunchServices.MakeDefault()
            ? DefaultPlayerStatus.Describe(LaunchServices.TypesElsewhere())
            : "Couldn't make Lucent the default. In the Finder, choose a song or video, press ⌘I, pick Lucent under " +
              "Open with, and click Change All.";
        DefaultPlayerStatusText.IsVisible = true;
    }
}
