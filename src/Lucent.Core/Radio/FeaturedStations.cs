namespace Lucent.Radio;

/// <summary>
/// The stations the radio browser opens on: listener-supported networks audio nerds already know, built in so they
/// play even when the directory is down. FRISKY's stations move around, so they're looked up in the directory instead.
/// Only high quality streams are featured (see <see cref="IsHighQuality"/>): each network's best stream, with its
/// everyday one kept as a <see cref="RadioStation.Backup"/> for when the best one can't be reached.
/// </summary>
public static class FeaturedStations
{
    public const string SomaFm = "SomaFM";
    public const string Frisky = "FRISKY";
    public const string RadioParadise = "Radio Paradise";
    public const string Nightride = "Nightride FM";
    public const string TheLot = "The Lot Radio";
    public const string Nts = "NTS";

    /// <summary>The networks in the order they're listed.</summary>
    public static readonly IReadOnlyList<string> Networks = new[] { SomaFm, Frisky, RadioParadise, Nightride, TheLot, Nts };

    /// <summary>Networks whose stations come from a directory search for this name, rather than the list below.</summary>
    public static readonly IReadOnlyDictionary<string, string> LookedUp = new Dictionary<string, string>
    {
        [Frisky] = "frisky",
    };

    /// <summary>
    /// Where each network takes support from its listeners: a donation page, a Patreon, or (for The Lot, which asks
    /// for neither) its shop.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> SupportPages = new Dictionary<string, string>
    {
        [SomaFm] = "https://somafm.com/support/",
        [RadioParadise] = "https://radioparadise.com/support",
        [Nightride] = "https://www.patreon.com/nightridefm",
        [TheLot] = "https://www.thelotradio.com/shop",
        [Nts] = "https://www.nts.live/supporters",
    };

    /// <summary>The lowest bitrate featured for MP3 and other older formats, in kbps.</summary>
    public const int MinBitrate = 192;

    /// <summary>The lowest featured for AAC, Opus and Ogg, which sound as good as MP3 at about one and a half times the bitrate.</summary>
    public const int MinEfficientBitrate = 128;

    /// <summary>
    /// True when a station sounds good enough to feature: lossless (FLAC), AAC, Opus or Ogg at 128 kbps or more, or
    /// MP3 at 192 kbps or more. Video stations are featured for their picture. The built-in stations below are picked by
    /// hand at their networks' best streams; this sorts the ones looked up in the directory.
    /// </summary>
    public static bool IsHighQuality(RadioStation station)
    {
        if (station.HasVideo)
        {
            return true;
        }

        var codec = station.Codec.ToUpperInvariant();
        if (codec.Contains("FLAC"))
        {
            return true;
        }

        var efficient = codec.Contains("AAC") || codec.Contains("OPUS") || codec.Contains("OGG") || codec.Contains("VORBIS");
        return station.Bitrate >= (efficient ? MinEfficientBitrate : MinBitrate);
    }

