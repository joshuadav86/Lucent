using System.Collections;
using Lucent.Settings;

namespace Lucent.Library;

/// <summary>The orders the library can be listed in.</summary>
public enum LibrarySort
{
    Newest,
    Oldest,
    Title,
    Artist,
    FileName,
    MostPlayed,
    RecentlyPlayed,
    Rating,
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
        LibrarySort.MostPlayed => "Most played",
        LibrarySort.RecentlyPlayed => "Recently played",
        LibrarySort.Rating => "Rating",
        _ => "File name",
    };
}

/// <summary>Compares library items for the chosen order. Ties fall back to the file path, so the order is stable.</summary>
public sealed class LibraryItemComparer : IComparer
{
    private readonly LibrarySort _sort;
    private readonly LibraryColumn? _column;
    private readonly bool _descending;

    public LibraryItemComparer(LibrarySort sort) => _sort = sort;

    /// <summary>Sorts by a tag column. Songs that tie are listed as on their albums: artist, album, disc, track.</summary>
    public LibraryItemComparer(LibraryColumn column, bool descending)
    {
        _column = column;
        _descending = descending;
    }

    /// <summary>The order the settings ask for: a tag column's when one is chosen, otherwise the sort menu's.</summary>
    public static LibraryItemComparer For(AppSettings settings) => settings.LibrarySortColumn is { } column
        ? new LibraryItemComparer(column, settings.LibrarySortDescending)
        : new LibraryItemComparer(settings.LibrarySort);

    /// <summary>The sort button's label for the settings' order.</summary>
    public static string LabelFor(AppSettings settings) => settings.LibrarySortColumn is { } column
        ? LibraryColumns.SortLabel(column, settings.LibrarySortDescending)
        : LibrarySorts.Label(settings.LibrarySort);

    /// <summary>Whether the order depends on tags, so it has to be sorted again once they've been read.</summary>
    public static bool NeedsTags(AppSettings settings) => settings.LibrarySortColumn is { } column
        ? column is not (LibraryColumn.Added or LibraryColumn.FileName or LibraryColumn.Rating or LibraryColumn.Plays or LibraryColumn.LastPlayed)
        : settings.LibrarySort is LibrarySort.Title or LibrarySort.Artist;

    /// <summary>Whether the order depends on plays or ratings, so it has to be sorted again when they change.</summary>
    public static bool NeedsStats(AppSettings settings) => settings.LibrarySortColumn is { } column
        ? column is LibraryColumn.Rating or LibraryColumn.Plays or LibraryColumn.LastPlayed
        : settings.LibrarySort is LibrarySort.MostPlayed or LibrarySort.RecentlyPlayed or LibrarySort.Rating;

    public int Compare(object? x, object? y)
    {
        var a = (LibraryItem)x!;
        var b = (LibraryItem)y!;
        if (_column is { } column)
        {
            var byColumn = LibraryColumns.Compare(column, a, b, _descending);
            if (byColumn == 0)
            {
                byColumn = AlbumOrder(a, b);
            }

            return byColumn != 0 ? byColumn : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
        }

        var result = _sort switch
        {
            LibrarySort.Newest => b.Added.CompareTo(a.Added),
            LibrarySort.Oldest => a.Added.CompareTo(b.Added),
            LibrarySort.Title => CompareText(a.Title, b.Title),
            LibrarySort.Artist => CompareArtists(a, b),
            LibrarySort.MostPlayed => Then(b.Plays.CompareTo(a.Plays), a, b),
            LibrarySort.RecentlyPlayed => Then((b.LastPlayed ?? DateTime.MinValue).CompareTo(a.LastPlayed ?? DateTime.MinValue), a, b),
            LibrarySort.Rating => Then(b.Rating.CompareTo(a.Rating), a, b),
            _ => CompareText(a.FileName, b.FileName),
        };

        return result != 0 ? result : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Songs that tie on plays or rating are listed by artist, then title.</summary>
    private static int Then(int result, LibraryItem a, LibraryItem b) => result != 0 ? result : CompareArtists(a, b);

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

    private static int AlbumOrder(LibraryItem a, LibraryItem b)
    {
        foreach (var column in AlbumOrderColumns)
        {
            var result = LibraryColumns.Compare(column, a, b, descending: false);
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    private static readonly LibraryColumn[] AlbumOrderColumns =
    {
        LibraryColumn.Artist, LibraryColumn.Album, LibraryColumn.Disc, LibraryColumn.Track, LibraryColumn.Title,
    };

    private static int CompareText(string a, string b) =>
        string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase);
}
