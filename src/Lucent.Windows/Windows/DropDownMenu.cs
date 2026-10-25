using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// Opens a menu under (or over) the button it belongs to, kept clear of the taskbar. A long menu
/// opens on whichever side of the button has room for all of it, or the side with more room, and
/// scrolls if it still doesn't fit.
/// </summary>
public static class DropDownMenu
{
    // Space kept between the menu and the edge of the screen's usable area.
    private const double ScreenMargin = 6;

    // Never squeeze a menu shorter than this, even next to a button at the very edge of the screen.
    private const double ShortestMenu = 120;

    public static void Open(ContextMenu menu, FrameworkElement button, double gap = 4)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.VerticalOffset = gap;

        if (Window.GetWindow(button) is { } window && PresentationSource.FromVisual(button)?.CompositionTarget is { } target)
        {
            var area = ScreenArea.Of(window);
            var fromDevice = target.TransformFromDevice;
            var top = fromDevice.Transform(button.PointToScreen(new Point(0, 0))).Y;
            var bottom = fromDevice.Transform(button.PointToScreen(new Point(0, button.ActualHeight))).Y;
            var below = area.Bottom - bottom - gap - ScreenMargin;
            var above = top - area.Top - gap - ScreenMargin;

            menu.ApplyTemplate();
            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var height = menu.DesiredSize.Height;

            var downward = height <= below || below >= above;
            if (!downward)
            {
                menu.Placement = PlacementMode.Top;
                menu.VerticalOffset = -gap;
            }

            menu.MaxHeight = Math.Max(ShortestMenu, downward ? below : above);
        }

        menu.IsOpen = true;
    }
}
