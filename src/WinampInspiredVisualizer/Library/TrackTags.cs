using System.Globalization;
using System.IO;
using TagLib;

namespace WinampInspiredVisualizer.Library;

/// <summary>The bits of a track's info that can be edited in the track info window.</summary>
public enum TagField
{
    Title,
    Artist,
    Album,
    AlbumArtist,
    Year,
    Track,
    TrackCount,
    Disc,
    DiscCount,
    Genre,
    Composer,
    Bpm,
    Comment,
    Lyrics,
}

public static class TagFields
{
    public static IReadOnlyList<TagField> All { get; } = Enum.GetValues<TagField>();

    /// <summary>Fields that hold a whole number (0 or blank means "not set").</summary>
    public static bool IsNumber(TagField field) =>
        field is TagField.Year or TagField.Track or TagField.TrackCount or TagField.Disc or TagField.DiscCount or TagField.Bpm;

    /// <summary>Fields that can hold several names, typed with semicolons between them ("Artist A; Artist B").</summary>
    public static bool IsList(TagField field) =>
        field is TagField.Artist or TagField.AlbumArtist or TagField.Genre or TagField.Composer;

    /// <summary>The name used in messages, like "Album artist".</summary>
    public static string Label(TagField field) => field switch
    {
        TagField.AlbumArtist => "Album artist",
        TagField.Track => "Track number",
        TagField.TrackCount => "Number of tracks",
        TagField.Disc => "Disc number",
        TagField.DiscCount => "Number of discs",
        TagField.Bpm => "BPM",
        _ => field.ToString(),
    };

    /// <summary>Splits "A; B" into its names, dropping blanks.</summary>
    public static string[] SplitList(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string JoinList(IEnumerable<string>? names) =>
        names is null ? string.Empty : string.Join("; ", names.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()));

    public static string NumberText(uint value) => value == 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads a number typed by the user; blank means 0 ("not set"). Returns false for anything else.</summary>
    public static bool TryParseNumber(string text, out uint value)
    {
        value = 0;
        var trimmed = text.Trim();
        return trimmed.Length == 0 || uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>The same text the way it would read back from the file, for comparing.</summary>
    public static string Normalize(TagField field, string text)
    {
        if (IsNumber(field))
        {
            return TryParseNumber(text, out var number) ? NumberText(number) : text.Trim();
        }

        return IsList(field) ? JoinList(SplitList(text)) : text.Trim().ReplaceLineEndings("\n");
    }
}

/// <summary>One picture stored inside a music file, such as its front cover.</summary>
public sealed record TagPicture(PictureType Type, string MimeType, string Description, byte[] Data)
{
    public string TypeLabel => Type switch
    {
        PictureType.FrontCover => "Front cover",
        PictureType.BackCover => "Back cover",
        PictureType.Media => "Disc",
        PictureType.LeafletPage => "Booklet page",
        PictureType.Artist or PictureType.LeadArtist => "Artist",
        PictureType.Band => "Band",
        PictureType.Illustration => "Illustration",
        PictureType.BandLogo => "Logo",
        PictureType.FileIcon or PictureType.OtherFileIcon => "Icon",
        _ => "Picture",
    };

    public static TagPicture From(IPicture picture) => new(
        picture.Type,
        string.IsNullOrEmpty(picture.MimeType) ? ImageFormats.MimeTypeOf(picture.Data.Data) : picture.MimeType,
        picture.Description ?? string.Empty,
        picture.Data.Data);

    public IPicture ToTagLib() => new Picture(new ByteVector(Data))
    {
        Type = Type,
        MimeType = MimeType,
        Description = Description,
    };

    /// <summary>True when both lists hold the same pictures in the same order.</summary>
    public static bool SameList(IReadOnlyList<TagPicture> a, IReadOnlyList<TagPicture> b) =>
        a.Count == b.Count && a.Zip(b).All(pair => pair.First.Type == pair.Second.Type && pair.First.Data.AsSpan().SequenceEqual(pair.Second.Data));
}

/// <summary>Facts about a music file that can't be edited: its format, quality, length and size.</summary>
public sealed record FileDetails(
    string Format,
    string Codec,
    string TagFormat,
    int BitrateKbps,
    int SampleRate,
    int BitsPerSample,
    int Channels,
    TimeSpan Duration,
    long SizeBytes,
    DateTime Modified);

/// <summary>Everything the track info window shows for one file, read with TagLib.</summary>
public sealed class TrackTags
{
    private TrackTags(string path, IReadOnlyDictionary<TagField, string> values, IReadOnlyList<TagPicture> pictures, FileDetails details)
    {
        Path = path;
        Values = values;
        Pictures = pictures;
        Details = details;
    }

    public string Path { get; }

    /// <summary>Every field as text: numbers in plain digits, lists joined with "; ", blank when not set.</summary>
    public IReadOnlyDictionary<TagField, string> Values { get; }

    public IReadOnlyList<TagPicture> Pictures { get; }

    public FileDetails Details { get; }

