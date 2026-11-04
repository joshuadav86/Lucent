using System.ComponentModel;

namespace Lucent.Library;

/// <summary>
/// The widths people give columns by dragging the lines between their headings, and how a drag shares out the room.
/// The library's tag columns and an opened streaming playlist's share them by name, so Album is as wide in both. The
/// title takes whatever room is left, and the length stays as it is at the end, as in a streaming app's playlist.
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

    // Each column's width that its cells follow, by name.
    private static readonly Dictionary<string, ColumnSize> Sizes = new();

    /// <summary>
    /// The widths set by dragging, by column name (the settings' <see cref="Settings.AppSettings.ColumnWidths"/>).
    /// The library window sets it; columns not in it keep their standard widths. After changing it, call
    /// <see cref="Refresh"/> so the rows follow.
    /// </summary>
    public static IReadOnlyDictionary<string, double> Custom { get; set; } = new Dictionary<string, double>();

    /// <summary>The column's width: the one it was dragged to, or <paramref name="standard"/>.</summary>
    public static double Of(string name, double standard) =>
        Custom.TryGetValue(name, out var width) && double.IsFinite(width) ? Math.Clamp(width, Min, Max) : standard;

    /// <summary>
    /// The column's width as one object that every row's cell in it binds to, so a column dragged wider widens all its
    /// cells at once, with nothing rebuilt. One per name; used on the UI thread.
    /// </summary>
    public static ColumnSize Size(string name, double standard)
    {
        if (!Sizes.TryGetValue(name, out var size))
        {
            Sizes[name] = size = new ColumnSize(standard, Of(name, standard));
        }

        return size;
    }

    /// <summary>Brings every column's <see cref="ColumnSize"/> in line with <see cref="Custom"/>, after it's changed.</summary>
    public static void Refresh()
    {
        foreach (var (name, size) in Sizes)
        {
            size.Width = Of(name, size.Standard);
        }
    }

    /// <summary>
    /// The columns' widths while line <paramref name="line"/> is dragged <paramref name="delta"/> to the right of where it was
    /// pressed, so the line stays under the pointer. <paramref name="start"/> holds the widths then. Line 0 is between the
    /// title and the first column, and line i between columns i - 1 and i (the line before the length isn't dragged, as the
    /// length stays as it is). What's next to the line on the side it moves away from grows, and what's on the side it moves
    /// into gives way: the nearest column first, then the next once that one is at its narrowest, and moving left, the title
    /// last, down to the room it keeps. <paramref name="room"/> is the space for the title and the columns together.
    /// </summary>
    public static double[] Drag(IReadOnlyList<double> start, int line, double delta, double room)
    {
        var widths = start.ToArray();
        var count = widths.Length;
        if (count == 0 || line < 0 || line >= count || !double.IsFinite(delta) || delta == 0)
        {
            return widths;
        }

        if (delta > 0)
        {
            // Right: the title or the column left of the line grows, and the columns right of it give way.
            var right = Enumerable.Range(line, count - line).ToList();
            var grows = line == 0 ? double.PositiveInfinity : Max - start[line - 1];
            var shift = Shift(delta, Math.Min(grows, Spare(start, right)));
            if (line > 0)
            {
                widths[line - 1] += shift;
            }

            Take(widths, right, shift);
        }
        else
        {
            // Left: the column right of the line grows, and the columns left of it give way, then the title.
            var left = Enumerable.Range(0, line).Reverse().ToList();
            var title = Math.Max(0, room - start.Sum() - TitleRoom);
            var shift = Shift(-delta, Math.Min(Max - start[line], Spare(start, left) + title));
            widths[line] += shift;
            Take(widths, left, shift);
        }

        return widths;
    }

    /// <summary>
    /// The columns a double-click on line <paramref name="line"/> fits to their text: the two either side of it, or for the
    /// title's line, the first (the title takes what's left).
    /// </summary>
    public static IReadOnlyList<int> FittedBy(int line) => line <= 0 ? new[] { 0 } : new[] { line - 1, line };

    /// <summary>
    /// The width that fits column <paramref name="index"/> to its widest text (<paramref name="widestText"/>, without the cell's
    /// padding), within the room the title keeps.
    /// </summary>
    public static double Fit(IReadOnlyList<double> widths, int index, double widestText, double room) =>
        Math.Clamp(Math.Ceiling(widestText + CellPadding), Min, Math.Max(Min, Widest(widths, index, room)));

    // How far the line moves: the pointer's distance in whole pixels, no further than the room there is.
    private static double Shift(double distance, double most) => Math.Max(0, Math.Min(Math.Round(distance), Math.Floor(most)));

    // How much the columns can give before each is at its narrowest.
    private static double Spare(IReadOnlyList<double> widths, IEnumerable<int> columns) =>
        columns.Sum(index => Math.Max(0, widths[index] - Min));

    // Takes room from the columns in turn, each down to its narrowest; any left over comes from the title.
    private static void Take(double[] widths, IEnumerable<int> columns, double amount)
    {
        foreach (var index in columns)
        {
            var taken = Math.Min(amount, Math.Max(0, widths[index] - Min));
            widths[index] -= taken;
            amount -= taken;
        }
    }

    /// <summary>How wide column <paramref name="index"/> can be with the title keeping its room; never less than it is now.</summary>
    private static double Widest(IReadOnlyList<double> widths, int index, double room)
    {
        var others = widths.Sum() - widths[index];
        return Math.Max(widths[index], Math.Min(Max, room - TitleRoom - others));
    }
}

/// <summary>
/// One column's width, shared by its heading's cells in every row (<see cref="LibraryCell"/>): when it changes, they all
/// follow at once, which keeps a dragged line under the pointer even in a long list.
/// </summary>
public sealed class ColumnSize : INotifyPropertyChanged
{
    private double _width;

    internal ColumnSize(double standard, double width)
    {
        Standard = standard;
        _width = width;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The column's width until it's dragged.</summary>
    public double Standard { get; }

    /// <summary>The column's width now, with its divider line and the space either side of the text.</summary>
    public double Width
    {
        get => _width;
        internal set
        {
            if (_width != value)
            {
                _width = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width)));
            }
        }
    }
}
