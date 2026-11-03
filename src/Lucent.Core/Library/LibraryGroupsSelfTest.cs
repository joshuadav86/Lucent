using System.IO;
using Lucent.Playlist;
using Lucent.Settings;

namespace Lucent.Library;

/// <summary>
/// Checks the library's headings (<see cref="LibraryGroups"/>): songs under a folder, artist or album heading go in disc
/// and track order, a compilation stays one album, and songs without an artist or album go last. Both apps run it in
/// their self-tests.
/// </summary>
public static class LibraryGroupsSelfTest
{
    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        try
        {
            return Check(log);
        }
        catch (Exception ex)
        {
            log($"Library headings: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Check(Action<string> log)
    {
        var root = Path.Combine(Path.GetTempPath(), "Music");
        var day = new DateTime(2026, 10, 1);
        var songs = new List<LibraryItem>
        {
            // An album that arrived out of order, over two discs.
            Song("Bicep/Isles", "Saku", "Bicep", "Isles", "", disc: 1, track: 4, day.AddHours(3)),
            Song("Bicep/Isles", "Atlas", "Bicep", "Isles", "", disc: 1, track: 1, day.AddHours(1)),
            Song("Bicep/Isles", "Hawk", "Bicep", "Isles", "", disc: 2, track: 1, day.AddHours(5)),
            Song("Bicep/Isles", "Apricots", "BICEP", "Isles", "", disc: 1, track: 3, day.AddHours(2)),
            // A compilation with no album artist: one album, by various artists.
            Song("Comps/Fabric 50", "One", "Four Tet", "Fabric 50", "", disc: 0, track: 2, day.AddDays(1)),
            Song("Comps/Fabric 50", "Two", "Caribou", "Fabric 50", "", disc: 0, track: 1, day.AddDays(1)),
            Song("Comps/Fabric 50", "Three", "Floating Points", "Fabric 50", "", disc: 0, track: 3, day.AddDays(1)),
            // One with an album artist.
            Song("Comps/Late Night Tales", "Four", "Khruangbin", "Late Night Tales", "Bonobo", disc: 0, track: 1, day.AddDays(2)),
            // Songs with no album, and none with no artist either. "Demo" came first, so newest first lists it last
            // although its name comes first.
            Song("Loose", "Single", "Bicep", "", "", disc: 0, track: 0, day.AddDays(3)),
            Song("Loose", "Mystery", "", "", "", disc: 0, track: 0, day.AddDays(4)),
            Song("Loose", "Demo", "", "", "", disc: 0, track: 0, day.AddDays(-1)),
        };

        var failures = 0;
        void Expect(string what, IEnumerable<string> actual, params string[] expected)
        {
            var got = actual.ToList();
            if (!got.SequenceEqual(expected))
            {
                log($"Library headings: {what}: expected {string.Join(", ", expected)}, got {string.Join(", ", got)}");
                failures++;
            }
        }

        var settings = new AppSettings { LibrarySort = LibrarySort.Newest };

        settings.LibraryGrouping = LibraryGrouping.Folders;
        LibraryGroups.Assign(songs, settings.LibraryGrouping);
        var order = Sorted(songs, settings);
        Expect("folder in track order", order.Where(song => song.Folder == "Bicep / Isles").Select(song => song.Title),
            "Atlas", "Apricots", "Saku", "Hawk");
        Expect("folders newest first", order.Select(song => song.Heading).Distinct(),
            "Loose", "Comps / Late Night Tales", "Comps / Fabric 50", "Bicep / Isles");
        Expect("loose songs newest first", order.Where(song => song.Folder == "Loose").Select(song => song.Title),
            "Mystery", "Single", "Demo");

        settings.LibrarySort = LibrarySort.Oldest;
        order = Sorted(songs, settings);
        Expect("folders oldest first", order.Select(song => song.Heading).Distinct(),
            "Loose", "Bicep / Isles", "Comps / Fabric 50", "Comps / Late Night Tales");
        Expect("loose songs oldest first", order.Where(song => song.Folder == "Loose").Select(song => song.Title),
            "Demo", "Single", "Mystery");
        Expect("album in track order oldest first", order.Where(song => song.Folder == "Bicep / Isles").Select(song => song.Title),
            "Atlas", "Apricots", "Saku", "Hawk");

        // A folder with an album and loose songs: the album stays together, where its newest song puts it.
        var mixed = new List<LibraryItem>
        {
            Song("Mixed", "Old loose", "", "", "", disc: 0, track: 0, day),
            Song("Mixed", "Side B", "Bicep", "Lost", "", disc: 0, track: 2, day.AddDays(2)),
            Song("Mixed", "Side A", "Bicep", "Lost", "", disc: 0, track: 1, day.AddDays(1)),
            Song("Mixed", "New loose", "", "", "", disc: 0, track: 0, day.AddDays(3)),
        };
        settings.LibrarySort = LibrarySort.Newest;
        LibraryGroups.Assign(mixed, settings.LibraryGrouping);
        Expect("album kept together among loose songs", Sorted(mixed, settings).Select(song => song.Title),
            "New loose", "Side A", "Side B", "Old loose");

        settings.LibrarySort = LibrarySort.Title;
        Expect("folder by title", Sorted(songs, settings).Where(song => song.Folder == "Loose").Select(song => song.Title),
            "Demo", "Mystery", "Single");
        settings.LibrarySort = LibrarySort.Artist;
        Expect("folder by artist, no artist last", Sorted(songs, settings).Where(song => song.Folder == "Loose").Select(song => song.Title),
            "Single", "Demo", "Mystery");
        settings.LibrarySort = LibrarySort.Newest;

        settings.LibraryGrouping = LibraryGrouping.Artists;
        LibraryGroups.Assign(songs, settings.LibraryGrouping);
        order = Sorted(songs, settings);
        Expect("artists newest first, unknown last", order.Select(song => song.Heading).Distinct(),
            "Bicep", "Bonobo", "Various artists", "Unknown artist");
        Expect("artist's songs without an album, then albums in track order",
            order.Where(song => song.Heading == "Bicep").Select(song => song.Title), "Single", "Atlas", "Apricots", "Saku", "Hawk");
        Expect("compilation in track order", order.Where(song => song.Heading == "Various artists").Select(song => song.Title),
            "Two", "One", "Three");
        Expect("album subheading", songs.Where(song => song.Title == "Atlas").Select(song => song.SubheadingLabel), "Isles (2021)");

        settings.LibraryGrouping = LibraryGrouping.Albums;
        LibraryGroups.Assign(songs, settings.LibraryGrouping);
        settings.LibrarySort = LibrarySort.Title;
        order = Sorted(songs, settings);
        Expect("albums by name, no album last", order.Select(song => song.Heading).Distinct(),
            "Fabric 50  ·  Various artists", "Isles  ·  Bicep", "Late Night Tales  ·  Bonobo", LibraryGroups.NoAlbum);

        // Without headings, sorting by artist lists each artist's albums in track order.
        settings.GroupLibraryByFolder = false;
        settings.LibrarySort = LibrarySort.Artist;
        var flat = Flat(songs, settings);
        Expect("by artist in album order", flat.Where(song => song.Artist.Equals("Bicep", StringComparison.OrdinalIgnoreCase))
            .Select(song => song.Title), "Atlas", "Apricots", "Saku", "Hawk", "Single");

        // Every other order without headings.
        settings.LibrarySort = LibrarySort.Newest;
        Expect("newest first", Flat(songs, settings).Take(4).Select(song => song.Title), "Mystery", "Single", "Four", "One");
        settings.LibrarySort = LibrarySort.Oldest;
        Expect("oldest first", Flat(songs, settings).Take(3).Select(song => song.Title), "Demo", "Atlas", "Apricots");
        settings.LibrarySort = LibrarySort.Title;
        Expect("by title", Flat(songs, settings).Take(4).Select(song => song.Title), "Apricots", "Atlas", "Demo", "Four");
        settings.LibrarySort = LibrarySort.FileName;
        Expect("by file name", Flat(songs, settings).Take(2).Select(song => song.Title), "Apricots", "Atlas");

        // Tag columns: a first click on Date added is newest first, a second oldest first; songs without the tag go last
        // either way.
        settings.LibrarySortColumn = LibraryColumn.Added;
        settings.LibrarySortDescending = LibraryColumns.DescendingFirst(LibraryColumn.Added);
        Expect("date added newest first", Flat(songs, settings).Take(2).Select(song => song.Title), "Mystery", "Single");
        settings.LibrarySortDescending = false;
        Expect("date added oldest first", Flat(songs, settings).Take(2).Select(song => song.Title), "Demo", "Atlas");
        settings.LibrarySortColumn = LibraryColumn.Year;
        Expect("year, missing last", Flat(songs, settings).Take(4).Select(song => song.Title).Concat(new[] { Flat(songs, settings)[^1].Title }),
            "Atlas", "Apricots", "Saku", "Hawk", "Mystery");
        settings.LibrarySortDescending = true;
        Expect("year flipped, missing still last", Flat(songs, settings).Take(1).Select(song => song.Title), "Atlas");

        // With folder headings, a column sorts the songs under each one by it.
        settings.GroupLibraryByFolder = true;
        settings.LibraryGrouping = LibraryGrouping.Folders;
        LibraryGroups.Assign(songs, settings.LibraryGrouping);
        settings.LibrarySortColumn = LibraryColumn.Added;
        settings.LibrarySortDescending = true;
        Expect("folder by date added", Sorted(songs, settings).Where(song => song.Folder == "Loose").Select(song => song.Title),
            "Mystery", "Single", "Demo");

        log(failures == 0 ? "Library headings: OK" : $"Library headings: {failures} problem(s)");
        return failures;

        LibraryItem Song(string folder, string title, string artist, string album, string albumArtist, uint disc, uint track,
            DateTime added)
        {
            var directory = Path.Combine(root, folder);
            var path = Path.Combine(directory, title + ".flac");
            var item = new LibraryItem(path, folder.Replace("/", " / "), directory, added);
            var tags = new TrackTagValues { AlbumArtist = albumArtist, Disc = disc, TrackNumber = track, Year = album == "Isles" ? 2021u : 0u };
            item.ApplyTags(Track.Sample(path, title, artist, album, tags));
            return item;
        }
    }

    private static List<LibraryItem> Flat(List<LibraryItem> songs, AppSettings settings)
    {
        var sorted = songs.ToList();
        sorted.Sort(LibraryItemComparer.For(settings).Compare);
        return sorted;
    }

    private static List<LibraryItem> Sorted(List<LibraryItem> songs, AppSettings settings)
    {
        var sorted = songs.ToList();
        sorted.Sort(LibraryGroups.Comparer(songs, settings).Compare);
        return sorted;
    }
}
