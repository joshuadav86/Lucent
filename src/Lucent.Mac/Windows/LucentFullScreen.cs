using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Lucent.Visualizer;

namespace Lucent.Mac.Windows;

/// <summary>
/// "Full screen Lucent" in the right-click menus of Lucent's windows, which switches Lucent full screen
/// (every open window tiled across the screen, see MainWindow.FullScreen.cs) from whichever window you're
/// in, and on a right-click on any of their title bars. "Close" (<see cref="CloseItem"/>) goes in after it.
/// It's apart from the visuals' own fullscreen. Set up once by the player as Lucent starts, like
/// <see cref="WindowLocks"/>.
/// </summary>
public static class LucentFullScreen
{
    /// <summary>The key that switches it, as menus show it.</summary>
    public static readonly KeyGesture Gesture = new(Key.F11, KeyModifiers.Shift);

    /// <summary>The key for Tidy layout, as menus show it.</summary>
    public static readonly KeyGesture TidyGesture = new(Key.D, KeyModifiers.Shift);

    private static readonly object Marker = new();

    private static readonly object LayoutsMarker = new();

    /// <summary>Menus that tick the item as they open (a menu built again each time it opens gets the item again, but this only once).</summary>
    private static readonly ConditionalWeakTable<ContextMenu, object> Ticking = new();

    /// <summary>Whether Lucent's full screen now.</summary>
    public static Func<bool>? IsOn { get; set; }

    /// <summary>Switches it on or off.</summary>
    public static Action? Toggle { get; set; }

    /// <summary>Tidy layout: lays the windows out again, balanced, the next way each time.</summary>
    public static Action? Tidy { get; set; }

    /// <summary>Reset to default layout: the windows as Lucent first opens them.</summary>
    public static Action? Reset { get; set; }

    /// <summary>Whether a window is one that Lucent full screen lays out, whose title bar gets the menu.</summary>
    public static Func<Window, bool>? Covers { get; set; }

    /// <summary>Adds "Full screen Lucent", ticked while it's on, "Tidy layout", "Layouts" (<see cref="SavedLayoutsMenu"/>), "Reset to default layout", and "Close" for the window to the end of a right-click menu. A menu kept and opened again gets them once.</summary>
    public static void AddTo(ContextMenu menu, Window window)
    {
        if (menu.Items.OfType<MenuItem>().Any(item => item.Tag == Marker))
        {
            return;
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var item = new MenuItem
        {
            Header = "Full screen Lucent", InputGesture = Gesture, ToggleType = MenuItemToggleType.CheckBox, Tag = Marker,
            IsChecked = IsOn?.Invoke() == true,
        };
        ToolTip.SetTip(item, MenuTips.FullScreenLucent);
        item.Click += (_, _) => Toggle?.Invoke();
        menu.Items.Add(item);
        menu.Items.Add(TidyItem());
        var layouts = SavedLayoutsMenu.Item(window);
        layouts.Tag = LayoutsMarker;
        menu.Items.Add(layouts);
        menu.Items.Add(ResetItem());
        menu.Items.Add(CloseItem.For(window));
        if (!Ticking.TryGetValue(menu, out _))
        {
            Ticking.Add(menu, Marker);
            menu.Opening += (_, _) =>
            {
                foreach (var shown in menu.Items.OfType<MenuItem>().Where(entry => entry.Tag == Marker))
                {
                    shown.IsChecked = IsOn?.Invoke() == true;
                }

                // Layouts saved, renamed or deleted since it last opened.
                foreach (var shown in menu.Items.OfType<MenuItem>().Where(entry => entry.Tag == LayoutsMarker))
                {
                    SavedLayoutsMenu.Fill(shown, window);
                }
            };
        }
    }

    /// <summary>Adds "Full screen Lucent", "Tidy layout", "Layouts", "Reset to default layout" and "Close" to the end of a menu that's built afresh each time it opens (the classic player's).</summary>
    public static void AddTo(MenuFlyout menu, Window window)
    {
        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var item = new MenuItem
        {
            Header = "Full screen Lucent", InputGesture = Gesture, ToggleType = MenuItemToggleType.CheckBox, IsChecked = IsOn?.Invoke() == true,
        };
        ToolTip.SetTip(item, MenuTips.FullScreenLucent);
        item.Click += (_, _) => Toggle?.Invoke();
        menu.Items.Add(item);
        menu.Items.Add(TidyItem());
        menu.Items.Add(SavedLayoutsMenu.Item(window));
        menu.Items.Add(ResetItem());
        menu.Items.Add(CloseItem.For(window));
    }

    /// <summary>"Tidy layout", which lays the windows out again in a balanced way.</summary>
    private static MenuItem TidyItem()
    {
        var tidy = new MenuItem { Header = "Tidy layout", InputGesture = TidyGesture };
        ToolTip.SetTip(tidy, MenuTips.Tidy);
        tidy.Click += (_, _) => Tidy?.Invoke();
        return tidy;
    }

    /// <summary>"Reset to default layout", which puts the windows back as Lucent first opens them.</summary>
    private static MenuItem ResetItem()
    {
        var reset = new MenuItem { Header = "Reset to default layout" };
        ToolTip.SetTip(reset, MenuTips.ResetLayout);
        reset.Click += (_, _) => Reset?.Invoke();
        return reset;
    }

    /// <summary>Opens a menu with "Full screen Lucent", "Tidy layout", "Layouts", "Reset to default layout" and "Close" at the pointer, for a right-click on a window's title bar. A window it doesn't lay out (a dialog, say) gets just "Close".</summary>
    public static bool ShowWindowMenu(Window window, Control at)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        if (Covers?.Invoke(window) == true)
        {
            AddTo(menu, window);
        }
        else
        {
            menu.Items.Add(CloseItem.For(window));
        }

        menu.Open(at);
        return true;
    }
}
