using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Radio;

/// <summary>
/// What the radio browser shows, the same on Windows and the Mac: its sections (the featured networks, the user's
/// favorites and recently played stations, every station, or one genre), the search, filters and sort applied to
/// stations it already has, and the starring and remembering of stations.
/// </summary>
public static class RadioGuide
{
    public const string Featured = "Featured";
    public const string Favorites = "Favorites";
    public const string Recent = "Recently played";
    public const string AllStations = "All stations";
    public const string LiveAndVideo = "Live DJs and video";

    /// <summary>How many recently played stations are remembered.</summary>
    public const int RecentLimit = 30;

    /// <summary>How wide the details beside a station's name start out, in pixels.</summary>
    public const double DetailsWidth = 240;

    /// <summary>The room a station's name keeps in its row (with the row's buttons and padding) before its details get any.</summary>
    private const double NameRoom = 280;

    /// <summary>
    /// The details' width in a station list this wide: what the listener dragged the divider to, but never so wide that
    /// the names are squeezed, so a narrow window shrinks the details first.
    /// </summary>
    public static double DetailsWidthFor(double wanted, double listWidth) =>
        listWidth <= 0 ? Math.Max(0, wanted) : Math.Clamp(wanted, 0, Math.Max(0, listWidth - NameRoom));

    /// <summary>The bitrates the quality filter offers, in kbps (0 for any).</summary>
    public static readonly IReadOnlyList<int> Qualities = new[] { 0, 128, 192, 256, 320 };

    /// <summary>True for sections whose stations are Lucent's own (featured, favorites, recent, live and video) rather than the directory's.</summary>
    public static bool IsLocal(string section) => section is Featured or Favorites or Recent or LiveAndVideo;

    /// <summary>
    /// True for stations that play live DJ sets and say who's on (The Lot, NTS), or that the directory tags as DJ sets.
    /// </summary>
    public static bool IsLiveDj(RadioStation station) =>
        (LiveShows.Knows(station) && !LiveShows.SaysSongs(station))
        || station.Tags.Any(tag => tag.Equals("dj sets", StringComparison.OrdinalIgnoreCase) || tag.Equals("live dj", StringComparison.OrdinalIgnoreCase));

    /// <summary>The genre a section is, or null for the other sections.</summary>
    public static Genre? GenreOf(string section) => Genres.All.FirstOrDefault(genre => genre.Name == section);

    public static string SortLabel(RadioSort sort) => sort switch
    {
        RadioSort.Votes => "TOP VOTED",
        RadioSort.Trending => "TRENDING",
        RadioSort.Name => "A TO Z",
        RadioSort.Bitrate => "BEST QUALITY",
        _ => "POPULAR",
    };

    public static string SortDescription(RadioSort sort) => sort switch
    {
        RadioSort.Votes => "Most voted for by Radio Browser's listeners",
        RadioSort.Trending => "Gaining the most listeners today",
        RadioSort.Name => "By name",
        RadioSort.Bitrate => "Highest bitrate first",
        _ => "Most played lately",
    };

    public static string QualityLabel(int kbps) => kbps <= 0 ? "Any quality" : $"{kbps} kbps and up";

    public static IEnumerable<RadioStation> Sort(IEnumerable<RadioStation> stations, RadioSort sort) => sort switch
    {
        RadioSort.Votes => stations.OrderByDescending(station => station.Votes),
        RadioSort.Trending => stations.OrderByDescending(station => station.Trend),
        RadioSort.Name => stations.OrderBy(station => station.Name, StringComparer.CurrentCultureIgnoreCase),
        RadioSort.Bitrate => stations.OrderByDescending(station => station.Bitrate),
        _ => stations.OrderByDescending(station => station.Clicks),
    };

