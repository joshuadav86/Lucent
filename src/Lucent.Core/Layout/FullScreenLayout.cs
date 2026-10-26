namespace Lucent.Layout;

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

/// <summary>
/// How <see cref="FullScreenLayout"/> arranges the windows. Tidy layout (Shift+D, or "Tidy layout" on a
/// right-click) goes through the tidy ones in turn; the layout chosen stays for laying them out again.
/// </summary>
public enum TileStyle
{
    /// <summary>The player's column on the left, the library down the far left, and the windows in a row sharing it as they're sized now.</summary>
    AsTheyAre,

    /// <summary>The visuals in the middle, the player's column on the left and the library column as wide on the right.</summary>
    Centred,

    /// <summary><see cref="Centred"/> the other way round: the player's column on the right.</summary>
    CentredMirrored,

    /// <summary>Like <see cref="AsTheyAre"/>, with the windows in a row sharing it evenly.</summary>
    Classic,

    /// <summary><see cref="Classic"/> the other way round: the player's column on the right, the library on the far right.</summary>
    Mirrored,
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
/// With the queue closed, the library (and the artwork) take its place under the equalizer when they fit,
/// so the column's filled to the bottom and the big windows get the library's column; otherwise the
/// artwork, the analyzer or the video does. On a small screen (or a large scaling) whatever doesn't fit beside the player moves into the space
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

    /// <summary>The tidy layouts, in the order Tidy layout goes through them.</summary>
    public static readonly IReadOnlyList<TileStyle> TidyStyles = new[] { TileStyle.Centred, TileStyle.CentredMirrored, TileStyle.Classic, TileStyle.Mirrored };

    /// <summary>What the user sees a layout called.</summary>
    public static string NameOf(TileStyle style) => style switch
    {
        TileStyle.Centred => "Visuals in the middle",
        TileStyle.CentredMirrored => "Visuals in the middle, player on the right",
        TileStyle.Classic => "Player on the left",
        TileStyle.Mirrored => "Player on the right",
        _ => "As they are",
    };

    /// <summary>
    /// Tidy layout: the next tidy layout after <paramref name="current"/> that would move something from
    /// where the windows are <paramref name="now"/>, so each press changes the layout, and where it puts them.
    /// One that leaves a window out that another fits is passed over.
    /// </summary>
    public static (TileStyle Style, TileResult Result) Tidy(TileRect area, IReadOnlyList<TileWindow> windows,
        IReadOnlyDictionary<string, TileRect> now, TileStyle current)
    {
        var start = TidyStyles.ToList().IndexOf(current) + 1;
        var tried = Enumerable.Range(0, TidyStyles.Count)
            .Select(step => TidyStyles[(start + step) % TidyStyles.Count])
            .Select(style => (Style: style, Result: Arrange(area, windows, style)))
            .ToList();
        var fewestOut = tried.Min(entry => entry.Result.LeftOut.Count);
        var fitting = tried.Where(entry => entry.Result.LeftOut.Count == fewestOut).ToList();
        var moving = fitting.FindIndex(entry => Moves(entry.Result, now));
        return fitting[Math.Max(0, moving)];
    }

    /// <summary>
    /// Tidy layout when Lucent isn't full screen: like <see cref="Tidy"/>, but each layout is a compact block
    /// around the player (<see cref="ArrangeAround"/>) rather than filling an area.
    /// </summary>
    public static (TileStyle Style, TileResult Result) TidyAround(TileRect screen, IReadOnlyList<TileWindow> windows,
        IReadOnlyDictionary<string, TileRect> now, TileStyle current)
    {
        var start = TidyStyles.ToList().IndexOf(current) + 1;
        var tried = Enumerable.Range(0, TidyStyles.Count)
            .Select(step => TidyStyles[(start + step) % TidyStyles.Count])
            .Select(style => (Style: style, Result: ArrangeAround(screen, windows, now, style)))
            .ToList();
        var fewestOut = tried.Min(entry => entry.Result.LeftOut.Count);
        var fitting = tried.Where(entry => entry.Result.LeftOut.Count == fewestOut).ToList();
        var moving = fitting.FindIndex(entry => Moves(entry.Result, now));
        return fitting[Math.Max(0, moving)];
    }

