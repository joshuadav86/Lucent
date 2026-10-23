using Avalonia.Controls;

namespace Lucent.Mac.Windows;

/// <summary>
/// "Close" at the end of Lucent's right-click menus, doing what the window's own X does: the windows around
/// the player hide, the rest close, and the player's closes Lucent. It goes in with "Full screen Lucent"
/// (<see cref="LucentFullScreen.AddTo(ContextMenu, Window)"/>), so every menu that has one has the other.
/// </summary>
public static class CloseItem
{
    /// <summary>The item for a window's menu.</summary>
    public static MenuItem For(Window window)
    {
        var item = new MenuItem { Header = window is MainWindow ? "Close Lucent" : "Close" };
        item.Click += (_, _) => Close(window);
        return item;
    }

    /// <summary>Closes a window as its X does.</summary>
    public static void Close(Window window)
    {
        if (window is ToolWindow)
        {
            window.Hide();
        }
        else
        {
            window.Close();
        }
    }
}
