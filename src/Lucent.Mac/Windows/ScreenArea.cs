using Avalonia;
using Avalonia.Controls;

namespace Lucent.Mac.Windows;

/// <summary>Finds the usable part (without the menu bar and Dock) of the screen a window is on.</summary>
public static class ScreenArea
{
    /// <summary>
    /// The work area of the window's screen, in the same units as the window's position and size
    /// (Avalonia positions windows in device pixels, and sizes them in its own units).
    /// </summary>
    public static PixelRect Of(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        return screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900);
    }

    /// <summary>The work area in the window's own units (what Width and Height use).</summary>
    public static Rect InUnitsOf(Window window)
    {
        var area = Of(window);
        var scale = window.Screens.ScreenFromWindow(window)?.Scaling ?? window.RenderScaling;
        return new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
    }
}
