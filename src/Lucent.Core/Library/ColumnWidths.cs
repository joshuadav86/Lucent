namespace Lucent.Library;

/// <summary>
/// The widths people give columns by dragging the lines between their headings, and how a drag shares out the room.
/// The library's tag columns and an opened streaming playlist's share them by name, so Album is as wide in both. The
/// title takes whatever room is left, as in a streaming app's playlist.
/// </summary>
public static class ColumnWidths
{
    /// <summary>The narrowest a column can be dragged: room for a short heading.</summary>
    public const double Min = 48;

    /// <summary>The widest a column can be dragged or fitted.</summary>
    public const double Max = 480;

    /// <summary>The room the title keeps when a column is dragged or fitted wider.</summary>
    public const double TitleRoom = 140;

    // A cell's space either side of its text (8 each side), and its divider line.
    private const double CellPadding = 17;

    /// <summary>
    /// The widths set by dragging, by column name (the settings' <see cref="Settings.AppSettings.ColumnWidths"/>).
    /// The library window sets it; columns not in it keep their standard widths.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Custom { get; set; } = new Dictionary<string, double>();

    /// <summary>The column's width: the one it was dragged to, or <paramref name="standard"/>.</summary>
    public static double Of(string name, double standard) =>
        Custom.TryGetValue(name, out var width) && double.IsFinite(width) ? Math.Clamp(width, Min, Max) : standard;

    /// <summary>
    /// The columns' widths while line <paramref name="line"/> is dragged <paramref name="delta"/> to the right of where it was
    /// pressed. <paramref name="start"/> holds the widths then. Line 0 is between the title and the first column, line i between
    /// columns i - 1 and i, and the last (start.Count) before the length. Dragging right widens what's left of the line; what's
    /// right of it gives up the room: the next column, or for the last line, the title (the length's column stays as it is).
    /// <paramref name="room"/> is the space for the title and the columns together.
    /// </summary>
    public static double[] Drag(IReadOnlyList<double> start, int line, double delta, double room)
    {
        var widths = start.ToArray();
        var count = widths.Length;
        if (count == 0 || line < 0 || line > count || !double.IsFinite(delta))
        {
            return widths;
        }

        if (line == 0)
        {
            // The title widens as the first column narrows.
            widths[0] = Math.Round(Math.Clamp(start[0] - delta, Math.Min(Min, start[0]), Widest(start, 0, room)));
        }
        else if (line == count)
        {
            widths[count - 1] = Math.Round(Math.Clamp(start[count - 1] + delta, Math.Min(Min, start[count - 1]), Widest(start, count - 1, room)));
        }
        else
        {
            // Two columns side by side share the room between them, so nothing else moves.
            var (left, right) = (start[line - 1], start[line]);
            var lowest = Math.Max(Min - left, right - Max);
            var highest = Math.Min(right - Min, Max - left);
            var shift = lowest <= highest ? Math.Round(Math.Clamp(delta, lowest, highest)) : 0;
            widths[line - 1] = left + shift;
            widths[line] = right - shift;
        }

        return widths;
    }

    /// <summary>The column a double-click on line <paramref name="line"/> fits: the one left of it, or for line 0, the first.</summary>
    public static int ColumnOf(int line) => Math.Max(0, line - 1);

    /// <summary>
    /// The width that fits column <paramref name="index"/> to its widest text (<paramref name="widestText"/>, without the cell's
    /// padding), within the room the title keeps.
    /// </summary>
    public static double Fit(IReadOnlyList<double> widths, int index, double widestText, double room) =>
        Math.Clamp(Math.Ceiling(widestText + CellPadding), Min, Math.Max(Min, Widest(widths, index, room)));

    /// <summary>How wide column <paramref name="index"/> can be with the title keeping its room; never less than it is now.</summary>
    private static double Widest(IReadOnlyList<double> widths, int index, double room)
    {
        var others = widths.Sum() - widths[index];
        return Math.Max(widths[index], Math.Min(Max, room - TitleRoom - others));
    }
}
