namespace Lucent.Layout;

/// <summary>
/// Single window (U, or "Single window" on a right-click): Lucent's windows shown as panels inside one
/// window, with one title bar across the top. The panels are laid out the way Lucent full screen lays the
/// windows out (<see cref="FullScreenLayout"/>), in the part of the window under the title bar instead of
/// across a screen, so the tidy layouts and swaps work the same. Locks don't apply inside the window.
/// Shared by both apps; tools/LayoutCheck checks it.
/// </summary>
public static class SingleWindowLayout
{
    /// <summary>The narrowest and shortest the window's body is ever made, whatever's open.</summary>
    public const double SmallestSide = 200;

    /// <summary>The most the window's body is searched up to for a size that fits every panel.</summary>
    private const double LargestSide = 8000;

    /// <summary>The step the smallest size is found to.</summary>
    private const double Step = 8;

    /// <summary>
    /// Lays the panels out in a body <paramref name="width"/> by <paramref name="height"/>, its top left
    /// corner at 0, 0, keeping the swaps made by dragging one panel onto another. Panels that don't fit at
    /// their smallest are left out until the window's big enough.
    /// </summary>
    public static TileResult Arrange(double width, double height, IReadOnlyList<TileWindow> panels, TileStyle style,
        IReadOnlyList<List<string>> swaps)
    {
        var free = Unlocked(panels);
        var result = FullScreenLayout.Arrange(new TileRect(0, 0, width, height), free, style);
        WindowSwap.Apply(result, free, swaps);
        return result;
    }

    /// <summary>
    /// Tidy layout (Shift+D) in the single window: the next tidy layout that moves something, as
    /// <see cref="FullScreenLayout.Tidy"/> does across a screen.
    /// </summary>
    public static (TileStyle Style, TileResult Result) Tidy(double width, double height, IReadOnlyList<TileWindow> panels,
        IReadOnlyDictionary<string, TileRect> now, TileStyle current) =>
        FullScreenLayout.Tidy(new TileRect(0, 0, width, height), Unlocked(panels), now, current);

    /// <summary>True when every panel fits a body this size.</summary>
    public static bool Fits(double width, double height, IReadOnlyList<TileWindow> panels, TileStyle style) =>
        FullScreenLayout.Arrange(new TileRect(0, 0, width, height), Unlocked(panels), style).LeftOut.Count == 0;

    /// <summary>
    /// The window's body's smallest width and height: narrower than the width, nothing fits however tall the
    /// window is, and shorter than the height, nothing fits however wide. (At both at once some panels can
    /// still be left out; <see cref="Grown"/> gives a size that fits them all.)
    /// </summary>
    public static (double Width, double Height) Smallest(IReadOnlyList<TileWindow> panels, TileStyle style)
    {
        if (panels.Count == 0)
        {
            return (SmallestSide, SmallestSide);
        }

        var free = Unlocked(panels);
        var width = Search(Math.Max(SmallestSide, free.Max(panel => panel.MinWidth)), size => Fits(size, LargestSide, free, style));
        var height = Search(Math.Max(SmallestSide, free.Max(panel => panel.MinHeight)), size => Fits(LargestSide, size, free, style));
        return (width, height);
    }

    /// <summary>
    /// A body at least <paramref name="width"/> by <paramref name="height"/> that every panel fits, grown a
    /// little at a time (taller first, as full screen's compact block does) but never past
    /// <paramref name="most"/>. If nothing up to that fits them all, it's <paramref name="most"/> (or the size
    /// asked for, if that's already bigger), and the panels that don't fit are left out.
    /// </summary>
    public static (double Width, double Height) Grown(double width, double height, IReadOnlyList<TileWindow> panels,
        TileStyle style, (double Width, double Height) most)
    {
        var free = Unlocked(panels);
        var (smallestWidth, smallestHeight) = Smallest(free, style);
        width = Math.Max(width, smallestWidth);
        height = Math.Max(height, smallestHeight);
        if (Fits(width, height, free, style))
        {
            return (width, height);
        }

        foreach (var (wider, taller) in new[] { (1.0, 1.1), (1.0, 1.25), (1.1, 1.25), (1.1, 1.5), (1.25, 1.5), (1.25, 2.0), (1.5, 2.0), (2.0, 2.0) })
        {
            var tryWidth = Math.Min(Math.Max(width, most.Width), width * wider);
            var tryHeight = Math.Min(Math.Max(height, most.Height), height * taller);
            if (Fits(tryWidth, tryHeight, free, style))
            {
                return (tryWidth, tryHeight);
            }
        }

        return (Math.Max(width, most.Width), Math.Max(height, most.Height));
    }

    /// <summary>
    /// Where the window goes as single window is switched on: around the windows that are open now (their
    /// outer edges, <paramref name="windows"/>), kept on the screen. The body is that less the title bar.
    /// </summary>
    public static TileRect Around(IReadOnlyCollection<TileRect> windows, TileRect screen)
    {
        if (windows.Count == 0)
        {
            return screen;
        }

        var left = windows.Min(rect => rect.Left);
        var top = windows.Min(rect => rect.Top);
        var bounds = new TileRect(left, top, windows.Max(rect => rect.Right) - left, windows.Max(rect => rect.Bottom) - top);
        return OnScreen(bounds, screen);
    }

    /// <summary>The rectangle no bigger than the screen, moved as little as it takes to be on it.</summary>
    public static TileRect OnScreen(TileRect rect, TileRect screen)
    {
        var width = Math.Min(rect.Width, screen.Width);
        var height = Math.Min(rect.Height, screen.Height);
        return new TileRect(
            Math.Max(screen.Left, Math.Min(rect.Left, screen.Right - width)),
            Math.Max(screen.Top, Math.Min(rect.Top, screen.Bottom - height)),
            width, height);
    }

    private static List<TileWindow> Unlocked(IReadOnlyList<TileWindow> panels) =>
        panels.Select(panel => panel.LockedAt is null ? panel : panel with { LockedAt = null }).ToList();

    /// <summary>The smallest size from <paramref name="from"/> up (to <see cref="Step"/>) that <paramref name="fits"/>, or the largest searched.</summary>
    private static double Search(double from, Func<double, bool> fits)
    {
        if (fits(from))
        {
            return from;
        }

        if (!fits(LargestSide))
        {
            return LargestSide;
        }

        // Fits at the top and not at the bottom: halve the gap until it's a step wide.
        var low = from;
        var high = LargestSide;
        while (high - low > Step)
        {
            var middle = (low + high) / 2;
            if (fits(middle))
            {
                high = middle;
            }
            else
            {
                low = middle;
            }
        }

        return Math.Ceiling(high);
    }
}