    /// <summary>True when every word of <paramref name="search"/> is in the station's name, tags, country or network.</summary>
    public static bool Matches(RadioStation station, string search)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.All(word =>
            station.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || station.Tags.Any(tag => tag.Contains(word, StringComparison.CurrentCultureIgnoreCase))
            || station.Country.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || (station.Network?.Contains(word, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }

    /// <summary>True when the station passes the quality, country and video filters.</summary>
    public static bool PassesFilters(RadioStation station, AppSettings settings) =>
        // A station that doesn't say its bitrate isn't hidden by the quality filter: plenty of good ones don't.
        (settings.RadioMinBitrate <= 0 || station.Bitrate == 0 || station.Bitrate >= settings.RadioMinBitrate)
        && (string.IsNullOrEmpty(settings.RadioCountry) || string.Equals(station.CountryCode, settings.RadioCountry, StringComparison.OrdinalIgnoreCase))
        && (!settings.RadioVideoOnly || HasVideo(settings, station));

    /// <summary>
    /// True when the station sends a picture, as far as Lucent knows: its format says so, it's a featured video
    /// station, or Lucent has seen a picture from it (which the favorites and recently played remember).
    /// </summary>
    public static bool HasVideo(AppSettings settings, RadioStation station) =>
        station.HasVideo
        || settings.RadioRecent.Any(saved => saved.Id == station.Id && saved.Video)
        || settings.RadioFavorites.Any(saved => saved.Id == station.Id && saved.Video);

    /// <summary>Remembers that the station sent a picture, so it gets the camera from now on.</summary>
    public static RadioStation MarkVideo(AppSettings settings, RadioStation station)
    {
        var marked = station with { Video = true };
        for (var i = 0; i < settings.RadioRecent.Count; i++)
        {
            if (settings.RadioRecent[i].Id == station.Id)
            {
                settings.RadioRecent[i] = settings.RadioRecent[i] with { Video = true };
            }
        }

        for (var i = 0; i < settings.RadioFavorites.Count; i++)
        {
            if (settings.RadioFavorites[i].Id == station.Id)
            {
                settings.RadioFavorites[i] = settings.RadioFavorites[i] with { Video = true };
            }
        }

        return marked;
    }

    /// <summary>
    /// A local section's stations, searched and filtered. The featured stations keep their networks' order (and the
    /// sort applies inside each), recently played stays most recent first, and favorites take the sort.
    /// </summary>
    public static IReadOnlyList<RadioStation> Local(string section, AppSettings settings, IReadOnlyList<RadioStation> featured,
        string search, RadioSort sort)
    {
        var stations = section switch
        {
            Featured => featured,
            Favorites => settings.RadioFavorites.Select(FeaturedStations.Latest).ToList(),
            // The featured ones first, then any others played or starred that turned out to have video.
            LiveAndVideo => featured.Concat(settings.RadioFavorites).Concat(settings.RadioRecent)
                .Where(station => IsLiveDj(station) || HasVideo(settings, station))
                .DistinctBy(station => station.Id)
                .ToList(),
            _ => settings.RadioRecent.Select(FeaturedStations.Latest).ToList(),
        };
        var shown = stations
            .Where(station => Matches(station, search) && PassesFilters(station, settings));
        return section switch
        {
            Featured => shown.GroupBy(station => station.Network ?? string.Empty)
                .OrderBy(group => NetworkOrder(group.Key))
                .SelectMany(group => sort == RadioSort.Name ? Sort(group, sort) : group)
                .ToList(),
            Favorites => Sort(shown, sort == RadioSort.Popular ? RadioSort.Name : sort).ToList(),
            _ => shown.ToList(),
        };
    }

    /// <summary>The stations under headings: the featured ones under their networks, the rest under one heading.</summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<RadioStation> Stations)> Group(IReadOnlyList<RadioStation> stations, string section)
    {
        if (section != Featured)
        {
            return stations.Count == 0
                ? Array.Empty<(string, IReadOnlyList<RadioStation>)>()
                : new[] { (section, stations) };
        }

        return stations.GroupBy(station => station.Network ?? string.Empty)
            .Select(group => (group.Key, (IReadOnlyList<RadioStation>)group.ToList()))
            .ToList();
    }

    public static bool IsFavorite(AppSettings settings, RadioStation station) =>
        settings.RadioFavorites.Any(favorite => favorite.Id == station.Id);

    public static void SetFavorite(AppSettings settings, RadioStation station, bool favorite)
    {
        settings.RadioFavorites.RemoveAll(saved => saved.Id == station.Id);
        if (favorite)
        {
            settings.RadioFavorites.Add(station);
        }
    }

    /// <summary>Puts a station at the top of the recently played list.</summary>
    public static void Remember(AppSettings settings, RadioStation station)
    {
        settings.RadioRecent.RemoveAll(saved => saved.Id == station.Id);
        settings.RadioRecent.Insert(0, station);
        if (settings.RadioRecent.Count > RecentLimit)
        {
            settings.RadioRecent.RemoveRange(RecentLimit, settings.RadioRecent.Count - RecentLimit);
        }

        // A favorite's details (its address, say) follow what was just played.
        var favorite = settings.RadioFavorites.FindIndex(saved => saved.Id == station.Id);
        if (favorite >= 0)
        {
            settings.RadioFavorites[favorite] = station;
        }
    }

    /// <summary>A station for an address typed in by hand.</summary>
    public static RadioStation FromAddress(string address)
    {
        address = address.Trim();
        var name = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.Host : address;
        return new RadioStation { Id = "address:" + address, Name = name, StreamUrl = address };
    }

    /// <summary>True when <paramref name="address"/> looks like a stream Lucent could open.</summary>
    public static bool IsStreamAddress(string address) =>
        Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static int NetworkOrder(string network)
    {
        var index = FeaturedStations.Networks.ToList().IndexOf(network);
        return index < 0 ? int.MaxValue : index;
    }
}
