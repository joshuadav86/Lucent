using Avalonia;
using Avalonia.Controls;

namespace Lucent.Mac.Windows;

/// <summary>
/// WPF's Left, Top, ActualWidth and ActualHeight for Avalonia windows, so window layout code reads as it
/// does on Windows. Everything is in the window's own units (what Width and Height use): Avalonia keeps a
/// window's position in desktop pixels, which this converts with the window's desktop scaling.
/// </summary>
public static class Placement
{
    public static double Scale(Window window) => window.DesktopScaling > 0 ? window.DesktopScaling : 1;

    public static double Left(this Window window) => window.Position.X / Scale(window);

    public static double Top(this Window window) => window.Position.Y / Scale(window);

    public static void SetLeft(this Window window, double left) => window.MoveTo(left, window.Top());

    public static void SetTop(this Window window, double top) => window.MoveTo(window.Left(), top);

    public static void MoveTo(this Window window, double left, double top)
    {
        var scale = Scale(window);
        var position = new PixelPoint((int)Math.Round(left * scale), (int)Math.Round(top * scale));
        if (window.Position != position)
        {
            window.Position = position;
        }
    }

    /// <summary>
    /// The window's size as drawn. A window that hasn't been shown yet has no drawn size, only the one
    /// it's been given (like WPF's ActualWidth falling back to Width).
    /// </summary>
    public static Size SizeOf(Window window)
    {
        var width = window.Bounds.Width > 0 ? window.Bounds.Width : !double.IsNaN(window.Width) ? window.Width : window.ClientSize.Width;
        var height = window.Bounds.Height > 0 ? window.Bounds.Height : !double.IsNaN(window.Height) ? window.Height : window.ClientSize.Height;
        return new Size(width, height);
    }

    public static double ActualWidth(this Window window) => SizeOf(window).Width;

    public static double ActualHeight(this Window window) => SizeOf(window).Height;

    /// <summary>Where the window is and how big, in its own units.</summary>
    public static Rect BoundsOf(Window window)
    {
        var size = SizeOf(window);
        return new Rect(window.Left(), window.Top(), size.Width, size.Height);
    }

    /// <summary>Puts a window somewhere at a size, as WPF code does by setting Left, Top, Width and Height.</summary>
    public static void Place(Window window, double left, double top, double width, double height)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.MoveTo(left, top);
        window.Width = Math.Max(width, 1);
        window.Height = Math.Max(height, 1);
    }

    /// <summary>The usable part (without the menu bar and Dock) of the screen a window is on, in its own units.</summary>
    public static Rect WorkArea(Window window)
    {
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        return InUnits(window, screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900));
    }

    /// <summary>The usable part of the screen that a rectangle (in the window's units) is mostly on.</summary>
    public static Rect WorkAreaAt(Window window, Rect bounds)
    {
        var scale = Scale(window);
        var pixels = new PixelRect((int)(bounds.X * scale), (int)(bounds.Y * scale), Math.Max(1, (int)(bounds.Width * scale)), Math.Max(1, (int)(bounds.Height * scale)));
        var screen = window.Screens.ScreenFromBounds(pixels) ?? window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
        return InUnits(window, screen?.WorkingArea ?? new PixelRect(0, 0, 1440, 900));
    }

    /// <summary>All the screens together (WPF's SystemParameters.VirtualScreen...).</summary>
    public static Rect VirtualScreen(Window window)
    {
        var all = window.Screens.All;
        if (all.Count == 0)
        {
            return InUnits(window, new PixelRect(0, 0, 1440, 900));
        }

        var union = all[0].Bounds;
        foreach (var screen in all.Skip(1))
        {
            union = union.Union(screen.Bounds);
        }

        return InUnits(window, union);
    }

    private static Rect InUnits(Window window, PixelRect area)
    {
        var scale = Scale(window);
        return new Rect(area.X / scale, area.Y / scale, area.Width / scale, area.Height / scale);
    }
}
