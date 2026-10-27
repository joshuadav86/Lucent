using System.IO;
using System.Text;
using Lucent.Settings;

namespace Lucent.Playlist;

/// <summary>
/// Music playlists the user has saved, kept as standard .m3u8 files in %AppData%\Lucent\Playlists,
/// so other players (Winamp included) can open them too.
/// </summary>
public static class SavedPlaylists
{
    public static string Folder => Path.Combine(AppDataFolder.Path, "Playlists");

    /// <summary>The saved playlists' names, alphabetically.</summary>
    public static IReadOnlyList<string> Names()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return Array.Empty<string>();
            }

            return Directory.EnumerateFiles(Folder, "*.m3u8")
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }
    }

    public static bool Exists(string name) => File.Exists(PathOf(name));

    /// <summary>The files in a playlist, in order, leaving out any that no longer exist.</summary>
    public static List<string> Read(string name)
    {
        var path = PathOf(name);
        var folder = Path.GetDirectoryName(path)!;
        var files = new List<string>();
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var file = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(folder, line));
            if (File.Exists(file))
            {
                files.Add(file);
            }
        }

        return files;
    }

    /// <summary>Saves (or replaces) a playlist.</summary>
    public static void Write(string name, IEnumerable<Track> tracks)
    {
        Directory.CreateDirectory(Folder);
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var track in tracks)
        {
            var seconds = track.Duration > TimeSpan.Zero ? (int)Math.Round(track.Duration.TotalSeconds) : -1;
            var title = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";
            text.Append("#EXTINF:").Append(seconds).Append(',').Append(title).Append('\n');
            text.Append(track.Path).Append('\n');
        }

        var path = PathOf(name);
        var temporary = path + ".saving";
        File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>Adds songs to the end of a playlist, creating it if it's new.</summary>
    public static void Append(string name, IReadOnlyList<Track> tracks)
    {
        var existing = Exists(name) ? Read(name).Select(Track.FromFile).ToList() : new List<Track>();
        existing.AddRange(tracks);
        Write(name, existing);
    }

    /// <summary>
    /// Points every saved playlist's songs at new files (a duplicate that was removed, to the copy that was
    /// kept), leaving the rest of each playlist as it was.
    /// </summary>
    public static void ReplacePaths(IReadOnlyDictionary<string, string> moves)
    {
        if (moves.Count == 0 || !Directory.Exists(Folder))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(Folder, "*.m3u8"))
        {
            try
            {
                var lines = File.ReadAllLines(path, Encoding.UTF8);
                var changed = false;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (moves.TryGetValue(lines[i].Trim(), out var replacement))
                    {
                        lines[i] = replacement;
                        changed = true;
                    }
                }

                if (changed)
                {
                    var temporary = path + ".saving";
                    File.WriteAllText(temporary, string.Join('\n', lines) + "\n", new UTF8Encoding(false));
                    File.Move(temporary, path, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A playlist that can't be rewritten just leaves the removed copy out when it's next opened.
            }
        }
    }

    public static void Delete(string name) => File.Delete(PathOf(name));

    public static void Rename(string name, string newName) => File.Move(PathOf(name), PathOf(newName));

    /// <summary>A name with the characters Windows doesn't allow in file names removed, or "" if nothing's left.</summary>
    public static string CleanName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim().TrimEnd('.');
    }

    private static string PathOf(string name) => Path.Combine(Folder, CleanName(name) + ".m3u8");
}
