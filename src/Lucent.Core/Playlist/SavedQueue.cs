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
            // Only files come back; a stream address has nothing to reopen.
            if (!Path.IsPathRooted(tracks[i].Path))
            {
                continue;
            }

            if (i == current)
            {
                text.Append(CurrentMark).Append('\n');
            }

            text.Append(tracks[i].Path).Append('\n');
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
    /// The saved queue's files that still exist, and which of them was playing (-1 if none, or it's gone).
    /// Empty if nothing was saved.
    /// </summary>
    public static (List<string> Files, int Current) Read()
    {
        var files = new List<string>();
        var current = -1;
        try
        {
            if (!File.Exists(FilePath))
            {
                return (files, current);
            }

            var marked = false;
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
                    continue;
                }

                if (Path.IsPathRooted(line) && File.Exists(line))
                {
                    if (marked)
                    {
                        current = files.Count;
                    }

                    files.Add(line);
                }

                marked = false;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Start with an empty queue.
        }

        return (files, current);
    }
}
