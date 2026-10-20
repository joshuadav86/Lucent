using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinampInspiredVisualizer.Editor.Plugins;

namespace WinampInspiredVisualizer.Settings;

/// <summary>
/// The Record window's choices, kept between runs in %AppData%\Lucent\recording.json (on a Mac, in
/// ~/Library/Application Support/Lucent): the input and its format, how recordings are named and where
/// they go, and the live plugins. Separate from the main settings, like the editor's.
/// </summary>
public sealed class RecordingSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object Gate = new();

    /// <summary>The sample rates offered, in hertz.</summary>
    public static readonly int[] SampleRates = { 44100, 48000, 88200, 96000, 176400, 192000 };

    /// <summary>The bit depths offered: 16 and 24 are whole numbers, 32 is floating point (it can't clip).</summary>
    public static readonly int[] BitDepths = { 16, 24, 32 };

    /// <summary>What a recording is called before anything is changed: the name, then the date and time it started.</summary>
    public const string DefaultTemplate = "{name} {date} {time}";

    /// <summary>The input last recorded from (see each app's Audio/LiveInputs.cs), or null for the system's default.</summary>
    public string? InputId { get; set; }

    /// <summary>The first of the input's channels recorded (0 is channel 1).</summary>
    public int FirstChannel { get; set; }

    /// <summary>1 for a mono file from one channel, 2 for stereo from a pair.</summary>
    public int Channels { get; set; } = 2;

    public int SampleRate { get; set; } = 48000;

    public int BitDepth { get; set; } = 24;

    /// <summary>The name that goes in place of {name} in the template: what's being recorded.</summary>
    public string Name { get; set; } = "DJ set";

    /// <summary>How files are named (see Recording/RecordingNames.cs for the words in braces).</summary>
    public string Template { get; set; } = DefaultTemplate;

    /// <summary>Where recordings are saved; null until one is chosen, when Music\Lucent Recordings is used.</summary>
    public string? Folder { get; set; }

    /// <summary>The input is heard through the player's output while the Record window is open (after the plugins).</summary>
    public bool Listen { get; set; }

    /// <summary>A second file without the plugins is recorded alongside, in case they're regretted later.</summary>
    public bool KeepDryCopy { get; set; }

    /// <summary>Lucent marks where it hears one song give way to the next, as well as the marks made by hand.</summary>
    public bool DetectSongChanges { get; set; } = true;

    /// <summary>
    /// Each track's song is looked up with ACRCloud, using the DJ's own key (each app's Audio/SongIdAccount.cs),
    /// and the marks are named after them. Does nothing until a key has been entered.
    /// </summary>
    public bool IdentifySongs { get; set; } = true;

    /// <summary>A .cue sheet listing the tracks is saved beside each recording, for players and DJ software that read them.</summary>
    public bool WriteCueSheet { get; set; } = true;

    /// <summary>Finished recordings show up in the library: their folder becomes a library folder.</summary>
    public bool AddToLibrary { get; set; } = true;

    /// <summary>The live plugins, in order, with their settings, so they come back the next time the window opens.</summary>
    public ChainPreset? Plugins { get; set; }

    /// <summary>The folder recordings go into when none has been chosen.</summary>
    public static string DefaultFolder
    {
        get
        {
            var music = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            if (string.IsNullOrEmpty(music))
            {
                music = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Music");
            }

            return Path.Combine(music, "Lucent Recordings");
        }
    }

    private static string FilePath => Path.Combine(AppDataFolder.Path, "recording.json");

    public static RecordingSettings Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = JsonSerializer.Deserialize<RecordingSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new RecordingSettings();
                    // A file edited by hand might ask for a format that isn't offered.
                    if (!SampleRates.Contains(settings.SampleRate))
                    {
                        settings.SampleRate = 48000;
                    }

                    if (!BitDepths.Contains(settings.BitDepth))
                    {
                        settings.BitDepth = 24;
                    }

                    settings.Channels = Math.Clamp(settings.Channels, 1, 2);
                    settings.FirstChannel = Math.Max(0, settings.FirstChannel);
                    return settings;
                }
            }
            catch (Exception)
            {
                // A damaged file just means starting from the defaults.
            }

            return new RecordingSettings();
        }
    }

    public void Save()
    {
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch (Exception)
            {
                // Not being able to save preferences shouldn't stop a recording.
            }
        }
    }
}
