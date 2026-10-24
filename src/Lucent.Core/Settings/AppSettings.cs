using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Radio;
using WinampInspiredVisualizer.Streaming;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Settings;

/// <summary>Preferences kept between runs, stored as JSON in %AppData%\Lucent\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Folders whose music is listed in the library.</summary>
    public List<string> LibraryFolders { get; set; } = new();

    /// <summary>Folders inside the library folders that were taken out of the library.</summary>
    public List<string> HiddenLibraryFolders { get; set; } = new();

    /// <summary>Library folders whose music is all listed under the folder's own heading, subfolders and all.</summary>
    public List<string> LibraryFoldersTogether { get; set; } = new();

    /// <summary>What to do with the folders inside a newly added library folder: ask, or the remembered answer.</summary>
    public LibrarySubfolders LibrarySubfolders { get; set; } = LibrarySubfolders.Ask;

    /// <summary>Single music files added to the library on their own (dropped on it), outside its folders.</summary>
    public List<string> LibraryFiles { get; set; } = new();

    /// <summary>
    /// Whether the music that comes with Lucent still has to be put in the library and the queue. Only a first
    /// start (no settings file yet) sets it, so people updating from an older Lucent don't get it.
    /// </summary>
    public bool AddStarterMusic { get; set; }

    public LibrarySort LibrarySort { get; set; } = LibrarySort.Newest;

    /// <summary>The order Spotify's and Music's playlists are listed in under the library's PLAYLISTS.</summary>
    public RemotePlaylistSort RemotePlaylistSort { get; set; }

    public bool GroupLibraryByFolder { get; set; } = true;

    /// <summary>Whether the library lists only the favourite songs.</summary>
    public bool LibraryFavoritesOnly { get; set; }

    /// <summary>Whether the library lists the saved playlists instead of the library folders.</summary>
    public bool LibraryShowPlaylists { get; set; }

    /// <summary>Library folder headings the user has closed.</summary>
    public List<string> CollapsedLibraryFolders { get; set; } = new();

    // A first start opens just the player, the equalizer and the visuals; the library and queue are
    // a click away on the player.
    public bool ShowLibrary { get; set; }

    public bool ShowQueue { get; set; }

    public bool ShowVisuals { get; set; } = true;

    /// <summary>Whether the equalizer is open, docked under the player like Winamp's.</summary>
    public bool ShowEqualizer { get; set; } = true;

    /// <summary>Whether the analyzer was open when Lucent closed, so it opens again with it.</summary>
    public bool ShowAnalyzer { get; set; }

    /// <summary>Whether the artwork window was open when Lucent closed, so it opens again with it.</summary>
    public bool ShowArtwork { get; set; }

    // ---- Player ----

    public bool Shuffle { get; set; }

    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    /// <summary>Show the time left in the track instead of the time played.</summary>
    public bool ShowRemainingTime { get; set; }

    public MeterMode Meter { get; set; } = MeterMode.Bars;

    public double Volume { get; set; } = 0.8;

    /// <summary>Run each track straight into the next, with no gap between them.</summary>
    public bool Gapless { get; set; } = true;

    /// <summary>The output to play through (a Windows device id), or null for Windows' default.</summary>
    public string? OutputDevice { get; set; }

    /// <summary>Take the output for Lucent alone and send each track at its own format: bit-perfect playback.</summary>
    public bool ExclusiveOutput { get; set; }

    /// <summary>List Lucent's original shaders in the visual picker and play them in Auto (off leaves MilkDrop, AVS and the user's own).</summary>
    public bool LucentVisuals { get; set; } = true;

    /// <summary>List MilkDrop presets in the visual picker and play them in Auto.</summary>
    public bool MilkDropVisuals { get; set; } = true;

    /// <summary>List Winamp AVS presets in the visual picker and play them in Auto.</summary>
    public bool AvsVisuals { get; set; } = true;

    /// <summary>Draw Winamp AVS presets sharper and smoother, in Lucent's light; off shows them as Winamp drew them.</summary>
    public bool ModernAvs { get; set; } = true;

    /// <summary>Show MilkDrop presets in Lucent's light, with its glow and tone-mapping; off shows them as MilkDrop did.</summary>
    public bool ModernMilkDrop { get; set; } = true;

    /// <summary>Visuals tense up through a song's build-ups and burst on its drops (see SongLook).</summary>
    public bool BuildUpsAndDrops { get; set; } = true;

    /// <summary>Update to every new build of Lucent, not only stable releases.</summary>
    public bool EveryBuild { get; set; }

    /// <summary>
    /// Install a downloaded update when Lucent closes (or on its next start). Off, Lucent still downloads
    /// it and says it's ready, but installs it only when Restart and install is chosen.
    /// </summary>
    public bool InstallUpdatesAutomatically { get; set; } = true;

    /// <summary>Send a report to Lucent's developer when Lucent crashes, with nothing personal in it.</summary>
    public bool SendCrashReports { get; set; } = true;

    /// <summary>
    /// Where each window was last left, by name ("Player", "Visuals", "Queue", "Library", "Equalizer",
    /// "Analyzer", "Artwork"). Lucent opens them there again next time.
    /// </summary>
    public Dictionary<string, WindowBounds> Windows { get; set; } = new();

    /// <summary>
    /// The windows locked in place, by the same names: they can't be moved or resized, don't move with
    /// the player, and stay where they are when the windows are docked or laid out again.
    /// </summary>
    public List<string> LockedWindows { get; set; } = new();

    /// <summary>Every window moves with the player when it's dragged, docked to it or not (J).</summary>
    public bool LinkWindows { get; set; }

    /// <summary>
    /// Lucent full screen (Shift+F11): every open window tiled across the player's screen. Lucent opens
    /// that way again if it was closed that way.
    /// </summary>
    public bool LucentFullScreen { get; set; }

    /// <summary>Where each window was before Lucent went full screen, by name: where they go back to when it's switched off.</summary>
    public Dictionary<string, WindowBounds> WindowsBeforeFullScreen { get; set; } = new();

    /// <summary>
    /// The windows swapped while Lucent's full screen (a window dragged onto another), by name, in the
    /// order they were swapped: laying the windows out again keeps them swapped. Cleared as it's switched on or off.
    /// </summary>
    public List<List<string>> FullScreenSwaps { get; set; } = new();

    // ---- Graphics ----

    public RenderResolution Resolution { get; set; } = RenderResolution.Auto;

    /// <summary>
    /// Whether this settings file has been moved onto the Automatic resolution, which came later:
    /// anyone still on the old default (the window's size) is moved over once.
    /// </summary>
    public bool AutoResolutionOffered { get; set; }

    public DetailLevel Detail { get; set; } = DetailLevel.Normal;

    public GlowLevel Glow { get; set; } = GlowLevel.Normal;

    /// <summary>Draw in HDR when the screen has HDR turned on in Windows.</summary>
    public bool Hdr { get; set; }

    public bool ShowFrameRate { get; set; }

    // ---- Visual changes ----

    public bool AutoCycle { get; set; } = true;

    public int AutoCycleSeconds { get; set; } = 25;

    public TransitionSpeed TransitionSpeed { get; set; } = TransitionSpeed.Smooth;

    public bool MorphTransitionsOnly { get; set; }

    /// <summary>Whether Previous visual goes back to the one shown before, or up the picker's list.</summary>
    public PreviousVisualMode PreviousVisual { get; set; } = PreviousVisualMode.LastShown;

    /// <summary>The visual showing when the app last closed.</summary>
    public string? LastVisual { get; set; }

    /// <summary>Visuals starred as favourites, by name.</summary>
    public List<string> FavoriteVisuals { get; set; } = new();

    /// <summary>The user's own lists of visuals: list name to visual names.</summary>
    public Dictionary<string, List<string>> VisualLists { get; set; } = new();

    /// <summary>
    /// Which visuals the picker shows and Auto plays from: null for all of them,
    /// <see cref="FavoritesListName"/> for the favourites, or the name of one of <see cref="VisualLists"/>.
    /// </summary>
    public string? ActiveVisualList { get; set; }

    /// <summary>Whether the visual picker lists visuals in folders by family (Fractal, Cosmic and so on).</summary>
    public bool GroupVisualsByFamily { get; set; } = true;

    /// <summary>Family folders in the visual picker the user has closed.</summary>
    public List<string> CollapsedVisualFamilies { get; set; } = new();

    /// <summary>The order the visual picker lists visuals in (inside each family folder, when they're on).</summary>
    public VisualSort VisualSort { get; set; } = VisualSort.Name;

    /// <summary>Visuals picked in the visual picker, by name, most recent first, for its "Recently used" order.</summary>
    public List<string> RecentVisuals { get; set; } = new();

    /// <summary>
    /// Tags ticked in the visual picker. When any are ticked, the picker shows (and Auto, N and B play) only
    /// the visuals with at least one of them. Empty means no filter.
    /// </summary>
    public List<string> VisualTagFilter { get; set; } = new();

    /// <summary>Songs marked as favourites, by file path.</summary>
    public List<string> FavoriteTracks { get; set; } = new();

    /// <summary>Identifies this copy of Lucent to Plex when signing in to import a Plex library.</summary>
    public string? PlexClientId { get; set; }

    /// <summary>The name the favourites go by in lists of visuals; no list of the user's can take it.</summary>
    public const string FavoritesListName = "Favorites";

    // ---- Sound ----

    public EqualizerSettings Equalizer { get; set; } = new();

    /// <summary>Left and right balance, -1 (left only) to 1 (right only). Set from the classic player's balance slider.</summary>
    public double Balance { get; set; }

    // ---- Look ----

    public string Theme { get; set; } = "Midnight";

    /// <summary>The skin: shapes, type and finish, laid over the theme's colours.</summary>
    public string Skin { get; set; } = "Lucent";

    /// <summary>
    /// The classic Winamp skin (.wsz) the player is drawn with instead, by name (see ClassicSkinLibrary),
    /// or null for Lucent's own player.
    /// </summary>
    public string? ClassicSkin { get; set; }

    /// <summary>Draws the classic player at twice its size, like Winamp's "double size" (D).</summary>
    public bool ClassicDoubleSize { get; set; } = true;

    /// <summary>
    /// How much bigger than its usual size Lucent's own player is drawn, from 1 up: dragging the player's
    /// edges scales everything in it, keeping its shape.
    /// </summary>
    public double PlayerScale { get; set; } = 1;

    // ---- Track info and artwork ----

    /// <summary>
    /// Put files' modified dates back after editing their info. The library lists newest first by date,
    /// so this keeps edited songs where they were instead of jumping to the top.
    /// </summary>
    public bool KeepFileDatesWhenEditing { get; set; } = true;

    /// <summary>Where the artwork window was last placed (null until it has been opened).</summary>
    public double? ArtworkWindowLeft { get; set; }

    public double? ArtworkWindowTop { get; set; }

    public double ArtworkWindowWidth { get; set; } = 420;

    public double ArtworkWindowHeight { get; set; } = 456;

    public bool ArtworkWindowOnTop { get; set; }

    // ---- Video ----

    /// <summary>The video window's size. It opens docked to the player's group, wherever there's room.</summary>
    public double VideoWindowWidth { get; set; } = 640;

    public double VideoWindowHeight { get; set; } = 396;

    // ---- Analyzer ----

    /// <summary>The analyzer window's size. It opens docked to the player's group, wherever there's room.</summary>
    public double AnalyzerWindowWidth { get; set; } = 760;

    public double AnalyzerWindowHeight { get; set; } = 460;

    public SpectrogramPalette AnalyzerPalette { get; set; } = SpectrogramPalette.Classic;

    public AnalyzerSpeed AnalyzerSpeed { get; set; } = AnalyzerSpeed.Normal;

    /// <summary>Which parts of the analyzer are shown.</summary>
    public AnalyzerModules AnalyzerModules { get; set; } = AnalyzerModules.All;

    /// <summary>
    /// True once the analyzer's loudness readouts have been switched on for settings saved before they existed,
    /// so they show for everyone once and stay hidden after that if they're turned off.
    /// </summary>
    public bool AnalyzerLoudnessOffered { get; set; }

    // ---- Audio tools ----

    /// <summary>The format and quality last picked in the converter, offered again next time.</summary>
    public ExportOptions ConvertOptions { get; set; } = new();

    /// <summary>The processing the converter last did to every song (normalizing, trimming silence…), in order.</summary>
    public List<ProcessingStep> ConvertSteps { get; set; } = new();

    // ---- Bandcamp and Beatport ----

    /// <summary>Import Bandcamp downloads from <see cref="DownloadsWatchFolder"/> into the library by themselves.</summary>
    public bool BandcampImport { get; set; }

    /// <summary>Import Beatport downloads from <see cref="DownloadsWatchFolder"/> into the library by themselves.</summary>
    public bool BeatportImport { get; set; }

    /// <summary>The folder watched for store downloads; null for the Downloads folder.</summary>
    public string? DownloadsWatchFolder { get; set; }

    /// <summary>
    /// Where store music goes: a Bandcamp and a Beatport folder inside it, then a folder per artist and per
    /// release. Null for the Music folder.
    /// </summary>
    public string? StoreMusicFolder { get; set; }

    /// <summary>
    /// Download new purchases from the signed-in Bandcamp account when Lucent starts. Off until the first
    /// sync has been chosen in the Bandcamp window, so signing in never starts a big download by itself.
    /// </summary>
    public bool BandcampSync { get; set; }

    /// <summary>The signed-in Bandcamp fan's username, for showing (the sign-in itself is kept encrypted elsewhere).</summary>
    public string? BandcampUsername { get; set; }

    /// <summary>The format to download purchases in (Bandcamp's own names, like "flac" or "mp3-320").</summary>
    public string BandcampFormat { get; set; } = "flac";

    /// <summary>
    /// Whether the visuals, meter, equalizer's spectrum and analyzer listen to every app's sound (Spotify, a
    /// browser, anything) instead of only Lucent's own music.
    /// </summary>
    public bool ListenToOtherApps { get; set; }

    /// <summary>
    /// With <see cref="ListenToOtherApps"/> on, the input the visuals listen to instead of every app's sound (its id in
    /// each app's Audio/LiveInputs.cs), or null for every app's sound. It's for a DAW that plays through its own
    /// driver (Ableton on ASIO), which the interface's mixer loops back to an input.
    /// </summary>
    public string? ListenInput { get; set; }

    /// <summary>The first of the two channels of <see cref="ListenInput"/> the visuals listen to, from 0.</summary>
    public int ListenInputChannel { get; set; }

    /// <summary>The other app the player shows and controls instead of Lucent's own music, if any.</summary>
    public RemoteApp RemoteApp { get; set; }

    // ---- Online radio ----

    /// <summary>Stations starred in the radio browser, kept whole so they play even when the directory is down.</summary>
    public List<RadioStation> RadioFavorites { get; set; } = new();

    /// <summary>Stations played lately, most recent first.</summary>
    public List<RadioStation> RadioRecent { get; set; } = new();

    /// <summary>The radio browser's section: Featured, Favorites, Recently played, All stations, or a genre.</summary>
    public string RadioSection { get; set; } = RadioGuide.Featured;

    public RadioSort RadioSort { get; set; } = RadioSort.Popular;

    /// <summary>The quality filter: only stations of at least this many kbps (0 for any).</summary>
    public int RadioMinBitrate { get; set; }

    /// <summary>The country filter: a two-letter country code, or null for anywhere.</summary>
    public string? RadioCountry { get; set; }

    /// <summary>The video filter: only stations with a picture (the ones with the camera beside them).</summary>
    public bool RadioVideoOnly { get; set; }

    private static string FilePath => Path.Combine(AppDataFolder.Path, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
                settings.CarryOverReplacedVisuals();
                settings.OfferAutoResolution();
                settings.OfferAnalyzerLoudness();
                return settings;
            }
        }
        catch (Exception)
        {
            // A damaged settings file just means starting from the defaults.
            return new AppSettings { AutoResolutionOffered = true, AnalyzerLoudnessOffered = true };
        }

        return new AppSettings { AddStarterMusic = true, AutoResolutionOffered = true, AnalyzerLoudnessOffered = true };
    }

    /// <summary>
    /// Visuals that were taken out and replaced: old name to the name of the visual that took its place.
    /// </summary>
    private static readonly Dictionary<string, string> ReplacedVisuals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Midnight Snowfall"] = "Julia Dust",
    };

    /// <summary>Visuals that were taken out with nothing in their place.</summary>
    private static readonly HashSet<string> RemovedVisuals = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cornell Box",
    };

    /// <summary>
    /// Points favourites, lists, recent picks and the last visual saved under a replaced visual's name at its
    /// replacement, and drops removed visuals, so nothing is left pointing at a visual that isn't there any more.
    /// </summary>
    private void CarryOverReplacedVisuals()
    {
        static string Replace(string name) => ReplacedVisuals.TryGetValue(name, out var replacement) ? replacement : name;
        static List<string> ReplaceAll(List<string>? names) =>
            (names ?? new()).Where(name => !RemovedVisuals.Contains(name))
                .Select(Replace).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        LastVisual = LastVisual is null || RemovedVisuals.Contains(LastVisual) ? null : Replace(LastVisual);
        FavoriteVisuals = ReplaceAll(FavoriteVisuals);
        RecentVisuals = ReplaceAll(RecentVisuals);
        VisualLists ??= new();
        foreach (var list in VisualLists.Keys.ToList())
        {
            VisualLists[list] = ReplaceAll(VisualLists[list]);
        }
    }

    private void OfferAutoResolution()
    {
        if (!AutoResolutionOffered)
        {
            AutoResolutionOffered = true;
            if (Resolution == RenderResolution.Window)
            {
                Resolution = RenderResolution.Auto;
            }
        }
    }

    private void OfferAnalyzerLoudness()
    {
        if (!AnalyzerLoudnessOffered)
        {
            AnalyzerLoudnessOffered = true;
            AnalyzerModules |= AnalyzerModules.Loudness;
        }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Not being able to save preferences shouldn't interrupt the music.
        }
    }
}

