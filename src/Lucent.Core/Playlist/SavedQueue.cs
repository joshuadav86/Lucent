using System.IO;
using System.Text;
using Lucent.Settings;

namespace Lucent.Playlist;

/// <summary>
/// The queue as it was when Lucent last closed, so it's still there on the next start. Kept as Queue.m3u8 in
/// the app data folder, with a "#LUCENT-CURRENT" line before the song that was playing.
/// </summary>
public static class SavedQueue
{
    private const string CurrentMark = "#LUCENT-CURRENT";

    public static string FilePath => Path.Combine(AppDataFolder.Path, "Queue.m3u8");

    /// <summary>Saves the queue's files in order, marking <paramref name="current"/> (-1 for none).</summary>
    public static void Write(IReadOnlyList<Track> tracks, int current)
    {
        var text = new StringBuilder("#EXTM3U\n");
        for (var i = 0; i < tracks.Count; i++)
        {
            // Files and Spotify songs come back; a radio station's address has nothing to reopen.
            if (!Path.IsPathRooted(tracks[i].Path) && !tracks[i].IsStreamed)
            {
                continue;
            }

            if (i == current)
            {
                text.Append(CurrentMark).Append('\n');
            }

            SavedPlaylists.AppendEntry(text, tracks[i]);
        }

        try
        {
            Directory.CreateDirectory(AppDataFolder.Path);
            var temporary = FilePath + ".saving";
            File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The queue just isn't remembered this time.
        }
    }

    /// <summary>
    /// The saved queue's songs (its files that still exist, and its Spotify songs), and which of them was playing (-1 if
    /// none, or it's gone). Empty if nothing was saved.
    /// </summary>
    public static (List<SavedSong> Songs, int Current) Read()
    {
        var files = new List<SavedSong>();
        var current = -1;
        try
        {
            if (!File.Exists(FilePath))
            {
                return (files, current);
            }

            var marked = false;
            var notes = new EntryNotes();
            foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
            {
                var line = raw.Trim();
                if (line == CurrentMark)
                {
                    marked = true;
                    continue;
                }

                if (line.Length == 0 || line.StartsWith('#'))
                {
                    notes.Take(line);
                    continue;
                }

                var streamed = notes.Song(line);
                if (streamed is not null || (Path.IsPathRooted(line) && File.Exists(line)))
                {
                    if (marked)
                    {
                        current = files.Count;
                    }

                    files.Add(new SavedSong(line, streamed));
                }

                marked = false;
                notes = new EntryNotes();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Start with an empty queue.
        }

        return (files, current);
    }
}
