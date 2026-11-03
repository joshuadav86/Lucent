namespace Lucent.Library;

/// <summary>
/// Checks how dragging the line between two column headings shares out the room (<see cref="ColumnWidths"/>): the
/// line follows the pointer, columns keep a least width, and the title keeps its room. Both apps run it in their self-tests.
/// </summary>
public static class ColumnWidthsSelfTest
{
    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Expect(string what, IEnumerable<double> actual, params double[] expected)
        {
            var got = actual.ToList();
            if (!got.SequenceEqual(expected))
            {
                log($"Column widths: {what}: expected {string.Join(", ", expected)}, got {string.Join(", ", got)}");
                failures++;
            }
        }

        // Genre and Year beside a title, with room for 600 between them.
        var start = new double[] { 102, 60 };
        Expect("title's line dragged left widens the first column", ColumnWidths.Drag(start, 0, -30, 600), 132, 60);
        Expect("title's line dragged right narrows it, no further than the least", ColumnWidths.Drag(start, 0, 200, 600), ColumnWidths.Min, 60);
        Expect("a line between columns moves room from one to the other", ColumnWidths.Drag(start, 1, 10.4, 600), 112, 50);
        Expect("the column right of the line keeps its least width", ColumnWidths.Drag(start, 1, 40, 600), 114, ColumnWidths.Min);
        Expect("the last line widens the last column into the title", ColumnWidths.Drag(start, 2, 50, 600), 102, 110);
        Expect("the title keeps its room", ColumnWidths.Drag(start, 2, 500, 400), 102, 400 - ColumnWidths.TitleRoom - 102);
        Expect("a line that isn't there changes nothing", ColumnWidths.Drag(start, 5, 50, 600), 102, 60);
        Expect("fit to the widest text and its padding", new[] { ColumnWidths.Fit(start, 0, 70.2, 600) }, 88);
        Expect("fit no narrower than the least", new[] { ColumnWidths.Fit(start, 1, 3, 600) }, ColumnWidths.Min);

        var before = ColumnWidths.Custom;
        try
        {
            ColumnWidths.Custom = new Dictionary<string, double> { ["Genre"] = 150, ["Year"] = 9000 };
            Expect("dragged widths, within the limits", new[] { LibraryColumns.Width(LibraryColumn.Genre), LibraryColumns.Width(LibraryColumn.Year),
                LibraryColumns.Width(LibraryColumn.Key), PlaylistColumns.Width(PlaylistColumn.Genre) }, 150, ColumnWidths.Max, 56, 150);
        }
        finally
        {
            ColumnWidths.Custom = before;
        }

        log(failures == 0 ? "Column widths: OK" : $"Column widths: {failures} problem(s)");
        return failures;
    }
}
