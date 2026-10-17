using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// One of the windows around the player (visuals, queue, library). It belongs to the player
/// window: it stays out of the taskbar, minimizes with it, and closing it just hides it.
/// Keys and dropped music go to the player, so they work whichever window has focus.
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

    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        if (!e.Handled && Player is not null && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            Player.AddDroppedPaths(paths);
            e.Handled = true;
        }
    }

    /// <summary>For the close button in the window's title bar.</summary>
    protected void OnHideWindow(object sender, RoutedEventArgs e) => Hide();
}