    /// <summary>
    /// Lays the windows out in a compact block around the player, which stays where it is. The block's size
    /// comes from the player's alone, never from how big the other windows are or where they happen to be,
    /// so Tidy layout always gives the same block, and a window left far away (or made huge) doesn't blow it
    /// up: the player's column as tall as the player, the equalizer and a queue a little shorter than the
    /// player is wide; the library three quarters of the player's width; the visuals a little wider than
    /// the block is tall, but no wider than two players. The block is kept on the screen and clear of
    /// locked windows (they stay where they are). If it can't fit everything it grows a little at a time,
    /// and only as a last resort does it fill the screen. If even that would go over a locked window,
    /// nothing moves (every window is left out).
    /// </summary>
    public static TileResult ArrangeAround(TileRect screen, IReadOnlyList<TileWindow> windows, IReadOnlyDictionary<string, TileRect> now, TileStyle style)
    {
        var player = windows.FirstOrDefault(window => window.Kind == TileKind.Player);
        if (player is null || !now.TryGetValue(player.Key, out var anchor))
        {
            return Arrange(screen, windows, style);
        }

        // A locked player stays where it is anyway, which is where the block's put around it: lay it out as if it
        // were free, so it can be centred or turned round about the player too.
        var lockedPlayer = player.LockedAt is not null;
        var free = windows.Where(window => window.LockedAt is null || window == player)
            .Select(window => window == player ? window with { LockedAt = null } : window)
            .ToList();
        var locked = windows.Where(window => window.LockedAt is not null && window != player).Select(window => window.LockedAt!.Value).ToList();
        var (width, height, sized) = CompactSize(screen, free);
        // Taller first (windows stacked under the visuals need height more often than width), then wider too.
        foreach (var (wider, taller) in new[] { (1.0, 1.0), (1.0, 1.25), (1.0, 1.5), (1.0, 2.0), (1.25, 2.0), (1.5, 2.0) })
        {
            var area = new TileRect(screen.Left, screen.Top, Math.Min(screen.Width, width * wider), Math.Min(screen.Height, height * taller));
            var result = Arrange(area, sized, style);
            if (result.LeftOut.Count > 0 || !result.Places.TryGetValue(player.Key, out var placed)
                || Shifted(result, anchor.Left - placed.Left, anchor.Top - placed.Top, screen, locked, keepPlayer: lockedPlayer) is not { } shifted)
            {
                continue;
            }

            if (lockedPlayer)
            {
                shifted.Places.Remove(player.Key);
            }

            return shifted;
        }

        var full = Arrange(screen, windows, style);
        if (full.Places.All(entry => (entry.Value.IsInside(screen) || entry.Key == player.Key && (player.Width > screen.Width || player.Height > screen.Height))
            && !locked.Any(at => at.Overlaps(entry.Value))))
        {
            return full;
        }

        var none = new TileResult();
        none.LeftOut.AddRange(windows.Where(window => window.LockedAt is null).Select(window => window.Key));
        return none;
    }

