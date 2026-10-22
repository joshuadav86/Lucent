namespace WinampInspiredVisualizer.Layout;

/// <summary>Which of Lucent's windows a <see cref="TileWindow"/> is, which decides where it goes.</summary>
public enum TileKind
{
    Player,
    Equalizer,
    Queue,
    Library,
    Artwork,
    Visuals,
    Video,
    Analyzer,
    Radio,
    Editor,
}

/// <summary>A rectangle on the screen, in the window units the apps place windows in (WPF's and Avalonia's own, not pixels).</summary>
public readonly record struct TileRect(double Left, double Top, double Width, double Height)
{
    public double Right => Left + Width;

    public double Bottom => Top + Height;

    public double Area => Math.Max(0, Width) * Math.Max(0, Height);

    /// <summary>True when the two share more than a sliver (a point of rounding either way doesn't count).</summary>
    public bool Overlaps(TileRect other) =>
        Left < other.Right - 1 && other.Left < Right - 1 && Top < other.Bottom - 1 && other.Top < Bottom - 1;

    /// <summary>True when this lies inside <paramref name="outer"/>, give or take a point.</summary>
    public bool IsInside(TileRect outer) =>
        Left >= outer.Left - 1 && Top >= outer.Top - 1 && Right <= outer.Right + 1 && Bottom <= outer.Bottom + 1;
}

/// <summary>
/// One open window for <see cref="FullScreenLayout"/>: its smallest size, its size now (what it'd like
/// to keep), and where it's locked, if it is.
/// </summary>
/// <param name="Key">What the app knows it by, e.g. "Queue". Two editors need two keys.</param>
/// <param name="FixedSize">It can't be resized (the player and the equalizer size themselves): it keeps <paramref name="Width"/> and <paramref name="Height"/>.</param>
/// <param name="LockedAt">Where it's locked: it stays there, and the others fill the screen around it.</param>
public sealed record TileWindow(string Key, TileKind Kind, double MinWidth, double MinHeight, double Width, double Height,
    bool FixedSize = false, TileRect? LockedAt = null);

/// <summary>Where <see cref="FullScreenLayout.Arrange"/> put each window.</summary>
public sealed class TileResult
{
    /// <summary>Each window it placed, by key. Locked windows aren't in here: they stay where they are.</summary>
    public Dictionary<string, TileRect> Places { get; } = new();

    /// <summary>Windows that didn't fit on this screen even at their smallest. They're left where they are, in front.</summary>
    public List<string> LeftOut { get; } = new();
}

/// <summary>
/// Lucent full screen: every open window tiled across the whole of one screen, docked edge to edge with
/// nothing on top of anything. The player keeps its size at the top of a column, with the equalizer and
/// the queue under it down to the bottom; the library (with the artwork under it) runs down the left;
/// the visuals fill the rest, with the analyzer, the radio and the editor side by side under them.
/// On a small screen (or a large scaling) whatever doesn't fit beside the player moves into the space
/// that's left: the library under the queue, the queue and the artwork under the visuals. A window that
/// can't fit anywhere at its smallest size is left out rather than squashed or put over another.
/// Shared by both apps so they lay out the same; tools/LayoutCheck checks it on many screens and scalings.
/// </summary>
public static class FullScreenLayout
{
    /// <summary>The most of the screen's width the library column takes when there's more to show.</summary>
    private const double SideShare = 0.3;

    /// <summary>The visuals (or whatever's biggest on the right) keep at least this much of their column's height when there's room.</summary>
    private const double MainShare = 0.5;

    /// <param name="area">The screen's usable part (without the taskbar, or the menu bar and Dock).</param>
    public static TileResult Arrange(TileRect area, IReadOnlyList<TileWindow> windows)
    {
        // With the library column, or with it moved out of the way to make room: whichever fits more in.
        var withSide = Arrange(area, windows, keepSide: true);
        if (withSide.LeftOut.Count == 0)
        {
            return withSide;
        }

        var withoutSide = Arrange(area, windows, keepSide: false);
        return withoutSide.LeftOut.Count < withSide.LeftOut.Count ? withoutSide : withSide;
    }

