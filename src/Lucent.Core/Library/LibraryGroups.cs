using System.Collections;
using System.Globalization;
using Lucent.Settings;

namespace Lucent.Library;

/// <summary>What the library's headings stand for.</summary>
public enum LibraryGrouping
{
    Folders,
    Artists,
    Albums,
}

/// <summary>
/// The library's headings: a folder's (as on disk), an artist's (with a subheading for each of their albums), or an
/// album's; and the order songs go in under them. Under any heading an album's songs play in disc and track order.
/// </summary>
public static class LibraryGroups
{
    public const string UnknownArtist = "Unknown artist";
    public const string VariousArtists = "Various artists";
    public const string NoAlbum = "No album";

    public static IReadOnlyList<LibraryGrouping> All { get; } = Enum.GetValues<LibraryGrouping>();

    /// <summary>"Folders", for the menu.</summary>
    public static string Label(LibraryGrouping grouping) => grouping.ToString();

    /// <summary>"FOLDERS", for the button.</summary>
    public static string ButtonLabel(LibraryGrouping grouping) => Label(grouping).ToUpperInvariant();

    /// <summary>What one heading is: "folder", "artist", or "album".</summary>
    public static string Noun(LibraryGrouping grouping) => grouping switch
    {
        LibraryGrouping.Artists => "artist",
        LibraryGrouping.Albums => "album",
        _ => "folder",
    };

    /// <summary>
    /// Puts each song under its heading for the grouping (<see cref="LibraryItem.Heading"/>, <see cref="LibraryItem.Subheading"/>).
    /// An album is its name and album artist; with no album artist, its name and the folder it's in, so a compilation
    /// stays together. Its artist is the album artist, or the artist most of its songs share, or "Various artists".
    /// Names that differ only in case go under one heading.
    /// </summary>
    public static void Assign(IReadOnlyList<LibraryItem> items, LibraryGrouping grouping)
    {
        if (grouping == LibraryGrouping.Folders)
        {
            foreach (var item in items)
            {
                item.SetHeadings(item.Section, item.Folder, item.SubFolder);
            }

            return;
        }

        var albums = new Dictionary<LibraryItem, AlbumInfo>();
        foreach (var album in items.Where(item => !item.IsPlaceholder && item.Album.Length > 0)
                     .GroupBy(AlbumKey, StringComparer.CurrentCultureIgnoreCase))
        {
            var songs = album.ToList();
            var year = songs.Select(song => song.Track?.Tags.Year ?? 0).Where(y => y > 0).DefaultIfEmpty(0u).Min();
            var info = new AlbumInfo(songs[0].Album, ArtistOf(songs), year);
            foreach (var song in songs)
            {
                albums[song] = info;
            }
        }

        // The first spelling of each name is the one shown.
        var spellings = new Dictionary<string, string>(StringComparer.CurrentCultureIgnoreCase);
        string Spelled(string name)
        {
            if (!spellings.TryGetValue(name, out var shown))
            {
                spellings[name] = shown = name;
            }

            return shown;
        }

        foreach (var item in items)
        {
            if (item.IsPlaceholder)
            {
                continue;
            }

            var album = albums.GetValueOrDefault(item);
            var artist = Spelled(album?.Artist ?? (item.Artist.Length > 0 ? item.Artist : UnknownArtist));
            if (grouping == LibraryGrouping.Artists)
            {
                if (album is null)
                {
                    item.SetHeadings(artist, artist, artist);
                }
                else
                {
                    var name = Spelled(album.Name);
                    var label = album.Year > 0 ? $"{name} ({album.Year.ToString(CultureInfo.CurrentCulture)})" : name;
                    item.SetHeadings(artist, artist + " / " + name, label);
                }
            }
            else
            {
                var heading = album is null ? NoAlbum : $"{Spelled(album.Name)}  ·  {artist}";
                item.SetHeadings(heading, heading, heading);
            }
        }
    }

    /// <summary>Whether a heading is the catch-all for songs without the tag, which goes last.</summary>
    public static bool IsUnknown(string heading) => heading is UnknownArtist or NoAlbum;