    /// <summary>
    /// The size of the compact block for <see cref="ArrangeAround"/>, and the windows with the sizes it'd like
    /// them to be (Arrange shares space out by the sizes windows would like).
    /// </summary>
    private static (double Width, double Height, List<TileWindow> Windows) CompactSize(TileRect screen, List<TileWindow> windows)
    {
        var player = windows.First(window => window.Kind == TileKind.Player);
        var equalizer = windows.FirstOrDefault(window => window.Kind == TileKind.Equalizer);
        var queue = windows.FirstOrDefault(window => window.Kind == TileKind.Queue);
        var side = windows.Where(window => window.Kind is TileKind.Library or TileKind.Artwork).ToList();
        var main = windows.Where(window => window.Kind is TileKind.Visuals or TileKind.Video or TileKind.Analyzer or TileKind.Radio or TileKind.Editor)
            .OrderBy(window => window.Kind).ToList();
        var column = Math.Max(player.Width, equalizer?.Width ?? 0);

        var height = player.Height + (equalizer?.Height ?? 0) + (queue is null ? 0 : Math.Max(queue.MinHeight, column * 0.8));
        if (side.Count > 0 || main.Count > 0)
        {
            // Not so short the windows beside the column are letterboxes.
            height = Math.Max(height, column * 0.9);
        }

        height = Math.Min(screen.Height, height);
        var mainWidth = main.Count == 0 ? 0 : Math.Clamp(height * 1.2, NeededWidth(main), Math.Max(NeededWidth(main), column * 2));
        var sideWidth = 0.0;
        if (side.Count > 0)
        {
            // Arrange gives the side column no more than its share of the width, so ask for no more than that.
            var least = NeededWidth(side);
            sideWidth = Math.Max(least, Math.Min(column * 0.75, SideShare * (column + mainWidth) / (1 - SideShare)));
        }

        var sized = windows.Select(window => window.Kind switch
        {
            TileKind.Library or TileKind.Artwork => window with { Width = sideWidth, Height = window.Kind == TileKind.Artwork ? sideWidth : window.Height },
            _ when main.Count > 0 && window == main[0] => window with { Width = mainWidth },
            TileKind.Queue => window with { Height = Math.Max(window.MinHeight, column * 0.8) },
            _ => window,
        }).ToList();
        return (column + sideWidth + mainWidth, height, sized);
    }

    /// <summary>
    /// The layout moved so the player lands where it is now, then nudged back onto the screen and to one side of
    /// any locked window it would cover. Null if it can't be: it's still over a locked window, or (with the
    /// player locked, so it can't move) it would go off the screen.
    /// </summary>
    private static TileResult? Shifted(TileResult result, double dx, double dy, TileRect screen, List<TileRect> locked, bool keepPlayer)
    {
        var places = result.Places.Values.ToList();
        var bounds = new TileRect(places.Min(place => place.Left) + dx, places.Min(place => place.Top) + dy, 0, 0);
        bounds = bounds with { Width = places.Max(place => place.Right) + dx - bounds.Left, Height = places.Max(place => place.Bottom) + dy - bounds.Top };
        var onScreen = OnScreen(bounds, screen);
        if (keepPlayer && onScreen != bounds)
        {
            return null;
        }

        // Clear of locked windows: as it is if it's clear, otherwise the smallest step to beside one, on the screen.
        bool Clear(TileRect block) => !locked.Any(at => at.Overlaps(block));
        var candidates = new List<TileRect> { onScreen };
        if (!keepPlayer)
        {
            foreach (var at in locked)
            {
                candidates.Add(OnScreen(onScreen with { Left = at.Right }, screen));
                candidates.Add(OnScreen(onScreen with { Left = at.Left - onScreen.Width }, screen));
                candidates.Add(OnScreen(onScreen with { Top = at.Bottom }, screen));
                candidates.Add(OnScreen(onScreen with { Top = at.Top - onScreen.Height }, screen));
            }
        }

        var clear = candidates.Where(Clear).OrderBy(block => Math.Abs(block.Left - bounds.Left) + Math.Abs(block.Top - bounds.Top)).ToList();
        if (clear.Count == 0)
        {
            return null;
        }

        var moveX = clear[0].Left - places.Min(place => place.Left);
        var moveY = clear[0].Top - places.Min(place => place.Top);
        var shifted = new TileResult();
        foreach (var (key, place) in result.Places)
        {
            shifted.Places[key] = place with { Left = place.Left + moveX, Top = place.Top + moveY };
        }

        return shifted;
    }

    /// <summary>The rectangle moved as little as it takes to be on the screen (its top left corner, if it's bigger).</summary>
    private static TileRect OnScreen(TileRect rect, TileRect screen) => rect with
    {
        Left = Math.Max(screen.Left, Math.Min(rect.Left, screen.Right - rect.Width)),
        Top = Math.Max(screen.Top, Math.Min(rect.Top, screen.Bottom - rect.Height)),
    };

