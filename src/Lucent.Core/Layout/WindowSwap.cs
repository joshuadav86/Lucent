namespace WinampInspiredVisualizer.Layout;

/// <summary>
/// One of Lucent's windows a dragged window could trade places with: where it is, its smallest size,
/// and whether it can trade at all (the player and the equalizer size themselves, and a locked window
/// stays put, so they can't).
/// </summary>
public readonly record struct SwapCandidate(TileRect Rect, double MinWidth, double MinHeight, bool CanSwap);

/// <summary>
/// Dragging one window onto another to swap them: drop a window with the mouse well inside another of
/// Lucent's windows and the two trade places and sizes. Shared by both apps' snappers, which work in
/// their own units (pixels on Windows, points on the Mac), so every distance here is in whatever units
/// it's given. Also keeps the swaps made while Lucent's full screen, so laying the windows out again
/// keeps them where they were swapped to.
/// </summary>
public static class WindowSwap
{
    /// <summary>
    /// How far (at 100% scaling) the mouse has to be inside a window before dropping there swaps. The
    /// snapper keeps a window that's pushed less than this far into another against that window's edge,
    /// and the mouse is never further in than the window's own edge, so this never takes over a drag
    /// that's lining a window up beside another.
    /// </summary>
    public const double Depth = 64;

    /// <summary>
    /// The window a drop at this point would swap with, as its index in <paramref name="others"/>, or -1
    /// for none. It's the window the mouse is well inside (by <paramref name="depth"/>, or a quarter of
    /// the window's size if that's smaller), and the two must each fit the other's place at their
    /// smallest size.
    /// </summary>
    /// <param name="dragged">Where the dragged window was when the drag began: the place the other one takes.</param>
    public static int TargetAt(double x, double y, SwapCandidate dragged, IReadOnlyList<SwapCandidate> others, double depth)
    {
        if (!dragged.CanSwap)
        {
            return -1;
        }

        for (var i = 0; i < others.Count; i++)
        {
            var other = others[i];
            var rect = other.Rect;
            var insetX = Math.Min(depth, rect.Width / 4);
            var insetY = Math.Min(depth, rect.Height / 4);
            if (x < rect.Left + insetX || x > rect.Right - insetX || y < rect.Top + insetY || y > rect.Bottom - insetY)
            {
                continue;
            }

            // The topmost window under the mouse decides, whether or not it can swap.
            return other.CanSwap && Fits(dragged, rect) && Fits(other, dragged.Rect) ? i : -1;
        }

        return -1;
    }

    /// <summary>True when the window can be the size of that place.</summary>
    public static bool Fits(SwapCandidate window, TileRect place) =>
        place.Width >= window.MinWidth - 0.5 && place.Height >= window.MinHeight - 0.5;

    /// <summary>
    /// Adds a swap of two windows (by their keys) to the swaps kept while Lucent's full screen. Only the
    /// most recent ones are kept, which is plenty to say where each window is.
    /// </summary>
    public static void Remember(List<List<string>> swaps, string first, string second)
    {
        const int most = 32;
        swaps.Add(new List<string> { first, second });
        if (swaps.Count > most)
        {
            swaps.RemoveRange(0, swaps.Count - most);
        }
    }

    /// <summary>
    /// Swaps the places <see cref="FullScreenLayout.Arrange"/> gave the windows, as the user swapped them,
    /// in the order they did. A swap is skipped when one of the two isn't open, keeps its own size, or
    /// wouldn't fit the other's place at its smallest, so the layout stays tidy whatever's open.
    /// </summary>
    public static void Apply(TileResult layout, IReadOnlyList<TileWindow> windows, IReadOnlyList<List<string>> swaps)
    {
        foreach (var swap in swaps)
        {
            if (swap.Count != 2
                || windows.FirstOrDefault(window => window.Key == swap[0]) is not { FixedSize: false } first
                || windows.FirstOrDefault(window => window.Key == swap[1]) is not { FixedSize: false } second
                || !layout.Places.TryGetValue(first.Key, out var firstPlace)
                || !layout.Places.TryGetValue(second.Key, out var secondPlace)
                || !Fits(new SwapCandidate(firstPlace, first.MinWidth, first.MinHeight, true), secondPlace)
                || !Fits(new SwapCandidate(secondPlace, second.MinWidth, second.MinHeight, true), firstPlace))
            {
                continue;
            }

            layout.Places[first.Key] = secondPlace;
            layout.Places[second.Key] = firstPlace;
        }
    }
}