    /// <summary>
    /// The order for the library with headings. The headings go in the chosen order, judged by each one's first song
    /// (newest first: the folder or album that arrived last), or by name for Title and File name, with the songs
    /// without an artist or album last. Under each heading the songs go in album order (album, disc, track) for
    /// Newest, Oldest and Artist; any other order sorts them by it, with ties in album order.
    /// Call <see cref="Assign"/> first, with the settings' <see cref="AppSettings.LibraryGrouping"/>.
    /// </summary>
    public static IComparer Comparer(IReadOnlyList<LibraryItem> items, AppSettings settings) =>
        new GroupComparer(items, settings);

    private sealed class GroupComparer : IComparer
    {
        private readonly Dictionary<string, int> _headings = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _subheadings = new(StringComparer.Ordinal);
        private readonly LibraryItemComparer _songs;
        private readonly bool _albumOrder;
        private readonly bool _unknownLast;

        public GroupComparer(IReadOnlyList<LibraryItem> items, AppSettings settings)
        {
            _songs = LibraryItemComparer.For(settings);
            _unknownLast = settings.LibraryGrouping != LibraryGrouping.Folders;
            _albumOrder = settings.LibrarySortColumn is null
                && settings.LibrarySort is LibrarySort.Newest or LibrarySort.Oldest or LibrarySort.Artist;
            var byName = settings.LibrarySortColumn is null && settings.LibrarySort is LibrarySort.Title or LibrarySort.FileName;
            var songs = items.Where(item => !item.IsPlaceholder).ToList();
            if (byName)
            {
                Rank(_headings, songs.Select(item => item.Heading).Distinct().Order(StringComparer.CurrentCultureIgnoreCase));
                Rank(_subheadings, songs.OrderBy(item => item.SubheadingLabel, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => item.Subheading));
            }
            else
            {
                songs.Sort(_songs.Compare);
                Rank(_headings, songs.Select(item => item.Heading));
                Rank(_subheadings, songs.Select(item => item.Subheading));
            }
        }

        private void Rank(Dictionary<string, int> ranks, IEnumerable<string> inOrder)
        {
            foreach (var name in inOrder)
            {
                ranks.TryAdd(name, _unknownLast && IsUnknown(name) ? int.MaxValue : ranks.Count);
            }
        }

        public int Compare(object? x, object? y)
        {
            var a = (LibraryItem)x!;
            var b = (LibraryItem)y!;
            var result = HeadingRank(a).CompareTo(HeadingRank(b));
            if (result == 0)
            {
                result = SubheadingRank(a).CompareTo(SubheadingRank(b));
            }

            if (result != 0)
            {
                return result;
            }

            if (_albumOrder)
            {
                result = LibraryItemComparer.TrackOrder(a, b);
                return result != 0 ? result : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            }

            return _songs.Compare(a, b);
        }

        private int HeadingRank(LibraryItem item) => _headings.GetValueOrDefault(item.Heading, int.MaxValue);

        // The songs right under a heading (not in a subheading) come before its subheadings.
        private int SubheadingRank(LibraryItem item) => item.HasSubheading
            ? _subheadings.GetValueOrDefault(item.Subheading, int.MaxValue)
            : -1;
    }

    private sealed record AlbumInfo(string Name, string Artist, uint Year);

    private static string AlbumKey(LibraryItem item) => item.Track?.Tags.AlbumArtist is { Length: > 0 } albumArtist
        ? item.Album + "\0" + albumArtist
        : item.Album + "\0\0" + System.IO.Path.GetDirectoryName(item.Path);

    /// <summary>The album artist, or the artist more than half its songs have, or "Various artists".</summary>
    private static string ArtistOf(IReadOnlyList<LibraryItem> songs)
    {
        if (songs.Select(song => song.Track?.Tags.AlbumArtist).FirstOrDefault(name => !string.IsNullOrEmpty(name)) is { } albumArtist)
        {
            return albumArtist;
        }

        var named = songs.Where(song => song.Artist.Length > 0).ToList();
        if (named.Count == 0)
        {
            return UnknownArtist;
        }

        var most = named.GroupBy(song => song.Artist, StringComparer.CurrentCultureIgnoreCase)
            .MaxBy(artist => artist.Count())!;
        return most.Count() * 2 > songs.Count ? most.First().Artist : VariousArtists;
    }
}
