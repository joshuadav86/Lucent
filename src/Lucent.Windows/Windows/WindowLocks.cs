using System.Windows;
using Lucent.Settings;

namespace Lucent.Windows;

/// <summary>
/// Which of the windows around the player are locked in place. A locked window can't be moved or
/// resized, doesn't move with the player when the player's dragged, and stays where it is when the
/// windows are docked or laid out again (D, G, K). Each window's lock button (<see cref="LockButton"/>)
/// switches it, and it's remembered by the window's name, like where the window was left.
/// </summary>
public static class WindowLocks
{
    /// <summary>Where the locks are kept. Set once by the player as Lucent starts.</summary>
    public static AppSettings? Settings { get; set; }

    /// <summary>Raised when a window is locked or unlocked.</summary>
    public static event EventHandler<Window>? Changed;

    /// <summary>The name a window's place and lock are saved under, e.g. "Queue". Null for windows that don't have one.</summary>
    public static string? NameOf(Window? window) => window switch
    {
        ToolWindow tool when tool.PlacementName.Length > 0 => tool.PlacementName,
        MainWindow => "Player",
        EqualizerWindow => "Equalizer",
        AnalyzerWindow => "Analyzer",
        ArtworkWindow => "Artwork",
        VideoWindow => "Video",
        _ => null,
    };

    public static bool IsLocked(Window? window) =>
        NameOf(window) is { } name && Settings?.LockedWindows.Contains(name) == true;

    /// <summary>True when the window is locked where it is now: it's locked, and it's been shown (so it has a place to stay in).</summary>
    public static bool Holds(Window window) => window.IsLoaded && IsLocked(window);

    public static void SetLocked(Window window, bool locked)
    {
        if (Settings is not { } settings || NameOf(window) is not { } name || IsLocked(window) == locked)
        {
            return;
        }

        if (locked)
        {
            settings.LockedWindows.Add(name);
        }
        else
        {
            settings.LockedWindows.Remove(name);
        }

        settings.Save();
        Changed?.Invoke(null, window);
    }
}