    private static TileResult Arrange(TileRect area, IReadOnlyList<TileWindow> windows, bool keepSide)
    {
        var result = new TileResult();

        // Locked windows keep their spots, and the rest fill the biggest part of the screen beside them. A
        // locked player is different: the column under it is laid out from where it is.
        foreach (var locked in windows.Where(window => window.LockedAt is not null && window.Kind != TileKind.Player))
        {
            area = Beside(area, locked.LockedAt!.Value);
        }

        var free = windows.Where(window => window.LockedAt is null).ToList();
        var player = windows.FirstOrDefault(window => window.Kind == TileKind.Player);
        var equalizer = free.FirstOrDefault(window => window.Kind == TileKind.Equalizer);
        var columnWidth = Math.Min(player?.Width ?? equalizer?.Width ?? 0, area.Width);
        var playerHeight = player?.Height ?? 0;
        var columnTop = area.Top;
        double? lockedLeft = null;
        if (player?.LockedAt is { } playerAt)
        {
            lockedLeft = Math.Clamp(playerAt.Left, area.Left, Math.Max(area.Left, area.Right - columnWidth));
            columnTop = Math.Clamp(playerAt.Top, area.Top, Math.Max(area.Top, area.Bottom - playerHeight));
        }

        // On a short screen the equalizer doesn't fit under the player: it goes under the visuals instead.
        var equalizerBeside = equalizer is not null && columnTop + playerHeight + equalizer.Height > area.Bottom + 0.5;
        var equalizerHeight = equalizer is null || equalizerBeside ? 0 : equalizer.Height;

        var below = columnTop + playerHeight + equalizerHeight;
        var underHeight = area.Bottom - below;

        // What goes under the player, down the side, and in the big space.
        var under = free.Where(window => window.Kind == TileKind.Queue).ToList();
        var side = free.Where(window => window.Kind is TileKind.Library or TileKind.Artwork).OrderBy(window => window.Kind).ToList();
        var main = free.Where(window => window.Kind is TileKind.Visuals or TileKind.Video or TileKind.Analyzer or TileKind.Radio or TileKind.Editor)
            .OrderBy(window => window.Kind).ToList();
        if (equalizerBeside)
        {
            main.Insert(main.Count > 0 ? 1 : 0, equalizer!);
        }

        if (under.Count > 0 && (underHeight < under[0].MinHeight || columnWidth < under[0].MinWidth))
        {
            // No room for the queue under the player: under the visuals instead, first in line.
            main.InsertRange(main.Count > 0 ? 1 : 0, under);
            under.Clear();
        }

        TileRect sideRegion;
        TileRect mainRegion;
        double columnLeft;
        if (lockedLeft is { } fixedLeft)
        {
            columnLeft = fixedLeft;
            var left = new TileRect(area.Left, area.Top, columnLeft - area.Left, area.Height);
            var right = new TileRect(columnLeft + columnWidth, area.Top, area.Right - columnLeft - columnWidth, area.Height);
            // The big windows go on whichever side of the locked player they fit on, the wider if both.
            var mainOnRight = right.Width >= left.Width || right.Width >= NeededWidth(main);
            (sideRegion, mainRegion) = mainOnRight ? (left, right) : (right, left);
            if (!keepSide || sideRegion.Width < NeededWidth(side))
            {
                MoveSide(side, under, main, underHeight, columnWidth);
            }
        }
        else
        {
            var sideWidth = 0.0;
            if (side.Count > 0)
            {
                // Room beside the column for the big windows, leaving out any too wide to fit beside the library anyway.
                var besideColumn = area.Width - columnWidth;
                var room = besideColumn - NeededWidth(main.Where(window => window.MinWidth <= besideColumn - NeededWidth(side)).ToList());
                if (main.Count == 0)
                {
                    sideWidth = Math.Max(0, area.Width - columnWidth);
                }
                else if (keepSide && room >= NeededWidth(side))
                {
                    var wanted = Math.Clamp(side[0].Width, NeededWidth(side), Math.Max(NeededWidth(side), area.Width * SideShare));
                    sideWidth = Math.Min(wanted, room);
                }

                if (sideWidth < NeededWidth(side))
                {
                    sideWidth = 0;
                    MoveSide(side, under, main, underHeight, columnWidth);
                }
            }

            columnLeft = area.Left + sideWidth;
            sideRegion = new TileRect(area.Left, area.Top, sideWidth, area.Height);
            mainRegion = new TileRect(columnLeft + columnWidth, area.Top, area.Right - columnLeft - columnWidth, area.Height);
        }

        if (player is { LockedAt: null })
        {
            result.Places[player.Key] = new TileRect(columnLeft, columnTop, player.Width, player.Height);
        }

        if (equalizer is not null && !equalizerBeside)
        {
            result.Places[equalizer.Key] = new TileRect(columnLeft, columnTop + playerHeight, columnWidth, equalizerHeight);
        }

        // With nothing beside it, the queue (or the library) runs the whole width under the player.
        var underWidth = main.Count == 0 && side.Count == 0 && lockedLeft is null ? area.Right - columnLeft : columnWidth;
        Pack(new TileRect(columnLeft, below, underWidth, underHeight), under, result);
        Pack(sideRegion, side, result);
        Pack(mainRegion, main, result);
        return result;
    }

