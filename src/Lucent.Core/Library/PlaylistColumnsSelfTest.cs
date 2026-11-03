using System.Text.Json;
using Lucent.Streaming;

namespace Lucent.Library;

/// <summary>
/// Checks an opened streaming playlist's columns (<see cref="PlaylistColumns"/>): what Spotify's songs fill them with,
/// which columns a playlist offers, and the sorts. Also the saved playlists' sort under PLAYLISTS. Both apps run it in
/// their self-tests.
/// </summary>
public static class PlaylistColumnsSelfTest
{
    /// <summary>Returns the number of problems.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Check(string what, bool ok, string got)
        {
            if (!ok)
            {
                log($"::error::Playlist columns: {what}: got {got}.");
                failures++;
            }
        }

        try
        {
            using var json = JsonDocument.Parse("""
                {"id":"t1","name":"Roygbiv","duration_ms":151000,"track_number":7,"disc_number":1,"uri":"spotify:track:t1",
                 "artists":[{"name":"Boards of Canada"}],
                 "album":{"name":"Music Has the Right to Children","release_date":"1998-04-20","artists":[{"name":"Boards of Canada"}]}}
                """);
            var parsed = SpotifyRemote.ParseTrack(json.RootElement, null);
            Check("Spotify song", parsed is { Year: 1998, TrackNumber: 7, Disc: 1, AlbumArtist: "Boards of Canada" },
                parsed is null ? "nothing" : $"{parsed.Year}, {parsed.TrackNumber}, {parsed.Disc}, {parsed.AlbumArtist}");

            var day = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
            RemoteTrack Song(string title, string artist, string album, uint year, DateTime? added) =>
                new(title, title, artist, album, TimeSpan.FromMinutes(3), AddedAt: added) { Year = year };
            var tracks = new[]
            {
                Song("Aged", "M83", "Hurry Up", 2011, day),
                Song("Lullaby", "The Cure", "Disintegration", 1989, day.AddDays(2)),
                Song("Blip", "Beacon", "Blip", 0, null),
                Song("Air", "Bicep", "Isles", 2021, day.AddDays(1)),
            };
            string Titles(PlaylistColumn column, bool descending) =>
                string.Join(", ", PlaylistColumns.Sort(tracks, column, descending).Select(song => song.Track.Title));

            Check("newest first", Titles(PlaylistColumn.Added, true) == "Lullaby, Air, Aged, Blip", Titles(PlaylistColumn.Added, true));
            Check("oldest first", Titles(PlaylistColumn.Added, false) == "Aged, Air, Lullaby, Blip", Titles(PlaylistColumn.Added, false));
            Check("by artist", Titles(PlaylistColumn.Artist, false) == "Blip, Air, Lullaby, Aged", Titles(PlaylistColumn.Artist, false));
            Check("by year", Titles(PlaylistColumn.Year, false) == "Lullaby, Aged, Air, Blip", Titles(PlaylistColumn.Year, false));
            Check("reversed", Titles(PlaylistColumn.Order, true) == "Air, Blip, Lullaby, Aged", Titles(PlaylistColumn.Order, true));

            var sorted = PlaylistColumns.Sort(tracks, PlaylistColumn.Title, false);
            var cells = PlaylistColumns.Cells(new[] { PlaylistColumn.Order, PlaylistColumn.Year }, sorted[0].Track, sorted[0].Order);
            Check("cells", cells.Count == 2 && cells[0].Text == "1" && cells[1].Text == "2011", string.Join(" | ", cells.Select(cell => cell.Text)));

            var offered = PlaylistColumns.For(new RemoteList("p", "Mix", tracks));
            Check("offered", offered.Contains(PlaylistColumn.Added) && offered.Contains(PlaylistColumn.Year) && !offered.Contains(PlaylistColumn.Genre),
                string.Join(", ", offered));

            // Saved playlists under PLAYLISTS: sorted inside each playlist, which stays in its place.
            var rows = new List<LibraryItem>
            {
                new("/m/Mango.mp3", "Zest", "/m", day),
                new("/m/Apple.mp3", "Zest", "/m", day.AddDays(1)),
                LibraryItem.Placeholder("Empty", "Nothing here yet"),
                new("/m/Cherry.mp3", "Berries", "/m", day.AddDays(2)),
                new("/m/Banana.mp3", "Berries", "/m", day.AddDays(3)),
            };
            string Rows(LibraryColumn column, bool descending)
            {
                var sorted = rows.ToList();
                var order = SavedPlaylistOrder.Comparer(rows, column, descending);
                sorted.Sort(order.Compare);
                return string.Join(", ", sorted.Select(item => item.IsPlaceholder ? "-" : item.Title));
            }

            Check("saved playlists by title", Rows(LibraryColumn.Title, false) == "Apple, Mango, -, Banana, Cherry", Rows(LibraryColumn.Title, false));
            Check("saved playlists newest first", Rows(LibraryColumn.Added, true) == "Apple, Mango, -, Banana, Cherry", Rows(LibraryColumn.Added, true));
            Check("saved playlists oldest first", Rows(LibraryColumn.Added, false) == "Mango, Apple, -, Cherry, Banana", Rows(LibraryColumn.Added, false));

            var clicks = new List<string>();
            (LibraryColumn? Column, bool Descending) sort = (null, false);
            for (var click = 0; click < 3; click++)
            {
                sort = SavedPlaylistOrder.Next(sort.Column, sort.Descending, LibraryColumn.Bpm);
                clicks.Add(sort.Column is null ? "order" : sort.Descending ? "down" : "up");
            }

            Check("saved playlist heading clicks", string.Join(" ", clicks) == "up down order", string.Join(" ", clicks));
        }
        catch (Exception ex)
        {
            log($"::error::Playlist columns: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }

        return failures;
    }
}
