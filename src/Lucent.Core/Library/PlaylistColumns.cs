using System.Globalization;
using Lucent.Streaming;

namespace Lucent.Library;

/// <summary>
/// What an opened streaming playlist (Spotify's, Music's) can show beside each song, and sort by: what the app says
/// about its songs. Title and Length are the row's own title and time, so they're sorted by but never added as columns.
/// </summary>
public enum PlaylistColumn
{
    /// <summary>The song's place in the playlist, the order the app plays it in without shuffle.</summary>
    Order,
    Title,
    Artist,
    Album,
    AlbumArtist,
    Genre,
    Year,
    Track,
    Disc,
    Added,
    Length,
}

/// <summary>
/// The opened playlist's columns: as the library's (<see cref="LibraryColumns"/>), and named, sized and lined up the
/// same, but filled from what the app says about each song.
/// </summary>
public static class PlaylistColumns
{
    /// <summary>Every column, in the order the sort menu lists them.</summary>
    public static IReadOnlyList<PlaylistColumn> All { get; } = Enum.GetValues<PlaylistColumn>();

    /// <summary>The columns that can be added beside the songs (all but the row's own title and time).</summary>
    public static IReadOnlyList<PlaylistColumn> Choosable { get; } =
        All.Where(column => column is not (PlaylistColumn.Title or PlaylistColumn.Length)).ToList();

    /// <summary>
    /// The columns <paramref name="list"/> can fill: those at least one of its songs has a value for. Spotify gives no
    /// genres, and only Music gives a date added for every playlist.
    /// </summary>
    public static IReadOnlyList<PlaylistColumn> For(RemoteList list) =>
        All.Where(column => column is PlaylistColumn.Order or PlaylistColumn.Title || list.Tracks.Any(track => SortValue(column, track, 0) is not null))
            .ToList();

    public static string Label(PlaylistColumn column) =>
        column == PlaylistColumn.Order ? "Playlist order" : LibraryColumns.Label(Same(column));

    /// <summary>The heading's text: short, to fit over a narrow column.</summary>
    public static string Heading(PlaylistColumn column) =>
        column == PlaylistColumn.Order ? "#" : LibraryColumns.Heading(Same(column));

    /// <summary>The column's width, with its divider line and the space either side of the text.</summary>
    public static double Width(PlaylistColumn column) =>
        column == PlaylistColumn.Order ? 52 : LibraryColumns.Width(Same(column));

    public static bool AlignRight(PlaylistColumn column) =>
        column == PlaylistColumn.Order || LibraryColumns.AlignRight(Same(column));

    /// <summary>Whether a first click on the heading sorts high to low: the songs added most recently first.</summary>
    public static bool DescendingFirst(PlaylistColumn column) => column == PlaylistColumn.Added;

    /// <summary>"Date added ▼" for the sort button. The playlist's own order has no arrow until it's turned round.</summary>
    public static string SortLabel(PlaylistColumn column, bool descending) => column == PlaylistColumn.Order
        ? descending ? "Playlist order, reversed" : Label(column)
        : $"{Label(column)} {(descending ? "▼" : "▲")}";

    /// <summary>The library column of the same name, for its label, width and alignment.</summary>
    private static LibraryColumn Same(PlaylistColumn column) => Enum.Parse<LibraryColumn>(column.ToString());

    /// <summary>
    /// The cells of a song's row for <paramref name="shown"/>. <paramref name="order"/> is its place in the playlist,
    /// counting from 0.
    /// </summary>
    public static IReadOnlyList<LibraryCell> Cells(IReadOnlyList<PlaylistColumn> shown, RemoteTrack track, int order) =>
        shown.Select(column => new LibraryCell(Text(column, track, order), Width(column), AlignRight(column))).ToList();

    /// <summary>What the song's row shows in the column; empty when the app doesn't say.</summary>
    public static string Text(PlaylistColumn column, RemoteTrack track, int order) => column switch
    {
        PlaylistColumn.Order => (order + 1).ToString("N0", CultureInfo.CurrentCulture),
        PlaylistColumn.Title => track.Title,
        PlaylistColumn.Artist => track.Artist,
        PlaylistColumn.Album => track.Album,
        PlaylistColumn.AlbumArtist => track.AlbumArtist,
        PlaylistColumn.Genre => track.Genre,
        PlaylistColumn.Year => Number(track.Year),
        PlaylistColumn.Track => Number(track.TrackNumber),
        PlaylistColumn.Disc => Number(track.Disc),
        PlaylistColumn.Added => track.AddedAt is { } added ? added.ToLocalTime().ToString("d", CultureInfo.CurrentCulture) : string.Empty,
        _ => track.DurationText,
    };

    /// <summary>
    /// <paramref name="tracks"/> (the playlist, in its own order) sorted by the column, each with its place in the
    /// playlist. Songs without a value go last whichever way it's sorted, as in the library, and songs that tie keep
    /// the playlist's order, by album and track under an artist or album.
    /// </summary>
    public static IReadOnlyList<(RemoteTrack Track, int Order)> Sort(IReadOnlyList<RemoteTrack> tracks, PlaylistColumn column, bool descending)
    {
        var listed = tracks.Select((track, order) => (Track: track, Order: order)).ToList();
        if (column == PlaylistColumn.Order)
        {
            if (descending)
            {
                listed.Reverse();
            }

            return listed;
        }

        var ordered = listed
            .OrderBy(entry => SortValue(column, entry.Track, entry.Order) is null)
            .ThenBy(entry => SortValue(column, entry.Track, entry.Order), new Flipped(descending));
        if (column is PlaylistColumn.Artist or PlaylistColumn.AlbumArtist or PlaylistColumn.Album or PlaylistColumn.Year)
        {
            ordered = ordered
                .ThenBy(entry => RemoteListSorting.SortKey(entry.Track.Album), StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(entry => entry.Track.Disc)
                .ThenBy(entry => entry.Track.TrackNumber);
        }

        return ordered.ToList();
    }

    /// <summary>What a song sorts by in the column, or null when it doesn't have it.</summary>
    private static IComparable? SortValue(PlaylistColumn column, RemoteTrack track, int order) => column switch
    {
        PlaylistColumn.Order => (IComparable)order,
        PlaylistColumn.Year => NumberOrNull(track.Year),
        PlaylistColumn.Track => NumberOrNull(track.TrackNumber),
        PlaylistColumn.Disc => NumberOrNull(track.Disc),
        PlaylistColumn.Added => track.AddedAt,
        PlaylistColumn.Length => track.Duration > TimeSpan.Zero ? (IComparable)track.Duration : null,
        // "The Cure" under C, as Spotify's own sort does.
        _ => Text(column, track, order) is { Length: > 0 } text ? RemoteListSorting.SortKey(text) : null,
    };

    private static string Number(uint value) => value == 0 ? string.Empty : value.ToString(CultureInfo.CurrentCulture);

    private static IComparable? NumberOrNull(uint value) => value == 0 ? null : value;

    // A to Z or low to high, or the other way; text without regard to case.
    private sealed class Flipped(bool descending) : IComparer<IComparable?>
    {
        public int Compare(IComparable? a, IComparable? b)
        {
            var result = a is null || b is null
                ? (a is null).CompareTo(b is null)
                : a is string aText && b is string bText
                    ? string.Compare(aText, bText, StringComparison.CurrentCultureIgnoreCase)
                    : a.CompareTo(b);
            return descending ? -result : result;
        }
    }
}
