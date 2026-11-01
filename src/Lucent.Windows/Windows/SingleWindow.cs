using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Lucent.Windows;

/// <summary>
/// Single window (U, or "Single window" on a right-click): every open Lucent window shown as a panel inside
/// the player's window, with one title bar across the top. The windows themselves stay open, out of sight,
/// with their insides lent to the player's window (MainWindow.SingleWindow.cs), so everything that asks
/// whether one's open still gets the right answer. This is what the windows and helpers outside the player
/// need to know about it. Set up once by the player as Lucent starts, like <see cref="LucentFullScreen"/>.
/// </summary>
public static class SingleWindow
{
    /// <summary>The key that switches it, as menus show it.</summary>
    public const string GestureText = "U";

    private const int WM_WINDOWPOSCHANGING = 0x0046;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const int SW_HIDE = 0;
    private const int SW_SHOWNA = 8;

    private static readonly object Marker = new();

    /// <summary>Whether it's on.</summary>
    public static Func<bool> IsOn { get; set; } = () => false;

    /// <summary>Switches it on or off.</summary>
    public static Action? Toggle { get; set; }

    /// <summary>Whether it can be switched on now (not with a classic skin, which draws its own fixed-size windows).</summary>
    public static Func<bool> IsAvailable { get; set; } = () => true;

    /// <summary>The panel showing a window's insides while it's in the single window, or null.</summary>
    public static Func<Window, FrameworkElement?> PaneOf { get; set; } = _ => null;

    /// <summary>Whether a window opening now goes in the single window rather than on the screen.</summary>
    public static Func<Window, bool> KeepsOutOfSight { get; set; } = _ => false;

    /// <summary>
    /// Takes a window out of the single window to show on its own, in its panel's place, for its own full
    /// screen. True if it was in it, so it goes back in with <see cref="PutBack"/> afterwards.
    /// </summary>
    public static Func<Window, bool> TakeOut { get; set; } = _ => false;

    /// <summary>Puts a window taken out with <see cref="TakeOut"/> back in the single window, if it's still on.</summary>
    public static Action<Window> PutBack { get; set; } = _ => { };

    /// <summary>
    /// Starts dragging a panel by whatever drags its window (the analyzer's picture, say), to drop it on another
    /// panel and swap the two. False when <paramref name="from"/> isn't in a panel that can swap (the equalizer
    /// sizes itself), so the window moves as usual.
    /// </summary>
    public static Func<DependencyObject, MouseButtonEventArgs, bool> DragPanel { get; set; } = (_, _) => false;

    /// <summary>True while the window is a panel in the single window.</summary>
    public static bool Holds(Window window) => PaneOf(window) is not null;

    /// <summary>The window a window's drag moves: the single window while it's a panel in it, otherwise itself.</summary>
    public static Window FrameOf(Window window) => PaneOf(window) is { } pane && Window.GetWindow(pane) is { } frame ? frame : window;

    /// <summary>Raised when a watched window is shown or hidden, for the player to give it a panel or take its panel away.</summary>
    public static Action<Window>? ShownOrHidden { get; set; }

    /// <summary>Where to show something for a window: its panel while it's one, otherwise the window itself.</summary>
    public static FrameworkElement Shown(FrameworkElement element) => element is Window window && PaneOf(window) is { } pane ? pane : element;

    /// <summary>
    /// Watches a window that can be a panel, from when it's first made: while it's one (or about to be), Windows
    /// is stopped from putting it on the screen when WPF shows it, so it never flashes up empty, and it doesn't
    /// take the focus from the single window. WPF still counts it as shown.
    /// </summary>
    public static void Watch(Window window)
    {
        window.IsVisibleChanged += (_, _) => ShownOrHidden?.Invoke(window);
        window.SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(
            (IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (msg == WM_WINDOWPOSCHANGING && lParam != IntPtr.Zero && KeepsOutOfSight(window))
                {
                    var position = Marshal.PtrToStructure<WindowPos>(lParam);
                    if ((position.Flags & SWP_SHOWWINDOW) != 0)
                    {
                        position.Flags = (position.Flags & ~SWP_SHOWWINDOW) | SWP_NOACTIVATE;
                        Marshal.StructureToPtr(position, lParam, false);
                    }
                }
                else if (msg == WM_WINDOWPOSCHANGED && lParam != IntPtr.Zero && Holds(window)
                         && (Marshal.PtrToStructure<WindowPos>(lParam).Flags & SWP_SHOWWINDOW) != 0)
                {
                    // Shown anyway while its insides are in a panel (a way of showing that skips the check
                    // above): an empty window over the single window, which once hid the radio's panel. It's
                    // put away again as soon as the showing's done.
                    window.Dispatcher.InvokeAsync(() =>
                    {
                        if (Holds(window))
                        {
                            PutAway(window);
                        }
                    });
                }

                return IntPtr.Zero;
            });
    }

    /// <summary>Takes a window off the screen without closing it: WPF still counts it as shown.</summary>
    public static void PutAway(Window window)
    {
        if (new WindowInteropHelper(window).Handle is var handle && handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_HIDE);
        }
    }

    /// <summary>Puts a window put away with <see cref="PutAway"/> back on the screen, without taking the focus.</summary>
    public static void BringBack(Window window)
    {
        if (new WindowInteropHelper(window).Handle is var handle && handle != IntPtr.Zero)
        {
            ShowWindow(handle, SW_SHOWNA);
        }
    }

    /// <summary>"Single window", ticked while it's on, for a right-click menu after "Full screen Lucent".</summary>
    public static MenuItem Item()
    {
        var item = new MenuItem
        {
            Header = "Single window", InputGestureText = GestureText, IsCheckable = true, Tag = Marker,
            IsChecked = IsOn(), IsEnabled = IsOn() || IsAvailable(),
            ToolTip = $"{Lucent.Visualizer.MenuTips.SingleWindow} Press {GestureText}.",
        };
        item.Click += (_, _) => Toggle?.Invoke();
        return item;
    }

    /// <summary>Ticks a menu's "Single window" as it opens.</summary>
    public static void Tick(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>().Where(entry => entry.Tag == Marker))
        {
            item.IsChecked = IsOn();
            item.IsEnabled = IsOn() || IsAvailable();
        }
    }

    /// <summary>The element a key or a drop happened on, as a visual (a run of text in a text box is its text box).</summary>
    public static DependencyObject? VisualOf(object? source)
    {
        var element = source as DependencyObject;
        while (element is not null and not Visual)
        {
            element = LogicalTreeHelper.GetParent(element);
        }

        return element;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr InsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int command);
}
