using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// "Full screen Lucent" in the right-click menus of Lucent's windows, which switches Lucent full screen
/// (every open window tiled across the screen, see MainWindow.FullScreen.cs) from whichever window you're
/// in, and on a right-click on any of their title bars. It's apart from the visuals' own fullscreen. Set
/// up once by the player as Lucent starts, like <see cref="WindowLocks"/>.
/// </summary>
public static class LucentFullScreen
{
    /// <summary>The key that switches it, as menus show it.</summary>
    public const string Gesture = "Shift+F11";

    private static readonly object Marker = new();

    /// <summary>Menus that tick the item as they open (a menu built again each time it opens gets the item again, but this only once).</summary>
    private static readonly ConditionalWeakTable<ContextMenu, object> Ticking = new();

    /// <summary>Whether Lucent's full screen now.</summary>
    public static Func<bool>? IsOn { get; set; }

    /// <summary>Switches it on or off.</summary>
    public static Action? Toggle { get; set; }

    /// <summary>Whether a window is one that Lucent full screen lays out, whose title bar gets the menu.</summary>
    public static Func<Window, bool>? Covers { get; set; }

    /// <summary>Adds "Full screen Lucent" to the end of a right-click menu, ticked while it's on. A menu kept and opened again gets it once.</summary>
    public static void AddTo(ContextMenu menu)
    {
        if (menu.Items.OfType<MenuItem>().Any(item => item.Tag == Marker))
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var item = new MenuItem { Header = "Full screen Lucent", InputGestureText = Gesture, IsCheckable = true, Tag = Marker };
        item.IsChecked = IsOn?.Invoke() == true;
        item.Click += (_, _) => Toggle?.Invoke();
        menu.Items.Add(item);
        if (!Ticking.TryGetValue(menu, out _))
        {
            Ticking.Add(menu, Marker);
            menu.Opened += (_, _) =>
            {
                foreach (var shown in menu.Items.OfType<MenuItem>().Where(entry => entry.Tag == Marker))
                {
                    shown.IsChecked = IsOn?.Invoke() == true;
                }
            };
        }
    }

    /// <summary>Opens a menu with "Full screen Lucent" at the mouse, for a right-click on a window's title bar. False for a window it doesn't lay out (a dialog, say).</summary>
    public static bool ShowWindowMenu(Window window)
    {
        if (Covers?.Invoke(window) != true || window.Content is not UIElement content)
        {
            return false;
        }

        var menu = new ContextMenu { PlacementTarget = content, Placement = PlacementMode.MousePoint };
        AddTo(menu);
        menu.IsOpen = true;
        return true;
    }
}