    /// <summary>Reads a file's tags, pictures and details. Throws if TagLib can't open it.</summary>
    public static TrackTags Read(string path)
    {
        using var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), ReadStyle.Average);
        var tag = file.Tag;
        var values = new Dictionary<TagField, string>
        {
            [TagField.Title] = tag.Title ?? string.Empty,
            [TagField.Artist] = TagFields.JoinList(tag.Performers),
            [TagField.Album] = tag.Album ?? string.Empty,
            [TagField.AlbumArtist] = TagFields.JoinList(tag.AlbumArtists),
            [TagField.Year] = TagFields.NumberText(tag.Year),
            [TagField.Track] = TagFields.NumberText(tag.Track),
            [TagField.TrackCount] = TagFields.NumberText(tag.TrackCount),
            [TagField.Disc] = TagFields.NumberText(tag.Disc),
            [TagField.DiscCount] = TagFields.NumberText(tag.DiscCount),
            [TagField.Genre] = TagFields.JoinList(tag.Genres),
            [TagField.Composer] = TagFields.JoinList(tag.Composers),
            [TagField.Bpm] = TagFields.NumberText(tag.BeatsPerMinute),
            [TagField.Comment] = (tag.Comment ?? string.Empty).ReplaceLineEndings("\n"),
            [TagField.Lyrics] = (tag.Lyrics ?? string.Empty).ReplaceLineEndings("\n"),
        };

        var pictures = (tag.Pictures ?? Array.Empty<IPicture>())
            .Where(picture => picture.Data is { Count: > 0 })
            .Select(TagPicture.From)
            .ToList();

        var info = new FileInfo(path);
        var properties = file.Properties;
        var codecs = properties?.Codecs.OfType<ICodec>().Select(codec => codec.Description).Where(text => !string.IsNullOrWhiteSpace(text)).ToList() ?? new List<string>();
        var details = new FileDetails(
            FormatName(path, codecs),
            string.Join(", ", codecs),
            TagFormatName(file),
            properties?.AudioBitrate ?? 0,
            properties?.AudioSampleRate ?? 0,
            properties?.BitsPerSample ?? 0,
            properties?.AudioChannels ?? 0,
            properties?.Duration ?? TimeSpan.Zero,
            info.Length,
            info.LastWriteTime);

        return new TrackTags(path, values, pictures, details);
    }

    /// <summary>"MP3", "FLAC", "Apple Lossless (M4A)" and so on, from the extension and codec.</summary>
    private static string FormatName(string path, IReadOnlyList<string> codecs)
    {
        var codec = string.Join(" ", codecs);
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "MP3",
            ".flac" => "FLAC",
            ".ogg" or ".oga" when codec.Contains("Opus", StringComparison.OrdinalIgnoreCase) => "Opus (Ogg)",
            ".ogg" or ".oga" => "Ogg Vorbis",
            ".opus" => "Opus",
            ".m4a" or ".mp4" when codec.Contains("alac", StringComparison.OrdinalIgnoreCase) => "Apple Lossless (M4A)",
            ".m4a" or ".mp4" => "AAC (M4A)",
            ".aac" => "AAC",
            ".wma" => "Windows Media Audio",
            ".wav" => "WAV",
            ".aif" or ".aiff" or ".aifc" => "AIFF",
            var other => other.TrimStart('.').ToUpperInvariant(),
        };
    }

    /// <summary>Which kind of tags the file has, like "ID3v2.3" or "Vorbis comments".</summary>
    private static string TagFormatName(TagLib.File file)
    {
        var names = new List<string>();
        var types = file.TagTypes;
        if (types.HasFlag(TagTypes.Id3v2))
        {
            names.Add(file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3 ? $"ID3v2.{id3.Version}" : "ID3v2");
        }

        if (types.HasFlag(TagTypes.Xiph))
        {
            names.Add("Vorbis comments");
        }

        if (types.HasFlag(TagTypes.Apple))
        {
            names.Add("iTunes-style");
        }

        if (types.HasFlag(TagTypes.Asf))
        {
            names.Add("Windows Media");
        }

        if (types.HasFlag(TagTypes.RiffInfo))
        {
            names.Add("RIFF INFO");
        }

        if (types.HasFlag(TagTypes.Ape))
        {
            names.Add("APE");
        }

        if (types.HasFlag(TagTypes.Id3v1))
        {
            names.Add("ID3v1");
        }

        return names.Count == 0 ? "None yet" : string.Join(" + ", names);
    }
}

/// <summary>Recognises image files from their first bytes, so a picture gets the right type and extension.</summary>
public static class ImageFormats
{
    public static string MimeTypeOf(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G')
        {
            return "image/png";
        }

        if (data.Length >= 4 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
        {
            return "image/gif";
        }

        if (data.Length >= 2 && data[0] == 'B' && data[1] == 'M')
        {
            return "image/bmp";
        }

        if (data.Length >= 4 && ((data[0] == 'I' && data[1] == 'I' && data[2] == 42) || (data[0] == 'M' && data[1] == 'M' && data[3] == 42)))
        {
            return "image/tiff";
        }

        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return "image/webp";
        }

        return "image/jpeg";
    }

    public static string ExtensionFor(string mimeType) => mimeType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/tiff" => ".tif",
        "image/webp" => ".webp",
        _ => ".jpg",
    };

    /// <summary>"JPEG", "PNG"… for showing next to a picture's size.</summary>
    public static string Name(string mimeType) => ExtensionFor(mimeType).TrimStart('.').ToUpperInvariant() switch
    {
        "JPG" => "JPEG",
        "TIF" => "TIFF",
        var name => name,
    };
}
