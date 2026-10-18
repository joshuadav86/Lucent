using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace WinampInspiredVisualizer.Import;

/// <summary>
/// Reads playlists saved by other players: M3U and M3U8 (Winamp, VLC, MusicBee and most others), PLS,
/// XSPF, Windows Media Player's WPL, and foobar2000's own FPL (1.x) and FPLITE (2.x) files.
/// </summary>
public static class PlaylistFileReader
{
    public static IReadOnlyList<string> Extensions { get; } = new[] { ".m3u", ".m3u8", ".pls", ".xspf", ".wpl", ".fpl", ".fplite" };

    public static bool IsPlaylist(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>foobar2000's playlists folders, newest version first, that exist on this computer.</summary>
    public static IReadOnlyList<string> FoobarPlaylistFolders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var folders = new[]
        {
            Path.Combine(appData, "foobar2000-v2", "playlists-v2.0"),
            Path.Combine(appData, "foobar2000", "playlists-v2.0"),
            Path.Combine(appData, "foobar2000", "playlists"),

            // The Mac version keeps its profile in its container.
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "foobar2000-v2", "playlists-v2.0"),
        };
        return folders.Where(Directory.Exists).ToList();
    }

    /// <summary>Reads the playlists in these files, skipping any that can't be read or have no songs.</summary>
    public static ImportedLibrary ReadAll(IEnumerable<string> files, ImportSource source)
    {
        var library = new ImportedLibrary(source);
        foreach (var file in files)
        {
            try
            {
                if (Read(file) is { Tracks.Count: > 0 } playlist)
                {
                    library.Playlists.Add(playlist);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException or InvalidDataException)
            {
                // One damaged playlist doesn't stop the others.
            }
        }

        return library;
    }

    public static ImportedPlaylist? Read(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var tracks = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pls" => ReadPls(ReadText(path), folder),
            ".xspf" => ReadXspf(path, folder, ref name),
            ".wpl" => ReadWpl(path, folder, ref name),
            ".fpl" => ReadFpl(File.ReadAllBytes(path)),
            ".fplite" => ReadM3u(ReadText(path), folder),
            _ => ReadM3u(ReadText(path), folder),
        };

        if (string.Equals(Path.GetExtension(path), ".fplite", StringComparison.OrdinalIgnoreCase))
        {
            name = FoobarPlaylistName(path) ?? name;
        }

        return tracks.Count == 0 ? null : new ImportedPlaylist(name, tracks);
    }

    /// <summary>
    /// UTF-8 if it is (M3U8 always is), otherwise the old Windows code page most M3U files were saved in.
    /// </summary>
    private static string ReadText(string path)
    {
        var bytes = File.ReadAllBytes(path);
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    public static List<ImportedTrack> ReadM3u(string text, string folder)
    {
        var tracks = new List<ImportedTrack>();
        string? artist = null, title = null;
        var seconds = 0;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                // #EXTINF:215,Artist - Title
                var info = line[8..];
                var comma = info.IndexOf(',');
                var length = comma >= 0 ? info[..comma] : info;
                seconds = int.TryParse(length.Split(' ')[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var s) ? Math.Max(0, s) : 0;
                (artist, title) = SplitArtist(comma >= 0 ? info[(comma + 1)..] : null);
                continue;
            }

            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (FileUri.Resolve(line, folder) is { } path)
            {
                tracks.Add(new ImportedTrack { Path = path, Artist = artist, Title = title, Duration = TimeSpan.FromSeconds(seconds) });
            }

            artist = title = null;
            seconds = 0;
        }

        return tracks;
    }

    private static List<ImportedTrack> ReadPls(string text, string folder)
    {
        // [playlist] File1=..., Title1=..., Length1=...
        var files = new SortedDictionary<int, string>();
        var titles = new Dictionary<int, string>();
        var lengths = new Dictionary<int, int>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (Numbered(key, "File") is { } file)
            {
                files[file] = value;
            }
            else if (Numbered(key, "Title") is { } titleNumber)
            {
                titles[titleNumber] = value;
            }
            else if (Numbered(key, "Length") is { } lengthNumber && int.TryParse(value, out var length))
            {
                lengths[lengthNumber] = Math.Max(0, length);
            }
        }

        var tracks = new List<ImportedTrack>();
        foreach (var (number, file) in files)
        {
            if (FileUri.Resolve(file, folder) is { } path)
            {
                var (artist, title) = SplitArtist(titles.GetValueOrDefault(number));
                tracks.Add(new ImportedTrack { Path = path, Artist = artist, Title = title, Duration = TimeSpan.FromSeconds(lengths.GetValueOrDefault(number)) });
            }
        }

        return tracks;
    }

    private static int? Numbered(string key, string prefix) =>
        key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && int.TryParse(key[prefix.Length..], out var number) ? number : null;

    private static List<ImportedTrack> ReadXspf(string path, string folder, ref string name)
    {
        var document = LoadXml(path);
        var root = document.Root;
        if (root?.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value is { Length: > 0 } title)
        {
            name = title.Trim();
        }

        var tracks = new List<ImportedTrack>();
        foreach (var track in document.Descendants().Where(e => e.Name.LocalName == "track"))
        {
            string? Child(string child) => track.Elements().FirstOrDefault(e => e.Name.LocalName == child)?.Value.Trim() is { Length: > 0 } v ? v : null;
            if (FileUri.Resolve(Child("location"), folder) is { } file)
            {
                var duration = long.TryParse(Child("duration"), out var ms) ? TimeSpan.FromMilliseconds(Math.Max(0, ms)) : TimeSpan.Zero;
                tracks.Add(new ImportedTrack { Path = file, Title = Child("title"), Artist = Child("creator"), Album = Child("album"), Duration = duration });
            }
        }

        return tracks;
    }

    private static List<ImportedTrack> ReadWpl(string path, string folder, ref string name)
    {
        // <smil><head><title>…</title></head><body><seq><media src="..\Music\song.mp3"/></seq></body></smil>
        var document = LoadXml(path);
        if (document.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value is { Length: > 0 } title)
        {
            name = title.Trim();
        }

        return document.Descendants()
            .Where(e => e.Name.LocalName == "media")
            .Select(e => FileUri.Resolve(e.Attribute("src")?.Value, folder))
            .OfType<string>()
            .Select(file => new ImportedTrack { Path = file })
            .ToList();
    }

    private static XDocument LoadXml(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(reader);
    }

    /// <summary>
    /// foobar2000 1.x playlists are binary, but each song's address is in them as a UTF-8 string ending in a
    /// zero byte, "file://C:\Music\song.flac", in the playlist's order. Reading those is enough.
    /// </summary>
    public static List<ImportedTrack> ReadFpl(byte[] data)
    {
        var tracks = new List<ImportedTrack>();
        var marker = "file://"u8;
        var span = data.AsSpan();
        var start = 0;
        while (start < span.Length)
        {
            var found = span[start..].IndexOf(marker);
            if (found < 0)
            {
                break;
            }

            var begin = start + found;
            var length = span[begin..].IndexOf((byte)0);
            var end = length < 0 ? span.Length : begin + length;
            if (FileUri.ToPath(Encoding.UTF8.GetString(span[begin..end])) is { } path)
            {
                tracks.Add(new ImportedTrack { Path = path });
            }

            start = end + 1;
        }

        return tracks;
    }

    /// <summary>
    /// foobar2000 2.x names its playlist files by an id and lists their names in index.txt beside them.
    /// Its exact layout isn't documented, so this looks for the line that mentions the file's id and takes
    /// the rest of the line as the name.
    /// </summary>
    private static string? FoobarPlaylistName(string path)
    {
        try
        {
            var index = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? string.Empty, "index.txt");
            if (!File.Exists(index))
            {
                return null;
            }

            var id = Path.GetFileNameWithoutExtension(path);
            if (id.StartsWith("playlist-", StringComparison.OrdinalIgnoreCase))
            {
                id = id["playlist-".Length..];
            }

            foreach (var line in File.ReadLines(index, Encoding.UTF8))
            {
                var at = line.IndexOf(id, StringComparison.OrdinalIgnoreCase);
                if (at < 0)
                {
                    continue;
                }

                var rest = (line[..at] + line[(at + id.Length)..]).Trim().Trim(':', '=', '\t', ',', '{', '}', ' ', '"').Trim();
                rest = rest.Replace(".fplite", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("playlist-", string.Empty, StringComparison.OrdinalIgnoreCase).Trim(':', '=', '\t', ' ');
                return rest.Length > 0 ? rest : null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The id stands in for the name.
        }

        return null;
    }

    /// <summary>"Artist - Title" split in two. Without the dash it's all title.</summary>
    public static (string? Artist, string? Title) SplitArtist(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return (null, null);
        }

        var dash = text.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? (text[..dash].Trim(), text[(dash + 3)..].Trim()) : (null, text.Trim());
    }
}