    /// <summary>
    /// A looked-up network's stations from a directory search: only the ones named for it, each at its best stream,
    /// and only those that are <see cref="IsHighQuality">high quality</see>. (The directory already leaves out streams
    /// that were down at its last check.)
    /// </summary>
    public static List<RadioStation> BestStations(IEnumerable<RadioStation> found, string name, string network) =>
        found.Where(station => station.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(station => RadioDirectory.NameKey(station.Name))
            .Select(copies => copies.Where(IsHighQuality).OrderByDescending(Quality).ThenByDescending(station => station.Votes).FirstOrDefault())
            .OfType<RadioStation>()
            .Select(station => station with { Network = network })
            .ToList();

    // MP3 kbps that sound about the same: AAC, Opus and Ogg count half again.
    private static double Quality(RadioStation station)
    {
        var codec = station.Codec.ToUpperInvariant();
        if (codec.Contains("FLAC"))
        {
            return double.MaxValue;
        }

        var efficient = codec.Contains("AAC") || codec.Contains("OPUS") || codec.Contains("OGG") || codec.Contains("VORBIS");
        return station.Bitrate * (efficient ? 1.5 : 1.0);
    }

    /// <summary>
    /// A featured station as Lucent lists it now, for a copy saved in the favorites or recently played before its
    /// stream changed (SomaFM's were 128 kbps MP3), so it plays at today's quality. Any other station comes back as it is.
    /// </summary>
    public static RadioStation Latest(RadioStation station) =>
        station.IsFromDirectory || station.Network is null
            ? station
            : Stations.FirstOrDefault(featured => featured.Id == station.Id) is { } latest
                ? latest with { Video = latest.Video || station.Video }
                : station;

    // The networks' own web addresses, so their stations found in the directory are known too.
    private static readonly (string Host, string Network)[] Hosts =
    {
        ("somafm.com", SomaFm), ("radioparadise.com", RadioParadise), ("nightride.fm", Nightride),
        ("thelotradio.com", TheLot), ("nts.live", Nts), ("ntslive.net", Nts),
    };

    /// <summary>
    /// Where listeners can support the station, or null when Lucent doesn't know: a featured network's page, found by
    /// the station's network, or for a station from the directory by its website's or stream's address.
    /// </summary>
    public static string? SupportPage(RadioStation station)
    {
        var network = station.Network;
        if (network is null)
        {
            foreach (var address in new[] { station.Homepage, station.StreamUrl })
            {
                if (address is not null && Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
                {
                    var host = uri.Host.ToLowerInvariant();
                    network = Hosts.FirstOrDefault(known => host == known.Host || host.EndsWith("." + known.Host)).Network;
                    if (network is not null)
                    {
                        break;
                    }
                }
            }
        }

        return network is not null && SupportPages.TryGetValue(network, out var page) ? page : null;
    }

    public static readonly IReadOnlyList<RadioStation> Stations = new[]
    {
        Soma("groovesalad", "Groove Salad", "ambient", "downtempo", "chillout"),
        Soma("gsclassic", "Groove Salad Classic", "ambient", "downtempo", "chillout"),
        Soma("dronezone", "Drone Zone", "ambient", "drone", "space"),
        Soma("deepspaceone", "Deep Space One", "ambient", "space", "experimental"),
        Soma("spacestation", "Space Station Soma", "electronic", "ambient", "space"),
        Soma("lush", "Lush", "chillout", "electronic", "vocal"),
        Soma("secretagent", "Secret Agent", "lounge", "downtempo", "soundtrack"),
        Soma("illstreet", "Illinois Street Lounge", "lounge", "exotica", "jazz"),
        Soma("beatblender", "Beat Blender", "deep house", "downtempo", "electronic"),
        Soma("thetrip", "The Trip", "progressive house", "trance", "electronic"),
        Soma("cliqhop", "cliqhop idm", "idm", "electronic", "experimental"),
        Soma("dubstep", "Dub Step Beyond", "dubstep", "dub", "electronic"),
        Soma("fluid", "Fluid", "hip hop", "instrumental", "downtempo"),
        Soma("vaporwaves", "Vaporwaves", "vaporwave", "electronic", "chillout"),
        Soma("defcon", "DEF CON Radio", "electronic", "techno", "breaks"),
        Soma("synphaera", "Synphaera Radio", "ambient", "synthwave", "electronic"),
        Soma("darkzone", "The Dark Zone", "dark ambient", "ambient", "experimental"),
        Soma("missioncontrol", "Mission Control", "ambient", "space", "experimental"),
        Soma("n5md", "n5MD Radio", "ambient", "idm", "post rock"),
        Soma("u80s", "Underground 80s", "80s", "new wave", "synthpop"),
        Soma("seventies", "Left Coast 70s", "70s", "rock", "pop"),
        Soma("bagel", "BAGeL Radio", "alternative", "rock", "indie"),
        Soma("indiepop", "Indie Pop Rocks!", "indie", "pop", "rock"),
        Soma("poptron", "PopTron", "electropop", "indie", "pop"),
        Soma("covers", "Covers", "covers", "rock", "pop"),
        Soma("folkfwd", "Folk Forward", "folk", "indie", "americana"),
        Soma("bootliquor", "Boot Liquor", "americana", "country", "roots"),
        Soma("thistle", "ThistleRadio", "celtic", "folk", "world"),
        Soma("suburbsofgoa", "Suburbs of Goa", "world", "asian", "electronic"),
        Soma("sonicuniverse", "Sonic Universe", "jazz", "nu jazz", "avant-garde"),
        Soma("reggae", "Heavyweight Reggae", "reggae", "ska", "rocksteady"),
        Soma("metal", "Metal Detector", "metal", "rock", "heavy metal"),
        Soma("digitalis", "Digitalis", "electronic", "alternative", "rock"),
        Soma("sf1033", "SF 10-33", "ambient", "scanner", "experimental"),
        Paradise("main", "aac-320", "mp3-192", "Radio Paradise Main Mix", "eclectic", "rock", "world"),
        Paradise("mellow", "mellow-320", "mellow-192", "Radio Paradise Mellow Mix", "eclectic", "chillout", "acoustic"),
        Paradise("rock", "rock-320", "rock-192", "Radio Paradise Rock Mix", "rock", "classic rock", "alternative"),
        Station(Nightride, "nightride:nightride", "Nightride FM", "https://stream.nightride.fm/nightride.mp3", 320,
            "https://nightride.fm", "synthwave", "retrowave", "electronic"),
        Station(Nightride, "nightride:chillsynth", "Chillsynth", "https://stream.nightride.fm/chillsynth.mp3", 320,
            "https://nightride.fm", "synthwave", "chillwave", "chillout"),
        Station(Nightride, "nightride:darksynth", "Darksynth", "https://stream.nightride.fm/darksynth.mp3", 320,
            "https://nightride.fm", "darksynth", "synthwave", "electronic"),
        // The Lot's DJs are filmed live from a shipping container in Brooklyn, so the stream is video (HLS from Livepeer).
        Station(TheLot, "thelot:live", "The Lot Radio", "https://livepeercdn.studio/hls/85c28sa2o8wppm58/index.m3u8", 0,
            "https://www.thelotradio.com", "dj sets", "house", "electronic") with { Video = true, Codec = "H.264" },
        // NTS's two live channels, from London (and LA, Shanghai and elsewhere): a different DJ or show most hours.
        Station(Nts, "nts:1", "NTS 1", "https://stream-relay-geo.ntslive.net/stream", 0,
            "https://www.nts.live", "dj sets", "eclectic", "electronic") with { Country = "United Kingdom", CountryCode = "GB" },
        Station(Nts, "nts:2", "NTS 2", "https://stream-relay-geo.ntslive.net/stream2", 0,
            "https://www.nts.live", "dj sets", "eclectic", "experimental") with { Country = "United Kingdom", CountryCode = "GB" },
    };

    // SomaFM's best is 320 kbps AAC over HLS; its 128 kbps MP3 Icecast stream is the backup.
    private static RadioStation Soma(string channel, string name, params string[] tags)
    {
        var everyday = Station(SomaFm, $"somafm:{channel}", name, $"https://ice1.somafm.com/{channel}-128-mp3", 128,
            $"https://somafm.com/{channel}/", tags);
        return everyday with
        {
            StreamUrl = $"https://hls.somafm.com/hls/{channel}/320k/program.m3u8",
            Codec = "AAC",
            Bitrate = 320,
            Backup = everyday,
        };
    }

    // Radio Paradise's best is 320 kbps AAC; its 192 kbps MP3 stream is the backup.
    private static RadioStation Paradise(string mix, string best, string everyday, string name, params string[] tags)
    {
        var backup = Station(RadioParadise, $"radioparadise:{mix}", name, $"http://stream.radioparadise.com/{everyday}", 192,
            "https://radioparadise.com", tags);
        return backup with
        {
            StreamUrl = $"https://stream.radioparadise.com/{best}",
            Codec = "AAC",
            Bitrate = 320,
            Backup = backup,
        };
    }

    private static RadioStation Station(string network, string id, string name, string url, int bitrate, string homepage, params string[] tags) =>
        new()
        {
            Id = id,
            Name = name,
            StreamUrl = url,
            Homepage = homepage,
            Tags = tags.ToList(),
            // SomaFM and Radio Paradise are both in California and The Lot is in New York; Nightride doesn't say where it is.
            Country = network == Nightride ? string.Empty : "United States",
            CountryCode = network == Nightride ? string.Empty : "US",
            Codec = "MP3",
            Bitrate = bitrate,
            Network = network,
        };
}
