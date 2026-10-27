using System.Globalization;

namespace Lucent.Library;

/// <summary>
/// A tag the library can show in a column beside each song, and sort by. Title and Length are the row's own
/// title and time, so they're sorted by but never added as columns.
/// </summary>
public enum LibraryColumn
{
    Title,
    Artist,
    Album,
    AlbumArtist,
    Genre,
    Year,
    Track,
    Disc,
    Composer,
    Bpm,
    Key,
    Length,
    Bitrate,
    SampleRate,
    Format,
    Rating,
    Plays,
    LastPlayed,
    Added,
    FileName,
    Comment,
}

/// <summary>One song's value in one column, as the row shows it.</summary>
public sealed record LibraryCell(string Text, double Width, bool AlignRight);

/// <summary>What each column is called, how wide it is, what it shows for a song, and how it sorts.</summary>
public static class LibraryColumns
{
    /// <summary>Every column, in the order the sort menu lists them.</summary>
    public static IReadOnlyList<LibraryColumn> All { get; } = Enum.GetValues<LibraryColumn>();

    /// <summary>The columns that can be added beside the songs (all but the row's own title and time).</summary>
    public static IReadOnlyList<LibraryColumn> Choosable { get; } =
        All.Where(column => column is not (LibraryColumn.Title or LibraryColumn.Length)).ToList();

    /// <summary>
    /// The columns shown now, in order: the library window sets it from the settings, and every song's
    /// <see cref="LibraryItem.Cells"/> follows it.
    /// </summary>
    public static IReadOnlyList<LibraryColumn> Shown { get; set; } = Array.Empty<LibraryColumn>();

    /// <summary>The space after the title for the shown columns.</summary>
    public static double ShownWidth => Shown.Sum(Width);

    public static string Label(LibraryColumn column) => column switch
    {
        LibraryColumn.AlbumArtist => "Album artist",
        LibraryColumn.Track => "Track",
        LibraryColumn.Bpm => "BPM",
        LibraryColumn.SampleRate => "Sample rate",
        LibraryColumn.LastPlayed => "Last played",
        LibraryColumn.Added => "Date added",
        LibraryColumn.FileName => "File name",
        _ => column.ToString(),
    };

    /// <summary>The heading's text: short, to fit over a narrow column.</summary>
    public static string Heading(LibraryColumn column) => column switch
    {
        LibraryColumn.AlbumArtist => "Album artist",
        LibraryColumn.Track => "#",
        LibraryColumn.SampleRate => "Rate",
        LibraryColumn.LastPlayed => "Played",
        LibraryColumn.Added => "Added",
        LibraryColumn.FileName => "File",
        _ => Label(column),
    };

    /// <summary>The column's width in device-independent pixels (WPF's and Avalonia's units).</summary>
    public static double Width(LibraryColumn column) => column switch
    {
        LibraryColumn.Artist or LibraryColumn.AlbumArtist or LibraryColumn.Composer => 120,
        LibraryColumn.Album => 130,
        LibraryColumn.Genre => 96,
        LibraryColumn.Year or LibraryColumn.Bpm => 54,
        LibraryColumn.Track or LibraryColumn.Disc => 46,
        LibraryColumn.Key => 50,
        LibraryColumn.Bitrate => 74,
        LibraryColumn.SampleRate => 70,
        LibraryColumn.Format => 64,
        LibraryColumn.Rating => 70,
        LibraryColumn.Plays => 58,
        LibraryColumn.LastPlayed or LibraryColumn.Added => 80,
        _ => 150,
    };

    /// <summary>Numbers and dates line up on the right; names on the left.</summary>
    public static bool AlignRight(LibraryColumn column) => column is LibraryColumn.Year or LibraryColumn.Track or LibraryColumn.Disc
        or LibraryColumn.Bpm or LibraryColumn.Length or LibraryColumn.Bitrate or LibraryColumn.SampleRate or LibraryColumn.Plays
        or LibraryColumn.LastPlayed or LibraryColumn.Added;

    /// <summary>
    /// Whether a first click on the heading sorts high to low: the most played, best rated and newest are
    /// what people look for first. Everything else starts A to Z, or low to high.
    /// </summary>
    public static bool DescendingFirst(LibraryColumn column) =>
        column is LibraryColumn.Rating or LibraryColumn.Plays or LibraryColumn.LastPlayed or LibraryColumn.Added;

