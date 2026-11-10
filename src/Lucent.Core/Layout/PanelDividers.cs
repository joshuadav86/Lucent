namespace Lucent.Layout;

/// <summary>
/// A divider moved by hand in the single window: the line between the panels before it (to its left, or
/// above it) and the ones after it, moved <see cref="Offset"/> from where the layout puts it. Kept in the
/// settings, so it stays moved as the window's resized and when Lucent opens again.
/// </summary>
public sealed class PanelDivider
{
    /// <summary>True for a line across the window (panels above and below it), false for one down it.</summary>
    public bool Across { get; set; }

    /// <summary>The panels to its left (or above it), by name, in order.</summary>
    public List<string> Before { get; set; } = new();

    /// <summary>The panels to its right (or below it), by name, in order.</summary>
    public List<string> After { get; set; } = new();

    /// <summary>The panels between it and the next line that move along with it at their own size: the player's column.</summary>
    public List<string> Carried { get; set; } = new();

    /// <summary>How far it's moved from where the layout puts it: right (or down) is more.</summary>
    public double Offset { get; set; }
}

/// <summary>
/// A line between panels in a layout that can be dragged: where it is (<see cref="Position"/> across the
/// window for a line down it, or down the window for a line across it), how far it runs, and the panels
/// either side. Panels that size themselves (the player, the equalizer) can't be made bigger or smaller,
/// but a column of them with panels either side (the player's column, between the library and the visuals)
/// is <see cref="Carried"/> along: then the line is the column's two edges, <see cref="CarriedWidth"/> apart,
/// and dragging either moves the column, the panels before it growing and the ones after it shrinking.
/// The one exception is a panel that folds (<see cref="TileWindow.FoldsTo"/>, the equalizer): the line under it
/// can be dragged up, folding it shorter so the panels under it grow, and back down to where the layout put it.
/// </summary>
public sealed record DividerLine(bool Across, double Position, double Start, double End, IReadOnlyList<string> Before,
    IReadOnlyList<string> After, IReadOnlyList<string>? Carried = null, double CarriedWidth = 0)
{
    /// <summary>The panels that move along with it, if any.</summary>
    public IReadOnlyList<string> Carries => Carried ?? Array.Empty<string>();

    /// <summary>True when <paramref name="divider"/> was moved from this line.</summary>
    public bool Is(PanelDivider divider) =>
        divider.Across == Across && divider.Before.SequenceEqual(Before) && divider.After.SequenceEqual(After)
        && divider.Carried.SequenceEqual(Carries);

    /// <summary>True when the two are the same line, wherever each is.</summary>
    public bool Is(DividerLine other) =>
        other.Across == Across && other.Before.SequenceEqual(Before) && other.After.SequenceEqual(After) && other.Carries.SequenceEqual(Carries);
}

/// <summary>
/// The dividers between the panels in the single window, which can be dragged to make the panels either side
/// bigger and smaller. The layout (<see cref="SingleWindowLayout"/>) places the panels first; then each
/// divider moved by hand is moved from there, as far as the panels either side allow at their smallest.
/// A divider's known by the panels either side of it, so it's found again however big the window is, and
/// one whose panels aren't side by side any more (a panel closed, or two swapped) is just left alone.
/// The line under the equalizer folds it (<see cref="TileWindow.FoldsTo"/>): with the equalizer's sliders
/// above the library and the art under that, it's the only way to make the library taller.
/// Shared by both apps; tools/LayoutCheck checks it.
/// </summary>
public static class PanelDividers
{
    /// <summary>How wide the strip is that grabs a divider, half on each side of the line.</summary>
    public const double Grip = 6;

    /// <summary>The most dividers kept, the latest moved; plenty for every line in any layout.</summary>
    private const int Most = 32;

    /// <summary>Within this, two edges are the same line.</summary>
    private const double Near = 1;

    /// <summary>Within this, one side of a line reaches as far along it as the other (the layouts round a point either way).</summary>
    private const double Covered = 2;