    /// <summary>A short note, after Tidy layout, on the windows it left where they were because they're locked, or null if none were.</summary>
    public static string? LockedNote(IReadOnlyList<string> locked) => locked.Count switch
    {
        0 => null,
        1 => $"{locked[0]} is locked, so it stayed put",
        _ => $"{string.Join(", ", locked.Take(locked.Count - 1))}{(locked.Count > 2 ? "," : "")} and {locked[^1]} are locked, so they stayed put",
    };

    /// <summary>True when the layout puts some window somewhere other than where it is now.</summary>
    private static bool Moves(TileResult result, IReadOnlyDictionary<string, TileRect> now) =>
        result.Places.Any(entry => !now.TryGetValue(entry.Key, out var at)
            || Math.Abs(at.Left - entry.Value.Left) > 1 || Math.Abs(at.Top - entry.Value.Top) > 1
            || Math.Abs(at.Width - entry.Value.Width) > 1 || Math.Abs(at.Height - entry.Value.Height) > 1);

    /// <param name="area">The screen's usable part (without the taskbar, or the menu bar and Dock).</param>
    public static TileResult Arrange(TileRect area, IReadOnlyList<TileWindow> windows, TileStyle style = TileStyle.AsTheyAre)
    {
        // With the library column, or with it moved out of the way to make room; and with the space under the
        // player filled when the queue's closed, or not (on a tiny screen that can crowd a window out):
        // whichever fits the most in, in this order when it's even.
        TileResult? best = null;
        foreach (var (keepSide, fillUnder) in new[] { (true, true), (false, true), (true, false), (false, false) })
        {
            var tried = Arrange(area, windows, keepSide, fillUnder, style);
            if (best is null || tried.LeftOut.Count < best.LeftOut.Count)
            {
                best = tried;
            }

            if (best.LeftOut.Count == 0)
            {
                break;
            }
        }

        return best!;
    }

    private static TileResult Arrange(TileRect area, IReadOnlyList<TileWindow> windows, bool keepSide, bool fillUnder, TileStyle style)
    {
        var result = new TileResult();
        var even = style != TileStyle.AsTheyAre;

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

        // A locked player stays where it is, so the layout can't be centred or turned round about it.
        var centred = lockedLeft is null && style is TileStyle.Centred or TileStyle.CentredMirrored;
        var mirrored = lockedLeft is null && style is TileStyle.Mirrored or TileStyle.CentredMirrored;

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

        if (fillUnder && under.Count == 0 && main.Count > 0)
        {
            // Nothing under the player (the queue's closed): other windows fill the space there rather than
            // leaving it empty. The library (with the artwork under it, if it's open and there's room), which
            // frees its column down the side for the big windows; a small library mostly leaves that column
            // empty anyway. Otherwise one window: the artwork, then one from under the visuals. Not with
            // nothing in the big space, where the library and artwork fill the screen beside the player anyway.
            // Centred, the library stays on the far side to balance the player's column.
            var fillers = Borrow(side, main, columnWidth, underHeight, keepOneSide: lockedLeft is not null || centred);
            if (fillers.Count == 0 && centred && lockedLeft is null)
            {
                // Nothing else to fill it with: the library after all, and the layout's not centred.
                fillers = Borrow(side, main, columnWidth, underHeight, keepOneSide: false);
            }
            foreach (var filler in fillers)
            {
                side.Remove(filler);
                main.Remove(filler);
                under.Add(filler);
            }
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
                else if (centred && main.Count > 0 && NeededWidth(side) <= columnWidth + 0.5 && room >= columnWidth)
                {
                    // As wide as the player's column, so the visuals sit in the middle.
                    sideWidth = columnWidth;
                }
            }

            if (centred && sideWidth > 0 && main.Count > 0)
            {
                // The player's column on the left, the big windows in the middle, and the library on the right.
                columnLeft = area.Left;
                mainRegion = new TileRect(area.Left + columnWidth, area.Top, area.Width - columnWidth - sideWidth, area.Height);
                sideRegion = new TileRect(area.Right - sideWidth, area.Top, sideWidth, area.Height);
            }
            else
            {
                columnLeft = area.Left + sideWidth;
                sideRegion = new TileRect(area.Left, area.Top, sideWidth, area.Height);
                mainRegion = new TileRect(columnLeft + columnWidth, area.Top, area.Right - columnLeft - columnWidth, area.Height);
            }
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
        Pack(new TileRect(columnLeft, below, underWidth, underHeight), under, result, even);
        Pack(sideRegion, side, result, even);
        Pack(mainRegion, main, result, even);
        if (mirrored)
        {
            // Turned round left to right, within the part of the screen it used.
            foreach (var (key, place) in result.Places.ToList())
            {
                result.Places[key] = place with { Left = area.Left + area.Right - place.Right };
            }
        }

        return result;
    }

