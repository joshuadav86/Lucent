using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Lucent.Mac.Windows;

/// <summary>
/// Single window (U, Window › Single Window, or "Single window" on a right-click): every open Lucent window
/// shown as a panel inside the player's window, with one title bar across the top. The windows themselves
/// stay open, out of sight, with their insides lent to the player's window (MainWindow.SingleWindow.cs), so
/// everything that asks whether one's open still gets the right answer. This is what the windows and helpers
/// outside the player need to know about it. Set up once by the player as Lucent starts, like
/// <see cref="LucentFullScreen"/>.
/// </summary>
public static class SingleWindow
{
    /// <summary>The key that switches it, as menus show it.</summary>
    public static readonly KeyGesture Gesture = new(Key.U);

    private static readonly object Marker = new();

    /// <summary>Whether it's on.</summary>
    public static Func<bool> IsOn { get; set; } = () => false;

    /// <summary>Switches it on or off.</summary>
    public static Action? Toggle { get; set; }

    /// <summary>Whether it can be switched on now (not with a classic skin, which draws its own fixed-size windows).</summary>
    public static Func<bool> IsAvailable { get; set; } = () => true;

    /// <summary>The panel showing a window's insides while it's in the single window, or null.</summary>
    public static Func<Window, Control?> PaneOf { get; set; } = _ => null;

    /// <summary>
    /// Takes a window out of the single window to show on its own, in its panel's place, for its own full
    /// screen. True if it was in it, so it goes back in with <see cref="PutBack"/> afterwards.
    /// </summary>
    public static Func<Window, bool> TakeOut { get; set; } = _ => false;

    /// <summary>Puts a window taken out with <see cref="TakeOut"/> back in the single window, if it's still on.</summary>
    public static Action<Window> PutBack { get; set; } = _ => { };

    /// <summary>
    /// Starts dragging a panel by its title bar (or by whatever drags its window), to drop it on another panel
    /// and swap the two. False when <paramref name="from"/> isn't in a panel that can swap (the equalizer
    /// sizes itself), so the window moves as usual.
    /// </summary>
    public static Func<Visual, PointerPressedEventArgs, bool> DragPanel { get; set; } = (_, _) => false;

    /// <summary>True while the window is a panel in the single window.</summary>
    public static bool Holds(Window window) => PaneOf(window) is not null;

    /// <summary>The window a window's drag moves: the single window while it's a panel in it, otherwise itself.</summary>
    public static Window FrameOf(Window window) => PaneOf(window)?.GetVisualRoot() is Window frame ? frame : window;

    /// <summary>Where to show something for a window: its panel while it's one, otherwise the window itself.</summary>
    public static Control Shown(Control control) => control is Window window && PaneOf(window) is { } pane ? pane : control;

    /// <summary>Adds "Single window", ticked while it's on, to a menu, after "Full screen Lucent". A menu kept and opened again gets it once.</summary>
    public static MenuItem Item()
    {
        var item = new MenuItem
        {
            Header = "Single window", InputGesture = Gesture, ToggleType = MenuItemToggleType.CheckBox, Tag = Marker,
            IsChecked = IsOn(), IsEnabled = IsOn() || IsAvailable(),
        };
        ToolTip.SetTip(item, Lucent.Visualizer.MenuTips.SingleWindow);
        item.Click += (_, _) => Toggle?.Invoke();
        return item;
    }

    /// <summary>Ticks the menu's "Single window" as it opens.</summary>
    public static void Tick(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>().Where(entry => entry.Tag == Marker))
        {
            item.IsChecked = IsOn();
            item.IsEnabled = IsOn() || IsAvailable();
        }
    }
}