    /// <summary>The lines between the placed panels that can be dragged.</summary>
    public static List<DividerLine> Lines(IReadOnlyDictionary<string, TileRect> places, IReadOnlyList<TileWindow> panels)
    {
        var fixedSize = panels.Where(panel => panel.FixedSize).Select(panel => panel.Key).ToHashSet();
        var folding = panels.Where(Folds).Select(panel => panel.Key).ToHashSet();
        bool Moves(IEnumerable<string> keys) => !keys.Any(fixedSize.Contains);
        static List<string> Sorted(IEnumerable<string> keys) => keys.Order(StringComparer.Ordinal).ToList();
        var lines = new List<DividerLine>();
        foreach (var across in new[] { false, true })
        {
            // A line across is found as a line down the window turned on its side.
            var rects = places.ToDictionary(entry => entry.Key, entry => across ? Turned(entry.Value) : entry.Value);
            var found = new List<(double Position, List<string> Before, List<string> After, double Start, double End)>();
            foreach (var position in Edges(rects.Values.Select(rect => rect.Right)))
            {
                var before = rects.Where(entry => Math.Abs(entry.Value.Right - position) < Near).Select(entry => entry.Key).ToList();
                var after = rects.Where(entry => Math.Abs(entry.Value.Left - position) < Near).Select(entry => entry.Key).ToList();
                foreach (var (left, right) in Joined(before, after, rects))
                {
                    if (left.Count > 0 && right.Count > 0)
                    {
                        var all = left.Concat(right).Select(key => rects[key]).ToList();
                        found.Add((position, Sorted(left), Sorted(right), all.Min(rect => rect.Top), all.Max(rect => rect.Bottom)));
                    }
                }
            }

            foreach (var (position, before, after, start, end) in found)
            {
                if (Moves(before) && Moves(after))
                {
                    lines.Add(new DividerLine(across, position, start, end, before, after));
                }
                else if (Moves(before) && Column(after, rects) is { } width
                    && found.FirstOrDefault(next => Math.Abs(next.Position - position - width) < Near && next.Before.SequenceEqual(after)) is { After: { } beyond } next
                    && Moves(beyond))
                {
                    // The column after it (the player's) is carried along to the line past it.
                    lines.Add(new DividerLine(across, position, Math.Min(start, next.Start), Math.Max(end, next.End), before, beyond, after, width));
                }
                else if (across && Moves(after) && before.All(key => !fixedSize.Contains(key) || folding.Contains(key))
                    && !before.Any(key => found.Any(other => other.After.Contains(key) && Moves(other.Before))))
                {
                    // Under a panel that folds (the equalizer, under the player): up folds it, the panels under it growing.
                    // Not when there's a panel over it to carry it down instead (the equalizer under the visuals on a short screen).
                    lines.Add(new DividerLine(across, position, start, end, before, after));
                }
            }
        }

        return lines;
    }

    /// <summary>True for a panel that sizes itself but can be folded shorter by the line under it (the equalizer).</summary>
    private static bool Folds(TileWindow panel) => panel.FixedSize && panel.FoldsTo > 0 && panel.FoldsTo < panel.Height - Near;

    /// <summary>The width of a column of panels lined up one over another, all as wide, or null if they aren't.</summary>
    private static double? Column(List<string> keys, Dictionary<string, TileRect> rects)
    {
        var first = rects[keys[0]];
        return keys.All(key => Math.Abs(rects[key].Left - first.Left) < Near && Math.Abs(rects[key].Right - first.Right) < Near)
            ? first.Width
            : null;
    }

    /// <summary>
    /// How far the line can move back (a negative number, or 0) and on (0 or more), keeping every panel either side
    /// at least its smallest, and stopping at any other panel in the way. A panel that folds (the equalizer) goes no
    /// shorter than it folds to, and no taller than its own height.
    /// </summary>
    public static (double Least, double Most) Room(DividerLine line, IReadOnlyDictionary<string, TileRect> places, IReadOnlyList<TileWindow> panels)
    {
        double Spare(string key)
        {
            var place = places[key];
            var panel = panels.FirstOrDefault(entry => entry.Key == key);
            var size = line.Across ? place.Height : place.Width;
            var smallest = panel is null ? 0
                : panel.FixedSize ? line.Across && Folds(panel) ? panel.FoldsTo : size
                : line.Across ? panel.MinHeight : panel.MinWidth;
            return Math.Max(0, size - smallest);
        }

        var least = -line.Before.Min(Spare);
        var most = line.After.Min(Spare);

        // A panel that sizes itself (folded) grows back no taller than its own height.
        foreach (var key in line.Before)
        {
            if (panels.FirstOrDefault(entry => entry.Key == key) is { FixedSize: true } panel)
            {
                most = Math.Min(most, Math.Max(0, line.Across ? panel.Height - places[key].Height : panel.Width - places[key].Width));
            }
        }

        // Only as far as the side growing covers all of the side shrinking, or it would leave a gap where that one was: once
        // another line's moved, the panels either side can stop at different places along it (the equalizer folded up over
        // the visuals after the line beside the visuals moved).
        if (line.Carries.Count == 0)
        {
            (double From, double To) Span(IEnumerable<string> keys)
            {
                var rects = keys.Select(key => line.Across ? Turned(places[key]) : places[key]).ToList();
                return (rects.Min(rect => rect.Top), rects.Max(rect => rect.Bottom));
            }

            static bool Covers((double From, double To) outer, (double From, double To) inner) =>
                outer.From <= inner.From + Covered && outer.To >= inner.To - Covered;
            var (before, after) = (Span(line.Before), Span(line.After));
            if (!Covers(after, before))
            {
                least = 0;
            }

            if (!Covers(before, after))
            {
                most = 0;
            }
        }

        // Nor over a panel that doesn't reach the line (one that sizes itself can leave a gap beside it).
        foreach (var (key, place) in places)
        {
            if (line.Before.Contains(key) || line.After.Contains(key) || line.Carries.Contains(key))
            {
                continue;
            }

            var rect = line.Across ? Turned(place) : place;
            if (Math.Min(rect.Bottom, line.End) - Math.Max(rect.Top, line.Start) <= Near)
            {
                continue;
            }

            if (rect.Right <= line.Position + Near)
            {
                least = Math.Max(least, Math.Min(0, rect.Right - line.Position));
            }
            else if (rect.Left >= line.Position + line.CarriedWidth - Near)
            {
                most = Math.Min(most, Math.Max(0, rect.Left - line.Position - line.CarriedWidth));
            }
        }

        return (least, most);
    }