    /// <summary>The narrowest a region can be and still take every one of these (each on a row of its own).</summary>
    private static double NeededWidth(IReadOnlyList<TileWindow> windows) => windows.Count == 0 ? 0 : windows.Max(window => window.MinWidth);

    /// <summary>
    /// The windows to fill the space under the player when the queue isn't there, each fitting it at its
    /// smallest: the whole side column (the library, and the artwork under it) when it all fits, otherwise
    /// one window from the side column or from under the visuals (never the visuals themselves). None if none fits.
    /// </summary>
    /// <param name="keepOneSide">The player's locked, so the side column's space is there either way: leave it at least one window.</param>
    private static List<TileWindow> Borrow(List<TileWindow> side, List<TileWindow> main, double columnWidth, double underHeight, bool keepOneSide)
    {
        bool Fits(TileWindow window) => window.MinWidth <= columnWidth + 0.5 && window.MinHeight <= underHeight + 0.5 && (!keepOneSide || side.Count > 1 || !side.Contains(window));
        if (!keepOneSide && side.Count > 0 && side.All(window => window.MinWidth <= columnWidth + 0.5) && side.Sum(window => window.MinHeight) <= underHeight + 0.5)
        {
            return side.ToList();
        }

        var one = side.FirstOrDefault(window => window.Kind == TileKind.Artwork && Fits(window))
            ?? main.Skip(1).FirstOrDefault(window => window.Kind is TileKind.Analyzer or TileKind.Video && Fits(window))
            ?? side.FirstOrDefault(window => window.Kind == TileKind.Library && Fits(window));
        return one is null ? new() : new() { one };
    }

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
    /// <param name="even">Windows side by side share the space evenly, rather than as they're sized now.</param>
    private static void Pack(TileRect region, List<TileWindow> windows, TileResult result, bool even)
    {
        var rows = new TileResult();
        PackRows(region, windows, rows, even);
        var columns = new TileResult();
        PackRows(Turned(region), windows.Select(Turned).ToList(), columns, even);
        var useColumns = columns.LeftOut.Count < rows.LeftOut.Count
            || columns.LeftOut.Count == rows.LeftOut.Count && region.Width > region.Height * 1.5;
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
    private static void PackRows(TileRect region, List<TileWindow> windows, TileResult result, bool even)
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

        var widths = rows.Select(row => Widths(row, region.Width, even)).ToList();
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
    /// wide it is now. A window that sizes itself keeps its width. Evenly, each gets the same width where
    /// their smallest sizes allow.
    /// </summary>
    private static List<double> Widths(List<TileWindow> row, double width, bool even)
    {
        if (even)
        {
            // An equal share each, but no less than a window's smallest: the others share what's left.
            var sharing = row.Where(window => !window.FixedSize).ToList();
            var space = width - row.Where(window => window.FixedSize).Sum(window => window.Width);
            var shares = new Dictionary<TileWindow, double>();
            while (sharing.Count > 0)
            {
                var share = space / sharing.Count;
                var tooSmall = sharing.Where(window => window.MinWidth > share).ToList();
                if (tooSmall.Count == 0)
                {
                    sharing.ForEach(window => shares[window] = share);
                    break;
                }

                foreach (var window in tooSmall)
                {
                    shares[window] = window.MinWidth;
                    space -= window.MinWidth;
                    sharing.Remove(window);
                }
            }

            return row.Select(window => window.FixedSize ? window.Width : shares[window]).ToList();
        }

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
