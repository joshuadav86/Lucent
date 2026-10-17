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

    public bool ShowVisuals { get; set; } = true;

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

/// <summary>A window's position and size, in device-independent pixels.</summary>
public sealed class WindowBounds
{
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}