    /// <summary>Moves the line by <paramref name="offset"/>: the panels before it grow by that much, the ones after it shrink, and any carried along move.</summary>
    public static void Move(IDictionary<string, TileRect> places, DividerLine line, double offset)
    {
        foreach (var key in line.Carries)
        {
            var place = places[key];
            places[key] = line.Across ? place with { Top = place.Top + offset } : place with { Left = place.Left + offset };
        }

        foreach (var key in line.Before)
        {
            var place = places[key];
            places[key] = line.Across ? place with { Height = place.Height + offset } : place with { Width = place.Width + offset };
        }

        foreach (var key in line.After)
        {
            var place = places[key];
            places[key] = line.Across
                ? place with { Top = place.Top + offset, Height = place.Height - offset }
                : place with { Left = place.Left + offset, Width = place.Width - offset };
        }
    }

    /// <summary>
    /// The layout with the dividers moved by hand moved, in the order they were moved, each as far as it can
    /// go towards where it was put. The layout itself is left as it was.
    /// </summary>
    public static TileResult Apply(TileResult layout, IReadOnlyList<TileWindow> panels, IReadOnlyList<PanelDivider> dividers)
    {
        var result = new TileResult();
        foreach (var (key, place) in layout.Places)
        {
            result.Places[key] = place;
        }

        result.LeftOut.AddRange(layout.LeftOut);
        foreach (var divider in dividers)
        {
            if (Lines(result.Places, panels).FirstOrDefault(line => line.Is(divider)) is not { } line)
            {
                continue;
            }

            var (least, most) = Room(line, result.Places, panels);
            var offset = Math.Clamp(divider.Offset, least, most);
            if (Math.Abs(offset) > 0.01)
            {
                Move(result.Places, line, offset);
            }
        }

        return result;
    }

    /// <summary>
    /// The layout with the dividers moved by hand moved, as <see cref="Apply"/> does, but with every panel that folds
    /// (the equalizer) at its own height: where the windows go when single window ends, the equalizer a window that
    /// sizes itself again. The panels under it give that height back, so they still end where they did.
    /// </summary>
    public static TileResult Unfolded(TileResult layout, IReadOnlyList<TileWindow> panels, IReadOnlyList<PanelDivider> dividers) =>
        Apply(layout, panels.Select(panel => panel.FoldsTo > 0 ? panel with { FoldsTo = 0 } : panel).ToList(), dividers);

    /// <summary>Keeps a line moved by <paramref name="offset"/>, replacing what was kept for it, as the last moved.</summary>
    public static PanelDivider Remember(List<PanelDivider> dividers, DividerLine line, double offset)
    {
        dividers.RemoveAll(line.Is);
        var divider = new PanelDivider
        {
            Across = line.Across, Before = line.Before.ToList(), After = line.After.ToList(), Carried = line.Carries.ToList(), Offset = offset,
        };
        dividers.Add(divider);
        if (dividers.Count > Most)
        {
            dividers.RemoveRange(0, dividers.Count - Most);
        }

        return divider;
    }

    /// <summary>A copy of the dividers kept, to keep with a saved layout or put back from one.</summary>
    public static List<PanelDivider> Copy(IEnumerable<PanelDivider> dividers) =>
        dividers.Select(divider => new PanelDivider
        {
            Across = divider.Across, Before = divider.Before.ToList(), After = divider.After.ToList(), Carried = divider.Carried.ToList(),
            Offset = divider.Offset,
        }).ToList();

    /// <summary>The distinct positions among these edges, each once.</summary>
    private static List<double> Edges(IEnumerable<double> edges)
    {
        var distinct = new List<double>();
        foreach (var edge in edges.Order())
        {
            if (distinct.Count == 0 || edge - distinct[^1] >= Near)
            {
                distinct.Add(edge);
            }
        }

        return distinct;
    }

    /// <summary>
    /// The panels either side of one position, in groups that are each one line: a panel on one side and a
    /// panel on the other are on the same line when they're beside each other (they overlap down it), and
    /// so is anything beside either of those, and so on.
    /// </summary>
    private static List<(List<string> Before, List<string> After)> Joined(List<string> before, List<string> after, Dictionary<string, TileRect> rects)
    {
        var all = before.Concat(after).ToList();
        var group = all.ToDictionary(key => key, key => key);
        string Find(string key) => group[key] == key ? key : group[key] = Find(group[key]);
        foreach (var left in before)
        {
            foreach (var right in after)
            {
                var a = rects[left];
                var b = rects[right];
                if (Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > Near)
                {
                    group[Find(left)] = Find(right);
                }
            }
        }

        return all.GroupBy(Find)
            .Select(members => (members.Where(before.Contains).ToList(), members.Where(after.Contains).ToList()))
            .ToList();
    }

    private static TileRect Turned(TileRect rect) => new(rect.Top, rect.Left, rect.Height, rect.Width);
}
