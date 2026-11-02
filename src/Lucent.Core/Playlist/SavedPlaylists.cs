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

    /// <summary>
    /// The songs in a playlist, in order: its files that still exist, and its Spotify songs (see <see cref="Track.ForSpotify"/>).
    /// </summary>
    public static List<SavedSong> Read(string name)
    {
        var path = PathOf(name);
        var folder = Path.GetDirectoryName(path)!;
        var songs = new List<SavedSong>();
        var notes = new EntryNotes();
        foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                notes.Take(line);
                continue;
            }

            if (notes.Song(line) is { } streamed)
            {
                songs.Add(new SavedSong(line, streamed));
            }
            else
            {
                var file = Path.IsPathRooted(line) ? line : Path.GetFullPath(Path.Combine(folder, line));
                if (File.Exists(file))
                {
                    songs.Add(new SavedSong(file, null));
                }
            }

            notes = new EntryNotes();
        }

        return songs;
    }

    /// <summary>Saves (or replaces) a playlist.</summary>
    public static void Write(string name, IEnumerable<Track> tracks)
    {
        Directory.CreateDirectory(Folder);
        var text = new StringBuilder("#EXTM3U\n");
        foreach (var track in tracks)
        {
            AppendEntry(text, track);
        }

        var path = PathOf(name);
        var temporary = path + ".saving";
        File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// A song's lines in a .m3u8: its #EXTINF (length and "Artist - Title"), then the file. A Spotify song gets its
    /// artist, album and cover on lines of their own first (#EXTART, #EXTALB, #EXTIMG), so Lucent can show it again
    /// without asking Spotify; other players skip those lines, and the spotify: address they can't play.
    /// </summary>
    internal static void AppendEntry(StringBuilder text, Track track)
    {
        var seconds = track.Duration > TimeSpan.Zero ? (int)Math.Round(track.Duration.TotalSeconds) : -1;
        var title = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";
        text.Append("#EXTINF:").Append(seconds).Append(',').Append(OneLine(title)).Append('\n');
        if (track.IsStreamed)
        {
            AppendNote(text, "#EXTART:", track.Artist);
            AppendNote(text, "#EXTALB:", track.Album);
            AppendNote(text, "#EXTIMG:", track.CoverUrl);
        }

        text.Append(track.Path).Append('\n');
    }

    private static void AppendNote(StringBuilder text, string tag, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(tag).Append(OneLine(value)).Append('\n');
        }
    }

    private static string OneLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Trim();

    /// <summary>Adds songs to the end of a playlist, creating it if it's new.</summary>
    public static void Append(string name, IReadOnlyList<Track> tracks)
    {
        var existing = Exists(name) ? Read(name).Select(song => song.ToTrack()).ToList() : new List<Track>();
        existing.AddRange(tracks);
        Write(name, existing);
    }

    /// <summary>The row an empty playlist shows under PLAYLISTS, saying how it gets its songs.</summary>
    public const string EmptyHint = "Empty. Right-click here and choose Add songs, or drop music files here.";

    /// <summary>What the New playlist box says about filling it.</summary>
    public const string NewHint = "It starts empty. Right-click it and choose Add songs, drop music files on it, or right-click songs anywhere and choose Add to playlist.";

    /// <summary>What's said once songs have gone into a playlist, "Added 3 songs to Road trip".</summary>
    public static string AddedNote(int count, string name) =>
        count == 1 ? $"Added 1 song to {name}" : $"Added {count:N0} songs to {name}";

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

/// <summary>
/// A song in a saved playlist or the saved queue: a file's path, or a Spotify song's address with what the playlist says
/// of it (<paramref name="Streamed"/>).
/// </summary>
public readonly record struct SavedSong(string Path, Track? Streamed)
{
    /// <summary>The song as a track: the Spotify song as saved, or the file with its tags read (which takes a moment).</summary>
    public Track ToTrack() => Streamed ?? Track.FromFile(Path);
}

/// <summary>What the # lines before a song in a .m3u8 say of it (see <see cref="SavedPlaylists.AppendEntry"/>).</summary>
internal sealed class EntryNotes
{
    private int _seconds = -1;
    private string _info = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private string? _cover;

    /// <summary>Notes a # line, leaving out any Lucent doesn't read.</summary>
    public void Take(string line)
    {
        if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
        {
            var info = line[8..];
            var comma = info.IndexOf(',');
            _seconds = comma > 0 && int.TryParse(info[..comma].Trim(), out var seconds) ? seconds : -1;
            _info = comma >= 0 ? info[(comma + 1)..].Trim() : string.Empty;
        }
        else if (line.StartsWith("#EXTART:", StringComparison.OrdinalIgnoreCase))
        {
            _artist = line[8..].Trim();
        }
        else if (line.StartsWith("#EXTALB:", StringComparison.OrdinalIgnoreCase))
        {
            _album = line[8..].Trim();
        }
        else if (line.StartsWith("#EXTIMG:", StringComparison.OrdinalIgnoreCase))
        {
            // Only a web address: the list downloads it.
            var cover = line[8..].Trim();
            _cover = cover.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? cover : null;
        }
    }

    /// <summary>The Spotify song at <paramref name="path"/>, as the notes describe it, or null when it's a file.</summary>
    public Track? Song(string path)
    {
        if (!Track.IsStreamedPath(path))
        {
            return null;
        }

        // #EXTINF has "Artist - Title"; the artist on its own line says where the title starts.
        var title = _artist.Length > 0 && _info.StartsWith(_artist + " - ", StringComparison.Ordinal) ? _info[(_artist.Length + 3)..] : _info;
        if (title.Length == 0)
        {
            title = "Spotify song";
        }

        var duration = _seconds > 0 ? TimeSpan.FromSeconds(_seconds) : TimeSpan.Zero;
        return Track.ForSpotify(path, title, _artist, _album, duration, _cover);
    }
}
