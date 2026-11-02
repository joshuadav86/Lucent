using System.Text;
using Lucent.Playlist;

namespace Lucent.Streaming;

/// <summary>
/// Checks Spotify songs in Lucent's playlists and queue: that they're saved and read back as they were, and when a song
/// handed to Spotify counts as finished (<see cref="StreamedSong.HasEnded"/>). Both apps run it in their self-tests.
/// </summary>
public static class StreamedSongSelfTest
{
    /// <summary>Returns the number of problems.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Check(string what, bool ok, string got)
        {
            if (!ok)
            {
                log($"::error::Spotify songs in playlists: {what}: got {got}.");
                failures++;
            }
        }

        try
        {
            // Saved and read back, beside a file.
            var song = Track.ForSpotify("spotify:track:4uLU6hMCjMI75M1A2tKUQC", "Never Gonna - Give You Up", "Rick Astley",
                "Whenever You Need Somebody", TimeSpan.FromSeconds(213), "https://i.scdn.co/image/small");
            var text = new StringBuilder();
            SavedPlaylists.AppendEntry(text, song);
            var notes = new EntryNotes();
            Track? read = null;
            foreach (var line in text.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.StartsWith('#'))
                {
                    notes.Take(line);
                }
                else
                {
                    read = notes.Song(line);
                }
            }

            Check("read back", read is { IsStreamed: true, Title: "Never Gonna - Give You Up", Artist: "Rick Astley", Album: "Whenever You Need Somebody" }
                    && read.Duration == TimeSpan.FromSeconds(213) && read.CoverUrl == song.CoverUrl && read.Path == song.Path,
                read is null ? "nothing" : $"{read.Path} | {read.Title} | {read.Artist} | {read.Album} | {read.Duration} | {read.CoverUrl}");
            Check("a file isn't a Spotify song", new EntryNotes().Song(@"C:\Music\Song.mp3") is null && !Track.IsStreamedPath("/Users/me/Music/a.flac"),
                "a Spotify song");

            var remote = new RemoteTrack("4uLU6hMCjMI75M1A2tKUQC", "Song", "Artist", "Album", TimeSpan.FromMinutes(3), Uri: "spotify:track:4uLU6hMCjMI75M1A2tKUQC");
            Check("Spotify song to track", StreamedSong.TrackOf(remote) is { IsStreamed: true } && StreamedSong.TrackOf(remote with { Uri = null }) is null,
                "wrong");

            // Following the song in Spotify.
            var entry = new PlaylistEntry(Track.ForSpotify("spotify:track:abc", "Ours", "Band", string.Empty, TimeSpan.FromMinutes(3), null));
            var now = DateTime.UtcNow;
            var ours = new RemoteTrack("abc", "Ours", "Band", string.Empty, TimeSpan.FromMinutes(3), Uri: "spotify:track:abc");
            var before = new RemoteTrack("xyz", "Before", "Someone", string.Empty, TimeSpan.FromMinutes(4));
            RemoteState At(RemoteTrack track, bool playing, double seconds) => new(track, playing, TimeSpan.FromSeconds(seconds), now);

            var followed = new StreamedSong(entry);
            Check("old song still named", !followed.HasEnded(At(before, true, 100)), "ended");
            Check("starts", !followed.HasEnded(At(ours, true, 1)), "ended");
            Check("paused midway", !followed.HasEnded(At(ours, false, 60)), "ended");
            Check("near its end", followed.HasEnded(At(ours, true, 179.8)), "not ended");
            Check("only once", !followed.HasEnded(At(ours, true, 179.9)), "ended twice");

            followed = new StreamedSong(entry);
            followed.HasEnded(At(ours, true, 5));
            Check("Spotify moved on", followed.HasEnded(At(before, true, 2)), "not ended");

            followed = new StreamedSong(entry);
            followed.HasEnded(At(ours, true, 5));
            followed.HasEnded(At(ours, true, 178.5));
            Check("stopped back at its start", followed.HasEnded(At(ours, false, 0)), "not ended");

            followed = new StreamedSong(entry);
            followed.HasEnded(At(ours, true, 5));
            Check("rewound and paused early on", !followed.HasEnded(At(ours, false, 0)), "ended");
        }
        catch (Exception ex)
        {
            log($"::error::Spotify songs in playlists: {ex.GetType().Name}: {ex.Message}");
            failures++;
        }

        return failures;
    }
}