    /// <summary>The narrowest a region can be and still take every one of these (each on a row of its own).</summary>
    private static double NeededWidth(IReadOnlyList<TileWindow> windows) => windows.Count == 0 ? 0 : windows.Max(window => window.MinWidth);

    /// <summary>
    /// The library column doesn't fit beside the player: the library goes under the queue if there's
    /// height for it there, otherwise under the visuals, like the artwork.
    /// </summary>
    private static void MoveSide(List<TileWindow> side, List<TileWindow> under, List<TileWindow> main, double underHeight, double columnWidth)
    {
        foreach (var window in side)
        {
            var spare = underHeight - under.Sum(other => other.MinHeight);
            if (window.Kind == TileKind.Library && spare >= window.MinHeight && columnWidth >= window.MinWidth)
            {
                under.Add(window);
            }
            else
            {
                main.Add(window);
            }
        }

        side.Clear();
    }

    /// <summary>
    /// Lays windows out in a region: the first fills the top, and the rest go in rows under it; or, if
    /// that fits more in, or as many on a wide region, the first fills the left and the rest go in columns
    /// beside it.
    /// </summary>
    private static void Pack(TileRect region, List<TileWindow> windows, TileResult result)
    {
        var rows = new TileResult();
        PackRows(region, windows, rows);
        var columns = new TileResult();
        PackRows(Turned(region), windows.Select(Turned).ToList(), columns);
        var useColumns = columns.LeftOut.Count < rows.LeftOut.Count
            || columns.LeftOut.Count == rows.LeftOut.Count && region.Width > region.Height * 1.25;
        if (useColumns)
        {
            foreach (var (key, place) in columns.Places)
            {
                result.Places[key] = Turned(place);
            }

            result.LeftOut.AddRange(columns.LeftOut);
        }
        else
        {
            foreach (var (key, place) in rows.Places)
            {
                result.Places[key] = place;
            }

            result.LeftOut.AddRange(rows.LeftOut);
        }
    }

    /// <summary>Across for down: columns are laid out as rows of the turned region, and turned back.</summary>
    private static TileRect Turned(TileRect rect) => new(rect.Top, rect.Left, rect.Height, rect.Width);

    private static TileWindow Turned(TileWindow window) => window with
    {
        MinWidth = window.MinHeight,
        MinHeight = window.MinWidth,
        Width = window.Height,
        Height = window.Width,
        LockedAt = window.LockedAt is { } at ? Turned(at) : null,
    };