    /// <summary>"Genre ▲" for the sort button and the sorted column's heading.</summary>
    public static string SortLabel(LibraryColumn column, bool descending) => $"{Label(column)} {(descending ? "▼" : "▲")}";

    /// <summary>What the song's row shows in the column; empty when the file doesn't say.</summary>
    public static string Text(LibraryColumn column, LibraryItem item)
    {
        var tags = item.Track?.Tags ?? Playlist.TrackTagValues.None;
        return column switch
        {
            LibraryColumn.Title => item.Title,
            LibraryColumn.Artist => item.Artist,
            LibraryColumn.Album => item.Album,
            LibraryColumn.AlbumArtist => tags.AlbumArtist,
            LibraryColumn.Genre => tags.Genre,
            LibraryColumn.Year => Number(tags.Year),
            LibraryColumn.Track => Number(tags.TrackNumber),
            LibraryColumn.Disc => Number(tags.Disc),
            LibraryColumn.Composer => tags.Composer,
            LibraryColumn.Bpm => Number(tags.Bpm),
            LibraryColumn.Key => tags.Key,
            LibraryColumn.Length => item.DurationText,
            LibraryColumn.Bitrate => item.Track is { Bitrate: > 0 } track ? $"{track.Bitrate} kbps" : string.Empty,
            LibraryColumn.SampleRate => item.Track is { SampleRate: > 0 } track ? $"{track.SampleRate / 1000.0:0.#} kHz" : string.Empty,
            LibraryColumn.Format => FormatOf(item),
            LibraryColumn.Rating => TrackStats.Stars(item.Rating),
            LibraryColumn.Plays => item.Plays > 0 ? item.Plays.ToString("N0", CultureInfo.CurrentCulture) : string.Empty,
            LibraryColumn.LastPlayed => item.LastPlayed is { } played ? played.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : string.Empty,
            LibraryColumn.Added => item.Added.ToString("d", CultureInfo.CurrentCulture),
            LibraryColumn.FileName => item.FileName,
            _ => tags.Comment,
        };
    }

    /// <summary>"FLAC", "MP3": the file's extension.</summary>
    public static string FormatOf(LibraryItem item) =>
        System.IO.Path.GetExtension(item.Path).TrimStart('.').ToUpperInvariant();

    /// <summary>
    /// Compares two songs by the column, A to Z or low to high. Songs that don't have the tag come after
    /// those that do whichever way it's sorted, so they never crowd the top; <paramref name="descending"/>
    /// flips only the songs that have it.
    /// </summary>
    public static int Compare(LibraryColumn column, LibraryItem a, LibraryItem b, bool descending)
    {
        var (aValue, bValue) = (SortValue(column, a), SortValue(column, b));
        var aMissing = aValue is null;
        var bMissing = bValue is null;
        if (aMissing || bMissing)
        {
            return aMissing.CompareTo(bMissing);
        }

        var result = aValue is string aText
            ? string.Compare(aText, (string)bValue!, StringComparison.CurrentCultureIgnoreCase)
            : ((IComparable)aValue!).CompareTo(bValue);
        return descending ? -result : result;
    }

    /// <summary>What a song sorts by in the column, or null when it doesn't have it.</summary>
    private static object? SortValue(LibraryColumn column, LibraryItem item)
    {
        var track = item.Track;
        var tags = track?.Tags ?? Playlist.TrackTagValues.None;
        return column switch
        {
            LibraryColumn.Year => NumberOrNull(tags.Year),
            LibraryColumn.Track => NumberOrNull(tags.TrackNumber),
            LibraryColumn.Disc => NumberOrNull(tags.Disc),
            LibraryColumn.Bpm => NumberOrNull(tags.Bpm),
            LibraryColumn.Length => track is { Duration.Ticks: > 0 } ? track.Duration : null,
            LibraryColumn.Bitrate => track is { Bitrate: > 0 } ? track.Bitrate : null,
            LibraryColumn.SampleRate => track is { SampleRate: > 0 } ? track.SampleRate : null,
            LibraryColumn.Rating => item.Rating > 0 ? item.Rating : null,
            LibraryColumn.Plays => item.Plays > 0 ? item.Plays : null,
            LibraryColumn.LastPlayed => item.LastPlayed,
            LibraryColumn.Added => item.Added,
            _ => Text(column, item) is { Length: > 0 } text ? text : null,
        };
    }

    private static string Number(uint value) => value == 0 ? string.Empty : value.ToString(CultureInfo.CurrentCulture);

    private static object? NumberOrNull(uint value) => value == 0 ? null : value;
}
