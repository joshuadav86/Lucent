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
            // Songs with no album, and none with no artist either.
            Song("Loose", "Single", "Bicep", "", "", disc: 0, track: 0, day.AddDays(3)),
            Song("Loose", "Mystery", "", "", "", disc: 0, track: 0, day.AddDays(4)),
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
        var flat = songs.ToList();
        flat.Sort(LibraryItemComparer.For(settings).Compare);
        Expect("by artist in album order", flat.Where(song => song.Artist.Equals("Bicep", StringComparison.OrdinalIgnoreCase))
            .Select(song => song.Title), "Atlas", "Apricots", "Saku", "Hawk", "Single");

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

    private static List<LibraryItem> Sorted(List<LibraryItem> songs, AppSettings settings)
    {
        var sorted = songs.ToList();
        sorted.Sort(LibraryGroups.Comparer(songs, settings).Compare);
        return sorted;
    }
}
