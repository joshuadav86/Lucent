using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace Lucent.Windows;

/// <summary>
/// One of the windows around the player (visuals, queue, library). It belongs to the player
/// window: it stays out of the taskbar, minimizes with it, and closing it just hides it.
/// Keys go to the player, so they work whichever window has focus. Music dropped on it plays straight
/// away, like a drop on the player, unless the window does something else with it (the queue adds it).
/// </summary>
public class ToolWindow : Window
{
    public ToolWindow()
    {
        ShowInTaskbar = false;
        AllowDrop = true;
        ShowActivated = false;
    }

    /// <summary>The player window this window belongs to.</summary>
    public MainWindow? Player { get; set; }

    /// <summary>The name its position is saved under, e.g. "Queue".</summary>
    public string PlacementName { get; set; } = string.Empty;

    /// <summary>
    /// Shows the window without taking focus from the player. WPF refuses to show a maximized
    /// window that doesn't activate, so one that was hidden while maximized (or full screen) is
    /// activated as it comes back instead of crashing Lucent.
    /// </summary>
    public new void Show()
    {
        ShowActivated = WindowState == WindowState.Maximized;
        base.Show();
    }

    /// <summary>
    /// Windows also shows this window by itself, without going through <see cref="Show"/>: when the
    /// player comes back from being minimized, every window it owns reappears. WPF throws if one of
    /// them is maximized but doesn't activate, so ShowActivated follows WindowState all the time.
    /// </summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == WindowStateProperty)
        {
            ShowActivated = WindowState == WindowState.Maximized;
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Closing only hides it; it goes away for good when the player closes.
        if (Player is { IsShuttingDown: false })
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (!e.Handled && Player is not null)
        {
            Player.HandleShortcut(e);
        }
    }

    protected override void OnPreviewDragEnter(DragEventArgs e)
    {
        base.OnPreviewDragEnter(e);
        DroppedMusic.Accept(e);
    }

    protected override void OnPreviewDragOver(DragEventArgs e)
    {
        base.OnPreviewDragOver(e);
        DroppedMusic.Accept(e);
    }

    protected override void OnPreviewDrop(DragEventArgs e)
    {
        base.OnPreviewDrop(e);
        if (Player is not null && DroppedMusic.Paths(e) is { Count: > 0 } paths)
        {
            OnMusicDropped(paths);
        }
    }

    /// <summary>Files or folders dropped on this window. They play now, replacing the queue.</summary>
    protected virtual void OnMusicDropped(IReadOnlyList<string> paths) => Player?.PlayDroppedPaths(paths);

    /// <summary>For the close button in the window's title bar.</summary>
    protected void OnHideWindow(object sender, RoutedEventArgs e) => Hide();
}
