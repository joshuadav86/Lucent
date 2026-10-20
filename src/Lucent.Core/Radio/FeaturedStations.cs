namespace WinampInspiredVisualizer.Radio;

/// <summary>
/// The stations the radio browser opens on: listener-supported networks audio nerds already know, built in so they
/// play even when the directory is down. FRISKY's stations move around, so they're looked up in the directory instead.
/// </summary>
public static class FeaturedStations
{
    public const string SomaFm = "SomaFM";
    public const string Frisky = "FRISKY";
    public const string RadioParadise = "Radio Paradise";
    public const string Nightride = "Nightride FM";

    /// <summary>The networks in the order they're listed.</summary>
    public static readonly IReadOnlyList<string> Networks = new[] { SomaFm, Frisky, RadioParadise, Nightride };

    /// <summary>Networks whose stations come from a directory search for this name, rather than the list below.</summary>
    public static readonly IReadOnlyDictionary<string, string> LookedUp = new Dictionary<string, string>
    {
        [Frisky] = "frisky",
    };

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
        Station(RadioParadise, "radioparadise:main", "Radio Paradise Main Mix", "http://stream.radioparadise.com/mp3-192", 192,
            "https://radioparadise.com", "eclectic", "rock", "world"),
        Station(RadioParadise, "radioparadise:mellow", "Radio Paradise Mellow Mix", "http://stream.radioparadise.com/mellow-192", 192,
            "https://radioparadise.com", "eclectic", "chillout", "acoustic"),
        Station(RadioParadise, "radioparadise:rock", "Radio Paradise Rock Mix", "http://stream.radioparadise.com/rock-192", 192,
            "https://radioparadise.com", "rock", "classic rock", "alternative"),
        Station(Nightride, "nightride:nightride", "Nightride FM", "https://stream.nightride.fm/nightride.mp3", 320,
            "https://nightride.fm", "synthwave", "retrowave", "electronic"),
        Station(Nightride, "nightride:chillsynth", "Chillsynth", "https://stream.nightride.fm/chillsynth.mp3", 320,
            "https://nightride.fm", "synthwave", "chillwave", "chillout"),
        Station(Nightride, "nightride:darksynth", "Darksynth", "https://stream.nightride.fm/darksynth.mp3", 320,
            "https://nightride.fm", "darksynth", "synthwave", "electronic"),
    };

    private static RadioStation Soma(string channel, string name, params string[] tags) =>
        Station(SomaFm, $"somafm:{channel}", name, $"https://ice1.somafm.com/{channel}-128-mp3", 128,
            $"https://somafm.com/{channel}/", tags);

    private static RadioStation Station(string network, string id, string name, string url, int bitrate, string homepage, params string[] tags) =>
        new()
        {
            Id = id,
            Name = name,
            StreamUrl = url,
            Homepage = homepage,
            Tags = tags.ToList(),
            // SomaFM and Radio Paradise are both in California; Nightride doesn't say where it is.
            Country = network == Nightride ? string.Empty : "United States",
            CountryCode = network == Nightride ? string.Empty : "US",
            Codec = "MP3",
            Bitrate = bitrate,
            Network = network,
        };
}
