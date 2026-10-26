using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lucent.Mac.Windows;
using Lucent.Layout;

namespace Lucent.Mac;

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

    private void OnToggleLink(object? sender, RoutedEventArgs e) => ToggleLink();

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

        if (WindowLocks.Holds(window))
        {
            // Locked, it's still where it was left.
            return true;
        }

        if (window.CanResize)
        {
            Place(window, saved.Left, saved.Top, saved.Width, saved.Height);
        }
        else
        {
            // A window that sizes itself (the equalizer) only goes back to where it was.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.MoveTo(saved.Left, saved.Top);
        }

        if (!window.IsVisible)
        {
            // macOS can move a window as it first appears; put it back where it belongs.
            void OnOpened(object? sender, EventArgs e)
            {
                window.Opened -= OnOpened;
                window.MoveTo(saved.Left, saved.Top);
            }

            window.Opened += OnOpened;
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
        var group = new List<Rect> { Placement.BoundsOf(this) };
        var rest = OwnedWindows
            .Where(other => other.IsVisible && other.WindowState == WindowState.Normal && !(other == _visuals && _visuals.IsFullscreen))
            .Select(Placement.BoundsOf)
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
    private void RememberWhenClosed(Window window) => window.Closing += (_, e) =>
    {
        // Only when it's closed on its own: closing with the player, the player remembers it.
        if (e.CloseReason == WindowCloseReason.WindowClosing && !IsShuttingDown)
        {
            SavePlacements();
        }
    };

    /// <summary>
    /// Moves the player, keeping the locked windows where they are. (macOS moves the windows that belong
    /// to the player along with it.)
    /// </summary>
    private void MovePlayer(double left, double top)
    {
        var locked = OwnedWindows.Where(WindowLocks.Holds).Select(window => (Window: window, window.Position)).ToList();
        this.MoveTo(left, top);
        foreach (var (window, position) in locked)
        {
            window.Position = position;
        }
    }

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

        foreach (var window in _snapper.Windows.ToList())
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

        ShowTool(_visuals);

        // Once the player's at its usual size and the equalizer's open, as on the first run.
        Dispatcher.UIThread.Post(() =>
        {
            ArrangeFirstTime();
            SavePlacements();
            Dispatcher.UIThread.Post(RedockSmallWindows, DispatcherPriority.Background);
            // Said once the windows have settled: shown while the player was still changing size, it could open
            // in the corner of the screen and stay there.
            Toast.Show(this, "Default layout");
        }, DispatcherPriority.Background);
    }
}
