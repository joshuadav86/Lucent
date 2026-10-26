namespace Lucent.Library;

/// <summary>The orders another app's playlists (Spotify's, Music's) can be listed in under PLAYLISTS.</summary>
public enum RemotePlaylistSort
{
    /// <summary>As the app lists them: Spotify puts the ones most recently added or played first.</summary>
    AppOrder,
    Name,
    MostSongs,
}

public static class RemotePlaylistSorts
{
    public static IReadOnlyList<RemotePlaylistSort> All { get; } = Enum.GetValues<RemotePlaylistSort>();

    public static string Label(RemotePlaylistSort sort) => sort switch
    {
        RemotePlaylistSort.Name => "Name",
        RemotePlaylistSort.MostSongs => "Most songs",
        _ => "App's order",
    };
}
