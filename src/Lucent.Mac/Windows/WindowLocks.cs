using Avalonia.Controls;
using Avalonia.Threading;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac.Windows;

/// <summary>
/// Which of the windows around the player are locked in place. A locked window can't be moved or
/// resized, doesn't move with the player when the player's dragged, and stays where it is when the
/// windows are docked or laid out again (D, G, K). Each window's lock button (<see cref="LockButton"/>)
/// switches it, and it's remembered by the window's name, like where the window was left.
/// </summary>
/// <remarks>
/// macOS resizes windows from their edges itself, so a locked window is made one that can't be resized,
/// and goes back to whatever it would be (full screen and the classic player change it) once unlocked.
/// </remarks>
public static class WindowLocks
{
    // What each locked window's CanResize would be if it weren't locked.
    private static readonly Dictionary<Window, bool> CanResizeUnlocked = new();
    private static readonly HashSet<Window> Followed = new();
    private static bool _holding;

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
    public static bool Holds(Window window) => Followed.Contains(window) && IsLocked(window);

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
        Hold(window);
        Changed?.Invoke(null, window);
    }

    /// <summary>Keeps a window's edges still while it's locked, from now on. Its lock button calls this as it appears.</summary>
    public static void Follow(Window window)
    {
        if (Followed.Add(window))
        {
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property != Window.CanResizeProperty || _holding || !IsLocked(window))
                {
                    return;
                }

                // Something else changed it while it's locked: that's what it goes back to, and it stays held.
                CanResizeUnlocked[window] = e.NewValue is true;
                Dispatcher.UIThread.Post(() => Hold(window));
            };
            window.Closed += (_, _) =>
            {
                Followed.Remove(window);
                CanResizeUnlocked.Remove(window);
            };
        }

        Hold(window);
    }

    // Locked, it can't be resized; unlocked, it's back to how it was.
    private static void Hold(Window window)
    {
        _holding = true;
        try
        {
            if (IsLocked(window))
            {
                if (!CanResizeUnlocked.ContainsKey(window))
                {
                    CanResizeUnlocked[window] = window.CanResize;
                }

                window.CanResize = false;
            }
            else if (CanResizeUnlocked.Remove(window, out var canResize))
            {
                window.CanResize = canResize;
            }
        }
        finally
        {
            _holding = false;
        }
    }
}
