using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Settings;

/// <summary>What the audio editor shows in its main view.</summary>
public enum EditorView
{
    Waveform,
    Spectrogram,
}

/// <summary>
/// The audio editor's preferences and the last settings of its built-in tools, kept between runs in
/// %AppData%\Lucent\editor.json. Separate from the main settings, so several editors and the player
/// never write over each other's changes.
/// </summary>
public sealed class EditorSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly object Gate = new();

    /// <summary>The crossfade lengths offered for joins, in milliseconds; 0 is off.</summary>
    public static readonly int[] CrossfadeChoices = { 0, 5, 10, 20, 50 };

    /// <summary>Selections and the cursor land on the nearest zero crossing.</summary>
    public bool SnapToZeroCrossings { get; set; } = true;

    /// <summary>Tab and Shift+Tab jump the cursor to the next and previous transient (a drum hit, the start of a note).</summary>
    public bool TabToTransient { get; set; } = true;

    /// <summary>How readily Tab to Transient counts an onset as a transient, from 0 (only hard hits) to 100 (every onset).</summary>
    public double TransientSensitivity { get; set; } = Transients.DefaultSensitivity;

    /// <summary>How long the crossfade at a cut, paste or clip move is, in milliseconds (0 for none).</summary>
    public int CrossfadeMs { get; set; } = 10;

    public EditorView View { get; set; } = EditorView.Waveform;

    /// <summary>Playing the selection starts again at its beginning when it reaches the end.</summary>
    public bool Loop { get; set; }

    /// <summary>The beats and bars are drawn over the waveform, and the cursor and selection snap to the beats.</summary>
    public bool SnapToBeats { get; set; }

    /// <summary>How many bars long the loop maker makes a loop.</summary>
    public int LoopBars { get; set; } = 4;

    /// <summary>How much taller than full scale the waves are drawn (1 = full scale fills the lane).</summary>
    public double WaveZoom { get; set; } = 1;

    /// <summary>The history list is open beside the waveform.</summary>
    public bool ShowHistory { get; set; }

    /// <summary>The listening volume while editing (0 to 1); null until it's first set, when the main player's is used.</summary>
    public double? Volume { get; set; }

    /// <summary>The channel strip of plugin slots is open beside the waveform.</summary>
    public bool ShowInserts { get; set; } = true;

    /// <summary>The plugin browser lists plugins under their makers rather than their types.</summary>
    public bool PluginsByMaker { get; set; }

    /// <summary>The plugins starred in the plugin browser, by their class IDs (which stay the same across versions).</summary>
    public List<string> FavoritePlugins { get; set; } = new();

    /// <summary>Types the user gave plugins in the browser, by class ID, in place of the ones Lucent works out.</summary>
    public Dictionary<string, string> PluginTypes { get; set; } = new();

    /// <summary>The effects last used from the effects menu, by id, most recent first.</summary>
    public List<string> RecentEffects { get; set; } = new();

    /// <summary>Once the sound is split into clips, dragging a clip's waveform moves it (the hand tool); off, dragging selects.</summary>
    public bool DragMovesClips { get; set; } = true;

    public List<EqBand> EqBands { get; set; } = EqBand.Defaults();

    public CompressorSettings Compressor { get; set; } = new();

    public LimiterSettings Limiter { get; set; } = new();

    /// <summary>The compressor (false) or the limiter (true) was the last one used.</summary>
    public bool DynamicsIsLimiter { get; set; }

    public double NoiseReductionDb { get; set; } = 12;

    public double NoiseSensitivityDb { get; set; } = 6;

    public double ClickSensitivity { get; set; } = 5;

    public double StereoWidth { get; set; } = 1.2;

    /// <summary>The stems the stem splitter keeps ("vocals", "drums", "bass", "other"); at first, all but the vocals.</summary>
    public List<string> StemsKept { get; set; } = new() { "drums", "bass", "other" };

    private static string FilePath => Path.Combine(AppDataFolder.Path, "editor.json");

    public static EditorSettings Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var settings = JsonSerializer.Deserialize<EditorSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new EditorSettings();
                    // An older file, or one edited by hand, might have a different set of bands.
                    if (settings.EqBands.Count != EqBand.Defaults().Count)
                    {
                        settings.EqBands = EqBand.Defaults();
                    }

                    return settings;
                }
            }
            catch (Exception)
            {
                // A damaged file just means starting from the defaults.
            }

            return new EditorSettings();
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
                // Not being able to save preferences shouldn't interrupt editing.
            }
        }
    }
}