/// <summary>How the folders inside a newly added library folder are listed.</summary>
public enum LibrarySubfolders
{
    /// <summary>Ask each time a folder with folders inside it is added.</summary>
    Ask,

    /// <summary>Each folder inside gets a heading of its own.</summary>
    Separate,

    /// <summary>All the music inside goes under the added folder's heading.</summary>
    Together,
}

public enum RepeatMode
{
    Off,
    All,
    One,
}

/// <summary>What the little meter in the player window shows.</summary>
public enum MeterMode
{
    Bars,
    Scope,
    Off,
}

/// <summary>The colours the analyzer's heatmap is painted in.</summary>
public enum SpectrogramPalette
{
    /// <summary>Blue through green and yellow to red, like the Vision plugin.</summary>
    Classic,
    /// <summary>Black through red and orange to white.</summary>
    Fire,
    /// <summary>The theme's own background and accent colours.</summary>
    Theme,
}

/// <summary>The parts of the analyzer, any of which can be shown or hidden.</summary>
[Flags]
public enum AnalyzerModules
{
    None = 0,
    /// <summary>The frequencies over time, scrolling.</summary>
    Heatmap = 1,
    /// <summary>The frequencies right now, as bars beside the heatmap.</summary>
    Spectrum = 2,
    /// <summary>The waveform, scrolling under the heatmap.</summary>
    Waveform = 4,
    /// <summary>The stereo spread scope and correlation bar.</summary>
    Stereo = 8,
    /// <summary>The loudness readouts (LUFS, range and true peak), along the bottom.</summary>
    Loudness = 16,
    All = Heatmap | Spectrum | Waveform | Stereo | Loudness,
}

/// <summary>How fast the analyzer scrolls.</summary>
public enum AnalyzerSpeed
{
    Slow,
    Normal,
    Fast,
}

/// <summary>A window's position and size, in device-independent pixels.</summary>
public sealed class WindowBounds
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary>
    /// Whether it was docked to the player (or to a window docked to it) when it was left. Opened again
    /// later, a docked window docks to the player wherever the player is by then; one left on its own
    /// goes back where it was.
    /// </summary>
    public bool Docked { get; set; }
}
