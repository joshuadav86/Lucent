namespace WinampInspiredVisualizer.Radio;

/// <summary>An online radio station: where its stream is, and what the directory says about it.</summary>
/// <remarks>Favorites and recently played stations are kept in the settings like this, so they still play when the directory can't be reached.</remarks>
public sealed record RadioStation
{
    /// <summary>
    /// Radio Browser's id for the station, or for one of Lucent's own featured stations its network and name
    /// (<c>somafm:groovesalad</c>), so starring a station keeps it however it was found.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    /// <summary>The stream's address (or a .pls or .m3u playlist that points at it).</summary>
    public string StreamUrl { get; init; } = string.Empty;

    /// <summary>The station's website, if it has one.</summary>
    public string? Homepage { get; init; }

    /// <summary>The station's logo, if it has one.</summary>
    public string? Logo { get; init; }

    /// <summary>The station's genres and other tags, lowercase, as the directory lists them.</summary>
    public List<string> Tags { get; init; } = new();

    /// <summary>The country it broadcasts from, in English, or empty when the directory doesn't say.</summary>
    public string Country { get; init; } = string.Empty;

    /// <summary>The country's two-letter code (ISO 3166), or empty.</summary>
    public string CountryCode { get; init; } = string.Empty;

    /// <summary>The stream's format as the directory gives it: MP3, AAC and so on.</summary>
    public string Codec { get; init; } = string.Empty;

    /// <summary>Kilobits per second, or 0 when it isn't known.</summary>
    /// <remarks>Some directory listings give bits per second (320000 for 320); those are taken as kilobits, saved ones too.</remarks>
    public int Bitrate
    {
        get => _bitrate;
        init => _bitrate = value >= 10_000 ? (int)Math.Round(value / 1000.0) : Math.Max(0, value);
    }

    private readonly int _bitrate;

    /// <summary>How many Radio Browser users voted for it.</summary>
    public int Votes { get; init; }

    /// <summary>How many times Radio Browser users have played it lately (the last day or so).</summary>
    public int Clicks { get; init; }

    /// <summary>How many more (or fewer) plays it had today than yesterday.</summary>
    public int Trend { get; init; }

    /// <summary>The network it belongs to, for the featured stations (SomaFM, FRISKY…), otherwise null.</summary>
    public string? Network { get; init; }

    /// <summary>
    /// True for a station known to send a picture: a featured video station, or one Lucent has seen a picture from.
    /// Others count too when their format says so (<see cref="HasVideo"/>).
    /// </summary>
    public bool Video { get; init; }

    /// <summary>True when the station sends a picture as well as sound, as far as Lucent knows: the camera beside it.</summary>
    public bool HasVideo => Video || IsVideoCodec(Codec);

    /// <summary>True for stations from Radio Browser, whose plays are counted there.</summary>
    public bool IsFromDirectory => !Id.Contains(':');

    /// <summary>
    /// True when the directory's format names a picture: it lists video stations as their sound and picture together,
    /// such as "AAC,H.264" or "AAC+H.264".
    /// </summary>
    public static bool IsVideoCodec(string codec)
    {
        var upper = codec.ToUpperInvariant();
        return VideoCodecs.Any(upper.Contains);
    }

    private static readonly string[] VideoCodecs = { "H.264", "H264", "AVC", "H.265", "H265", "HEVC", "VP8", "VP9", "AV1", "MPEG4", "MPEG-4", "THEORA" };

    /// <summary>"ambient · electronica · United States · 128 kbps", leaving out whatever isn't known.</summary>
    public string Details(int tags = 2)
    {
        var parts = Tags.Take(tags).Select(Genres.Display).ToList();
        if (Country.Length > 0)
        {
            parts.Add(Country);
        }

        // Listeners often add the same station more than once, at different qualities, so both are shown.
        var codec = Codec.Trim().ToUpperInvariant();
        if (Bitrate > 0)
        {
            parts.Add(codec.Length > 0 ? $"{Bitrate} kbps {codec}" : $"{Bitrate} kbps");
        }
        else if (codec.Length > 0)
        {
            parts.Add(codec);
        }

        return string.Join(" · ", parts);
    }
}
