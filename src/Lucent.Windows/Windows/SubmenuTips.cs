using System.Windows;
using System.Windows.Controls;

namespace Lucent.Windows;

/// <summary>
/// Keeps a menu item's tooltip out of the way of its own submenu. WPF turns an open item's tooltip off, but one
/// already showing (or about to, once its delay runs out) stayed up and sat behind the submenu, cut off. Now the
/// tooltip closes as the submenu opens and won't open while it's open; it's back once the submenu closes.
/// </summary>
public static class SubmenuTips
{
    private static bool _installed;

    /// <summary>Sets it up for every menu item in Lucent. Safe to call more than once.</summary>
    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        _installed = true;
        EventManager.RegisterClassHandler(typeof(MenuItem), ToolTipService.ToolTipOpeningEvent, new ToolTipEventHandler(OnToolTipOpening));
        EventManager.RegisterClassHandler(typeof(MenuItem), MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(OnSubmenuOpened));
    }

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is not MenuItem item)
        {
            return;
        }

        if (item.IsSubmenuOpen)
        {
            e.Handled = true;
            return;
        }

        // A plain-text tip is shown in a tooltip WPF owns, which nothing else can close. Our own ToolTip around
        // the same text can be closed when the submenu opens. WPF reads the tip after this event, so it uses it.
        if (item.ToolTip is { } tip and not ToolTip)
        {
            item.ToolTip = new ToolTip { Content = tip };
        }
    }

    private static void OnSubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { ToolTip: ToolTip { IsOpen: true } tip })
        {
            tip.IsOpen = false;
        }
    }
}