    /// <summary>
    /// The first window fills the top of the region, and the rest go in rows under it, side by side as
    /// far as the width allows. Rows that can't fit at their smallest are left out.
    /// </summary>
    private static void PackRows(TileRect region, List<TileWindow> windows, TileResult result)
    {
        var fitting = new List<TileWindow>();
        foreach (var window in windows)
        {
            if (window.MinWidth <= region.Width + 0.5)
            {
                fitting.Add(window);
            }
            else
            {
                result.LeftOut.Add(window.Key);
            }
        }

        if (fitting.Count == 0)
        {
            return;
        }

        var first = fitting[0];
        if (first.MinHeight > region.Height + 0.5)
        {
            result.LeftOut.AddRange(fitting.Select(window => window.Key));
            return;
        }

        // The rest, in rows as wide as the region.
        var rows = new List<List<TileWindow>>();
        foreach (var window in fitting.Skip(1))
        {
            if (rows.Count == 0 || rows[^1].Sum(other => other.MinWidth) + window.MinWidth > region.Width + 0.5)
            {
                rows.Add(new List<TileWindow>());
            }

            rows[^1].Add(window);
        }

        // Rows that don't fit under the first at their smallest are left out, the last first.
        while (rows.Count > 0 && first.MinHeight + rows.Sum(row => row.Max(window => window.MinHeight)) > region.Height + 0.5)
        {
            result.LeftOut.AddRange(rows[^1].Select(window => window.Key));
            rows.RemoveAt(rows.Count - 1);
        }

        var widths = rows.Select(row => Widths(row, region.Width)).ToList();
        var smallest = rows.Select(row => row.Max(window => window.MinHeight)).ToList();
        var wanted = rows.Select((row, index) => Math.Max(smallest[index], row.Select((window, column) => Wanted(window, widths[index][column])).Max())).ToList();

        // The first keeps at least half the height when it can; the rows share the rest, between their smallest and what they'd like.
        var budget = region.Height - Math.Max(first.MinHeight, Math.Min(region.Height * MainShare, region.Height - smallest.Sum()));
        var least = smallest.Sum();
        var most = wanted.Sum();
        var stretch = most - least <= 0 ? 1 : Math.Clamp((budget - least) / (most - least), 0, 1);
        var heights = rows.Select((_, index) => smallest[index] + (wanted[index] - smallest[index]) * stretch).ToList();

        var firstHeight = region.Height - heights.Sum();
        result.Places[first.Key] = first.FixedSize
            ? new TileRect(region.Left, region.Top, first.Width, first.Height)
            : new TileRect(region.Left, region.Top, region.Width, firstHeight);
        var top = region.Top + firstHeight;
        for (var index = 0; index < rows.Count; index++)
        {
            var left = region.Left;
            for (var column = 0; column < rows[index].Count; column++)
            {
                // The last in a row runs to the region's edge, so rounding leaves no gap. A window that sizes
                // itself (the equalizer) keeps its size.
                var window = rows[index][column];
                var width = window.FixedSize ? window.Width : column == rows[index].Count - 1 ? region.Right - left : widths[index][column];
                result.Places[window.Key] = new TileRect(left, top, width, window.FixedSize ? window.Height : heights[index]);
                left += width;
            }

            top += heights[index];
        }
    }

    /// <summary>
    /// Shares a row's width out: each window gets its smallest width, and the rest in proportion to how
    /// wide it is now. A window that sizes itself keeps its width.
    /// </summary>
    private static List<double> Widths(List<TileWindow> row, double width)
    {
        var spare = Math.Max(0, width - row.Sum(window => window.FixedSize ? window.Width : window.MinWidth));
        var total = row.Where(window => !window.FixedSize).Sum(window => Math.Max(window.Width, 1));
        return row.Select(window => window.FixedSize ? window.Width : window.MinWidth + spare * Math.Max(window.Width, 1) / total).ToList();
    }

    /// <summary>
    /// How tall a window in a row would like to be at that width: its height now, or for the artwork, its
    /// shape now (so the picture stays square).
    /// </summary>
    private static double Wanted(TileWindow window, double width) =>
        window.Kind == TileKind.Artwork && window.Width > 0 ? width * window.Height / window.Width : window.Height;

    /// <summary>The biggest part of <paramref name="area"/> to one side of a locked window, or all of it if the window's elsewhere.</summary>
    private static TileRect Beside(TileRect area, TileRect locked)
    {
        if (!locked.Overlaps(area))
        {
            return area;
        }

        var choices = new[]
        {
            new TileRect(area.Left, area.Top, locked.Left - area.Left, area.Height),
            new TileRect(locked.Right, area.Top, area.Right - locked.Right, area.Height),
            new TileRect(area.Left, area.Top, area.Width, locked.Top - area.Top),
            new TileRect(area.Left, locked.Bottom, area.Width, area.Bottom - locked.Bottom),
        };
        var best = choices.MaxBy(choice => choice.Width > 0 && choice.Height > 0 ? choice.Area : -1);
        return best.Width > 0 && best.Height > 0 ? best : area;
    }
}
