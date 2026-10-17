using System.Collections;

namespace WinampInspiredVisualizer.Library;

/// <summary>The orders the library can be listed in.</summary>
public enum LibrarySort
{
    Newest,
    Oldest,
    Title,
    Artist,
    FileName,
}

public static class LibrarySorts
{
    public static IReadOnlyList<LibrarySort> All { get; } = Enum.GetValues<LibrarySort>();

    public static string Label(LibrarySort sort) => sort switch
    {
        LibrarySort.Newest => "Newest first",
        LibrarySort.Oldest => "Oldest first",
        LibrarySort.Title => "Title",
        LibrarySort.Artist => "Artist",
        _ => "File name",
    };
}

/// <summary>Compares library items for the chosen order. Ties fall back to the file path, so the order is stable.</summary>
public sealed class LibraryItemComparer : IComparer
{
    private readonly LibrarySort _sort;

    public LibraryItemComparer(LibrarySort sort) => _sort = sort;

    public int Compare(object? x, object? y)
    {
        var a = (LibraryItem)x!;
        var b = (LibraryItem)y!;
        var result = _sort switch
        {
            LibrarySort.Newest => b.Added.CompareTo(a.Added),
            LibrarySort.Oldest => a.Added.CompareTo(b.Added),
            LibrarySort.Title => CompareText(a.Title, b.Title),
            LibrarySort.Artist => CompareArtists(a, b),
            _ => CompareText(a.FileName, b.FileName),
        };

        return result != 0 ? result : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareArtists(LibraryItem a, LibraryItem b)
    {
        // Tracks without an artist go to the end rather than the top.
        var missing = string.IsNullOrEmpty(a.Artist).CompareTo(string.IsNullOrEmpty(b.Artist));
        if (missing != 0)
        {
            return missing;
        }

        var artist = CompareText(a.Artist, b.Artist);
        return artist != 0 ? artist : CompareText(a.Title, b.Title);
    }

    private static int CompareText(string a, string b) =>
        string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
}
