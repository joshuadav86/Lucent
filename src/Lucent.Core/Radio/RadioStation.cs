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
    public int Bitrate { get; init; }

    /// <summary>How many Radio Browser users voted for it.</summary>
    public int Votes { get; init; }

    /// <summary>How many times Radio Browser users have played it lately (the last day or so).</summary>
    public int Clicks { get; init; }

    /// <summary>How many more (or fewer) plays it had today than yesterday.</summary>
    public int Trend { get; init; }

    /// <summary>The network it belongs to, for the featured stations (SomaFM, FRISKY…), otherwise null.</summary>
    public string? Network { get; init; }

    /// <summary>True for stations from Radio Browser, whose plays are counted there.</summary>
    public bool IsFromDirectory => !Id.Contains(':');

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
