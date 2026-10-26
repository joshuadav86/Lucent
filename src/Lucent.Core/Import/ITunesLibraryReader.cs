using System.Globalization;
using System.IO;
using System.Xml;

namespace Lucent.Import;

/// <summary>
/// Reads the library file iTunes and Apple Music export ("iTunes Library.xml", or File > Library >
/// Export Library in Music on a Mac), which MusicBee can also write ("iTunes Music Library.xml"). It's a
/// property list: every song with its play count, rating and file, then the playlists by song id. Big
/// libraries make big files, so it's read a song at a time rather than all at once.
/// </summary>
public static class ITunesLibraryReader
{
    /// <summary>Where the library file usually is, or null if none of them exist.</summary>
    public static string? FindLibraryFile(ImportSource source)
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(music))
        {
            music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        }

        var candidates = source == ImportSource.MusicBee
            ? new[]
            {
                Path.Combine(music, "MusicBee", "iTunes Music Library.xml"),
                Path.Combine(music, "MusicBee", "iTunes Library.xml"),
            }
            : new[]
            {
                Path.Combine(music, "iTunes", "iTunes Library.xml"),
                Path.Combine(music, "iTunes", "iTunes Music Library.xml"),
                Path.Combine(music, "Music", "Library.xml"),
                Path.Combine(music, "Library.xml"),
            };
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Where to start looking when asking for the file.</summary>
    public static string LikelyFolder(ImportSource source)
    {
        var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(music))
        {
            music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
        }

        var folder = Path.Combine(music, source == ImportSource.MusicBee ? "MusicBee" : "iTunes");
        return Directory.Exists(folder) ? folder : music;
    }

    public static ImportedLibrary Read(string path, ImportSource source = ImportSource.AppleMusic)
    {
        using var stream = File.OpenRead(path);
        return Read(stream, source);
    }

    public static ImportedLibrary Read(Stream stream, ImportSource source = ImportSource.AppleMusic)
    {
        var library = new ImportedLibrary(source);
        var byId = new Dictionary<string, ImportedTrack>(StringComparer.Ordinal);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreWhitespace = true, IgnoreComments = true };
        using var reader = XmlReader.Create(stream, settings);

        if (!reader.ReadToFollowing("plist") || !reader.ReadToDescendant("dict"))
        {
            throw new InvalidDataException("This isn't an iTunes or Apple Music library file.");
        }

        var sawTracks = false;
        foreach (var key in DictKeys(reader))
        {
            if (key == "Tracks" && reader.NodeType == XmlNodeType.Element && reader.Name == "dict")
            {
                sawTracks = true;
                foreach (var id in DictKeys(reader))
                {
                    if (ReadValue(reader) is Dictionary<string, object> fields && ToTrack(fields) is { } track)
                    {
                        byId[id] = track;
                        library.Tracks.Add(track);
                    }
                }
            }
            else if (key == "Playlists" && reader.NodeType == XmlNodeType.Element && reader.Name == "array")
            {
                foreach (var playlist in ArrayItems(reader))
                {
                    if (playlist is Dictionary<string, object> fields && ToPlaylist(fields, byId) is { } list)
                    {
                        library.Playlists.Add(list);
                    }
                }
            }
            else
            {
                Skip(reader);
            }
        }

        if (!sawTracks)
        {
            throw new InvalidDataException("This isn't an iTunes or Apple Music library file.");
        }

        return library;
    }

    private static ImportedTrack? ToTrack(Dictionary<string, object> fields)
    {
        // Videos, podcasts and audiobooks aren't music for the library.
        if (Bool(fields, "Has Video") || Bool(fields, "Movie") || Bool(fields, "TV Show") || Bool(fields, "Podcast")
            || Bool(fields, "Music Video") || Text(fields, "Kind")?.Contains("audiobook", StringComparison.OrdinalIgnoreCase) == true)
        {
            return null;
        }

        // A rating Apple Music worked out from the album's isn't the user's own.
        var rating = Bool(fields, "Rating Computed") ? 0 : ImportedTrack.StarsFromPercent(Number(fields, "Rating"));
        return new ImportedTrack
        {
            Path = FileUri.ToPath(Text(fields, "Location")),
            Title = Text(fields, "Name"),
            Artist = Text(fields, "Artist") ?? Text(fields, "Album Artist"),
            Album = Text(fields, "Album"),
            Duration = TimeSpan.FromMilliseconds(Number(fields, "Total Time")),
            Plays = (int)Math.Max(0, Number(fields, "Play Count")),
            Rating = rating,
            LastPlayed = fields.TryGetValue("Play Date UTC", out var date) && date is DateTime played ? played : null,
            Loved = Bool(fields, "Loved") || Bool(fields, "Favorited"),
        };
    }

    private static ImportedPlaylist? ToPlaylist(Dictionary<string, object> fields, Dictionary<string, ImportedTrack> byId)
    {
        // The whole library ("Library", "Music") and the built-in lists aren't playlists the user made,
        // and folders only hold other playlists.
        if (Bool(fields, "Master") || fields.ContainsKey("Distinguished Kind") || Bool(fields, "Folder")
            || (fields.TryGetValue("Visible", out var visible) && visible is false)
            || Text(fields, "Name") is not { } name
            || fields.GetValueOrDefault("Playlist Items") is not List<object> items)
        {
            return null;
        }

        var tracks = items.OfType<Dictionary<string, object>>()
            .Select(item => item.GetValueOrDefault("Track ID")?.ToString())
            .Select(id => id is not null && byId.TryGetValue(id, out var track) ? track : null)
            .OfType<ImportedTrack>()
            .ToList();
        return tracks.Count == 0 ? null : new ImportedPlaylist(name, tracks);
    }

    private static string? Text(Dictionary<string, object> fields, string key) =>
        fields.GetValueOrDefault(key) is string text && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    private static double Number(Dictionary<string, object> fields, string key) => fields.GetValueOrDefault(key) switch
    {
        long number => number,
        double number => number,
        string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) => number,
        _ => 0,
    };

    private static bool Bool(Dictionary<string, object> fields, string key) => fields.GetValueOrDefault(key) is true;

    // ---- Property lists ----
    // A <dict> is <key> then value, over and over; an <array> is values. Both readers below leave the reader
    // on each value in turn and expect it to be read (or skipped) before asking for the next.

    /// <summary>The keys of the dict the reader is on, each time leaving the reader on that key's value.</summary>
    private static IEnumerable<string> DictKeys(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            yield break;
        }

        var depth = reader.Depth;
        reader.Read();
        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) && !reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element && reader.Name == "key")
            {
                var key = reader.ReadElementContentAsString();
                MoveToContent(reader);
                yield return key;
            }
            else
            {
                reader.Read();
            }
        }

        reader.Read();
    }

    private static IEnumerable<object?> ArrayItems(XmlReader reader)
    {
        if (reader.IsEmptyElement)
        {
            reader.Read();
            yield break;
        }

        var depth = reader.Depth;
        reader.Read();
        while (!(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth) && !reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                yield return ReadValue(reader);
            }
            else
            {
                reader.Read();
            }
        }

        reader.Read();
    }

    /// <summary>Reads the value the reader is on and moves past it.</summary>
    private static object? ReadValue(XmlReader reader)
    {
        switch (reader.Name)
        {
            case "dict":
                var dict = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var key in DictKeys(reader))
                {
                    if (ReadValue(reader) is { } value)
                    {
                        dict[key] = value;
                    }
                }

                return dict;
            case "array":
                return ArrayItems(reader).OfType<object>().ToList();
            case "true":
                Skip(reader);
                return true;
            case "false":
                Skip(reader);
                return false;
            case "integer":
                var whole = reader.ReadElementContentAsString();
                return long.TryParse(whole, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer) ? integer : 0L;
            case "real":
                var real = reader.ReadElementContentAsString();
                return double.TryParse(real, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0.0;
            case "date":
                var date = reader.ReadElementContentAsString();
                return DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
                    ? time
                    : null;
            case "string":
                return reader.ReadElementContentAsString();
            default:
                Skip(reader);
                return null;
        }
    }

    private static void Skip(XmlReader reader)
    {
        reader.Skip();
        MoveToContent(reader);
    }

    private static void MoveToContent(XmlReader reader)
    {
        while (!reader.EOF && reader.NodeType is not (XmlNodeType.Element or XmlNodeType.EndElement))
        {
            reader.Read();
        }
    }
}
