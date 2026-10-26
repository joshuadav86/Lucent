using System.Windows;
using System.Windows.Threading;
using WinampInspiredVisualizer.Layout;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

// Where the windows around the player go back to. Every window is opened again where it was left: as
// Lucent starts, they all go back exactly as they were. Later, a window that was docked to the player
// docks to it again wherever the player is by then, and one that was left on its own goes back to its
// spot. A locked window always goes back to its spot, and nothing else moves it (WindowLocks). With the
// windows linked, every window moves with the player, docked or not.
public partial class MainWindow
{
    private void SetUpWindowPlaces()
    {
        WindowLocks.Settings = _settings;
        _snapper.MoveEveryWindow = () => _settings.LinkWindows;
        LinkToggle.IsChecked = _settings.LinkWindows;
        LinkMenuItem.IsChecked = _settings.LinkWindows;
    }

    private void OnToggleLink(object sender, RoutedEventArgs e) => ToggleLink();

    /// <summary>Links the windows to the player, so they all move with it, docked or not, or unlinks them (J).</summary>
    private void ToggleLink()
    {
        _settings.LinkWindows = !_settings.LinkWindows;
        _settings.Save();
        LinkToggle.IsChecked = _settings.LinkWindows;
        LinkMenuItem.IsChecked = _settings.LinkWindows;
    }

    /// <summary>
    /// Puts a window back where it was left, if it was left on its own rather than docked to the player,
    /// or it's locked there, or Lucent is just starting (when everything goes back as it was). Returns
    /// false when it should dock to the player instead: it was docked, or it's never been placed.
    /// </summary>
    private bool OpenWhereLeft(Window window)
    {
        if (WindowLocks.NameOf(window) is not { } name || Saved(name) is not { } saved
            || saved.Docked && !_openingWindows && !WindowLocks.IsLocked(window))
        {
            return false;
        }

        if (window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
        {
            Place(window, saved.Left, saved.Top, saved.Width, saved.Height);
        }
        else if (!WindowLocks.Holds(window))
        {
            // A window that sizes itself (the equalizer) only goes back to where it was.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = saved.Left;
            window.Top = saved.Top;
        }

        return true;
    }

    /// <summary>The equalizer, the analyzer, the artwork and the video, whichever are open: their places are remembered too.</summary>
    private static IEnumerable<Window> SmallWindows =>
        new Window?[] { EqualizerWindow.Current, AnalyzerWindow.Current, ArtworkWindow.Current, VideoWindow.Current }.OfType<Window>();

    /// <summary>
    /// The player and the shown windows docked to it (touching it, or touching a window that's docked to
    /// it), as they are on the screen now.
    /// </summary>
    private List<Rect> DockedGroup()
    {
        static Rect Bounds(Window window) => new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        var player = Bounds(this);
        var group = new List<Rect> { player };
        var rest = Application.Current.Windows.Cast<Window>()
            .Where(other => other != this && other.Owner == this && other.IsVisible && other.WindowState == WindowState.Normal
                && !(other == _visuals && _visuals.IsFullscreen))
            .Select(Bounds)
            .ToList();
        for (var i = 0; i < group.Count; i++)
        {
            foreach (var rect in rest.ToList())
            {
                if (Touches(group[i], rect))
                {
                    group.Add(rect);
                    rest.Remove(rect);
                }
            }
        }

        return group;
    }

    /// <summary>Opens the analyzer and the artwork again if they were open when Lucent closed.</summary>
    private void ReopenSmallWindows()
    {
        if (_settings.ShowAnalyzer && !AnalyzerWindow.IsOpen)
        {
            ToggleAnalyzer();
        }

        if (_settings.ShowArtwork && !ArtworkWindow.IsOpen)
        {
            ToggleArtwork();
        }
    }

    /// <summary>A small window that's just opened (the equalizer, the analyzer, the artwork): where it's left is remembered as it closes.</summary>
    private void RememberWhenClosed(Window window) => window.Closing += (_, _) =>
    {
        if (!IsShuttingDown)
        {
            SavePlacements();
        }
    };

    /// <summary>
    /// Reset to default layout: the windows as Lucent opens them the very first time. The player at its usual
    /// size, the equalizer and the visuals, laid out together in the middle of the player's screen, with the
    /// queue, the library, the analyzer and the artwork closed, every window unlocked, and Lucent out of full
    /// screen. The video, the radio and any editors stay open (closing them would stop what's in them) and
    /// dock back on beside the rest.
    /// </summary>
    public void ResetLayout()
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        if (_visuals.IsFullscreen)
        {
            _visuals.ToggleFullscreen();
        }

        foreach (var window in Application.Current.Windows.Cast<Window>().ToList())
        {
            WindowLocks.SetLocked(window, false);
        }

        _settings.LockedWindows.Clear();
        _settings.FullScreenStyle = TileStyle.AsTheyAre;
        if (!IsClassic)
        {
            _settings.PlayerScale = 1;
            ApplyPlayerSize();
        }

        _queue.Hide();
        _libraryWindow.Hide();
        if (AnalyzerWindow.IsOpen)
        {
            ToggleAnalyzer();
        }

        if (ArtworkWindow.IsOpen)
        {
            ToggleArtwork();
        }

        if (OpenEqualizerWindow is null)
        {
            OpenEqualizer();
        }

        _visuals.Show();

        // Once the player's at its usual size and the equalizer's open, as on the first run.
        Dispatcher.InvokeAsync(() =>
        {
            ArrangeFirstTime();
            SavePlacements();
            Dispatcher.InvokeAsync(RedockSmallWindows, DispatcherPriority.Background);
        }, DispatcherPriority.Background);
        Toast.Show(this, "Default layout");
    }
}
