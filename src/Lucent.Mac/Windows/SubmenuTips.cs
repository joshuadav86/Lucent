using Avalonia;
using Avalonia.Controls;

namespace Lucent.Mac.Windows;

/// <summary>
/// Keeps a menu item's tooltip out of the way of its own submenu. One already showing (or about to, once its delay
/// ran out) stayed up and sat over or under the submenu. Now the tooltip closes as the submenu opens and won't
/// open while it's open; it's back once the submenu closes.
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
        ToolTip.ToolTipOpeningEvent.AddClassHandler<MenuItem>((item, e) =>
        {
            if (item.IsSubMenuOpen)
            {
                e.Cancel = true;
            }
        });
        MenuItem.IsSubMenuOpenProperty.Changed.AddClassHandler<MenuItem>((item, e) =>
        {
            if (e.NewValue is true && ToolTip.GetIsOpen(item))
            {
                ToolTip.SetIsOpen(item, false);
            }
        });
    }
}
