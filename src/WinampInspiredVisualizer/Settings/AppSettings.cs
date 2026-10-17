using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Library;

namespace WinampInspiredVisualizer.Settings;

/// <summary>Preferences kept between runs, stored as JSON in %AppData%\Lucid\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Folders whose music is listed in the library.</summary>
    public List<string> LibraryFolders { get; set; } = new();

    public LibrarySort LibrarySort { get; set; } = LibrarySort.Newest;

    public bool GroupLibraryByFolder { get; set; } = true;

    /// <summary>Whether the library lists only the favourite songs.</summary>
    public bool LibraryFavoritesOnly { get; set; }

    /// <summary>Library folder headings the user has closed.</summary>
    public List<string> CollapsedLibraryFolders { get; set; } = new();

    // A first start opens just the player, the equalizer and the visuals; the library and queue are
    // a click away on the player.
    public bool ShowLibrary { get; set; }

    public bool ShowQueue { get; set; }

    public bool ShowVisuals { get; set; } = true;

    /// <summary>Whether the equalizer is open, docked under the player like Winamp's.</summary>
    public bool ShowEqualizer { get; set; } = true;

    // ---- Player ----

    public bool Shuffle { get; set; }

    public RepeatMode Repeat { get; set; } = RepeatMode.Off;

    /// <summary>Show the time left in the track instead of the time played.</summary>
    public bool ShowRemainingTime { get; set; }

    public MeterMode Meter { get; set; } = MeterMode.Bars;

    public double Volume { get; set; } = 0.8;

    /// <summary>Where each window was last left, by name ("Player", "Visuals", "Queue", "Library").</summary>
    public Dictionary<string, WindowBounds> Windows { get; set; } = new();

    // ---- Graphics ----

    public RenderResolution Resolution { get; set; } = RenderResolution.Window;

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

    /// <summary>
    /// Tags ticked in the visual picker. When any are ticked, the picker shows (and Auto, N and B play) only
    /// the visuals with at least one of them. Empty means no filter.
    /// </summary>
    public List<string> VisualTagFilter { get; set; } = new();

    /// <summary>Songs marked as favourites, by file path.</summary>
    public List<string> FavoriteTracks { get; set; } = new();

    /// <summary>The name the favourites go by in lists of visuals; no list of the user's can take it.</summary>
    public const string FavoritesListName = "Favorites";

    // ---- Sound ----

    public EqualizerSettings Equalizer { get; set; } = new();

    // ---- Look ----

    public string Theme { get; set; } = "Midnight";

    /// <summary>The skin: shapes, type and finish, laid over the theme's colours.</summary>
    public string Skin { get; set; } = "Lucid";

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

    // ---- Analyzer ----

    /// <summary>The analyzer window's size. It opens docked to the player's group, wherever there's room.</summary>
    public double AnalyzerWindowWidth { get; set; } = 760;

    public double AnalyzerWindowHeight { get; set; } = 460;

    public SpectrogramPalette AnalyzerPalette { get; set; } = SpectrogramPalette.Classic;

    public AnalyzerSpeed AnalyzerSpeed { get; set; } = AnalyzerSpeed.Normal;

    /// <summary>Which parts of the analyzer are shown.</summary>
    public AnalyzerModules AnalyzerModules { get; set; } = AnalyzerModules.All;

    // ---- Audio tools ----

    /// <summary>The format and quality last picked in the converter, offered again next time.</summary>
    public ExportOptions ConvertOptions { get; set; } = new();

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lucid", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // A damaged settings file just means starting from the defaults.
        }

        return new AppSettings();
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
    All = Heatmap | Spectrum | Waveform | Stereo,
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
}
