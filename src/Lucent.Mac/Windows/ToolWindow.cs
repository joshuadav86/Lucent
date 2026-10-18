using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Lucent.Mac.Windows;

/// <summary>
/// One of the windows around the player (visuals, queue, library...). It belongs to the player
/// window: it stays out of the Dock's window list and minimizes with it, and closing it just hides it.
/// Keys and dropped music go to the player, so they work whichever window has focus.
/// </summary>
public class ToolWindow : Window
{
    public ToolWindow()
    {
        ShowInTaskbar = false;
        ShowActivated = false;
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOverFiles);
        AddHandler(DragDrop.DropEvent, OnDropFiles);
        AddHandler(KeyDownEvent, OnShortcutKey, RoutingStrategies.Tunnel);
    }

    /// <summary>The player window this window belongs to.</summary>
    public MainWindow? Player { get; set; }

    /// <summary>The name its position is saved under, e.g. "Queue".</summary>
    public string PlacementName { get; set; } = string.Empty;

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Closing only hides it; it goes away for good when the player closes. Avalonia asks a
        // window's owned windows before the window itself, so when the player (or the app) is
        // closing this runs before the player knows, and the reason is what tells them apart:
        // hiding then would stop the player closing, and leave the window off for next time.
        if (e.CloseReason == WindowCloseReason.WindowClosing && Player is { IsShuttingDown: false })
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OnShortcutKey(object? sender, KeyEventArgs e)
    {
        if (!e.Handled && Player is not null)
        {
            Player.HandleShortcut(e);
        }
    }

    private static void OnDragOverFiles(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDropFiles(object? sender, DragEventArgs e)
    {
        if (!e.Handled && Player is not null && e.DataTransfer.TryGetFiles() is { } files)
        {
            Player.AddDroppedPaths(files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray());
            e.Handled = true;
        }
    }

    /// <summary>For the close button in the window's title bar.</summary>
    protected void OnHideWindow(object? sender, RoutedEventArgs e) => Hide();
}
