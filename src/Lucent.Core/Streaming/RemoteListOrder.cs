namespace Lucent.Streaming;

/// <summary>How the queue window lists another app's playlist (Spotify's).</summary>
public enum RemoteListOrder
{
    /// <summary>As the app lists it, the order it plays in without shuffle.</summary>
    Playlist,

    /// <summary>The songs added most recently first.</summary>
    RecentlyAdded,

    /// <summary>By title, A to Z.</summary>
    Title,

    /// <summary>By artist, A to Z, then by album and title.</summary>
    Artist,
}

public static class RemoteListSorting
{
    /// <summary>The order's name in the queue window's Sort menu and on its button.</summary>
    public static string Name(RemoteListOrder order) => order switch
    {
        RemoteListOrder.RecentlyAdded => "Recently added",
        RemoteListOrder.Title => "Title",
        RemoteListOrder.Artist => "Artist",
        _ => "Playlist order",
    };

    /// <summary>The orders a list can be shown in: by date added only when the app said when its songs were added.</summary>
    public static IReadOnlyList<RemoteListOrder> OrdersFor(RemoteList list) =>
        list.Tracks.Any(track => track.AddedAt is not null)
            ? new[] { RemoteListOrder.Playlist, RemoteListOrder.RecentlyAdded, RemoteListOrder.Title, RemoteListOrder.Artist }
            : new[] { RemoteListOrder.Playlist, RemoteListOrder.Title, RemoteListOrder.Artist };

    /// <summary>
    /// <paramref name="tracks"/> in <paramref name="order"/>. Sorting is stable, so songs that tie (no date, the same
    /// title) keep the playlist's order.
    /// </summary>
    public static IReadOnlyList<RemoteTrack> Sort(IReadOnlyList<RemoteTrack> tracks, RemoteListOrder order) => order switch
    {
        RemoteListOrder.RecentlyAdded => tracks.OrderByDescending(track => track.AddedAt ?? DateTime.MinValue).ToList(),
        RemoteListOrder.Title => tracks.OrderBy(track => SortKey(track.Title), Comparer).ToList(),
        RemoteListOrder.Artist => tracks.OrderBy(track => SortKey(track.Artist), Comparer)
            .ThenBy(track => SortKey(track.Album), Comparer)
            .ThenBy(track => SortKey(track.Title), Comparer)
            .ToList(),
        _ => tracks,
    };

    private static readonly StringComparer Comparer = StringComparer.CurrentCultureIgnoreCase;

    // "... Passing By" files under P and "The Cure" under C, as Spotify's own sort does.
    internal static string SortKey(string text)
    {
        var key = text.TrimStart(' ', '.', '…', '"', '\'', '(', '[', '-', '#', '*');
        return key.StartsWith("The ", StringComparison.OrdinalIgnoreCase) && key.Length > 4 ? key[4..] : key;
    }
}
