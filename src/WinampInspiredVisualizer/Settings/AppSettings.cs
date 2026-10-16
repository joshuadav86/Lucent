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
