using System.Collections;

namespace Lucent.Library;

/// <summary>
/// The order of the rows under PLAYLISTS when a column heading is clicked: each playlist stays where it is, and the
/// songs inside it are sorted by the column. Songs that tie keep the playlist's order, and the rows that aren't songs
/// (another app's playlists, an empty playlist's note) aren't moved. The playlists' files are never changed.
/// </summary>
public static class SavedPlaylistOrder
{
    /// <summary>
    /// Sorts <paramref name="items"/> (the rows under PLAYLISTS, in their own order) by the column within each playlist.
    /// </summary>
    public static IComparer Comparer(IReadOnlyList<LibraryItem> items, LibraryColumn column, bool descending) =>
        new Order(items, column, descending);

    /// <summary>
    /// What a click on a column's heading sorts by next: a first click sorts by it (high to low first for plays, ratings
    /// and dates, as in the library), a second turns it round, and a third puts the playlists back in their own order.
    /// </summary>
    public static (LibraryColumn? Column, bool Descending) Next(LibraryColumn? sorted, bool descending, LibraryColumn clicked)
    {
        var first = LibraryColumns.DescendingFirst(clicked);
        if (sorted != clicked)
        {
            return (clicked, first);
        }

        return descending == first ? (clicked, !first) : (null, false);
    }

    /// <summary>Whether the order depends on plays or ratings, so it's sorted again when they change.</summary>
    public static bool NeedsStats(LibraryColumn? column) =>
        column is LibraryColumn.Rating or LibraryColumn.Plays or LibraryColumn.LastPlayed;

    private sealed class Order : IComparer
    {
        private readonly Dictionary<LibraryItem, int> _place = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, int> _playlistPlace = new(StringComparer.Ordinal);
        private readonly LibraryColumn _column;
        private readonly bool _descending;

        public Order(IReadOnlyList<LibraryItem> items, LibraryColumn column, bool descending)
        {
            _column = column;
            _descending = descending;
            for (var i = 0; i < items.Count; i++)
            {
                _place[items[i]] = i;
                _playlistPlace.TryAdd(items[i].Heading, i);
            }
        }

        public int Compare(object? x, object? y)
        {
            var a = (LibraryItem)x!;
            var b = (LibraryItem)y!;
            var result = PlaylistPlace(a).CompareTo(PlaylistPlace(b));
            if (result == 0 && IsSong(a) && IsSong(b))
            {
                result = LibraryColumns.Compare(_column, a, b, _descending);
            }

            return result != 0 ? result : Place(a).CompareTo(Place(b));
        }

        private int PlaylistPlace(LibraryItem item) => _playlistPlace.TryGetValue(item.Heading, out var place) ? place : int.MaxValue;

        private int Place(LibraryItem item) => _place.TryGetValue(item, out var place) ? place : int.MaxValue;

        private static bool IsSong(LibraryItem item) => !item.IsPlaceholder && item.RemotePlaylist is null;
    }
}
