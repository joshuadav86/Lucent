using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    /// <summary>Library folder headings the user has closed.</summary>
    public List<string> CollapsedLibraryFolders { get; set; } = new();

    public bool ShowLibrary { get; set; } = true;

    public bool ShowQueue { get; set; } = true;

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

    // ---- Sound ----

    public EqualizerSettings Equalizer { get; set; } = new();

    // ---- Look ----

    public string Theme { get; set; } = "Midnight";

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
