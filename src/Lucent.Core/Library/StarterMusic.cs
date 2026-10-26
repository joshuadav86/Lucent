using System.IO;
using Lucent.Audio;
using Lucent.Settings;

namespace Lucent.Library;

/// <summary>
/// The music that comes with Lucent (Kepler North's "Self Actualize"), so a first start has something to play.
/// It ships in the app's StarterMusic folder and is copied out to a Starter Music folder beside the settings,
/// because the app's own folder is replaced by every update (and on a Mac is signed, so its files can't change).
/// From there it's an ordinary library folder: its tags can be edited, and it can be taken out of the library.
/// </summary>
public static class StarterMusic
{
    /// <summary>Where the music is copied to, and the library folder it's listed under.</summary>
    public static string Folder => Path.Combine(AppDataFolder.Path, "Starter Music");

    private static string BundledFolder => Path.Combine(AppContext.BaseDirectory, "StarterMusic");

    /// <summary>Copies the music into <see cref="Folder"/>, leaving any copy already there, and returns its files.</summary>
    public static IReadOnlyList<string> Install()
    {
        var files = new List<string>();
        try
        {
            if (!Directory.Exists(BundledFolder))
            {
                return files;
            }

            Directory.CreateDirectory(Folder);
            foreach (var source in Directory.EnumerateFiles(BundledFolder).Where(AudioReaderFactory.IsSupported).Order(StringComparer.OrdinalIgnoreCase))
            {
                var target = Path.Combine(Folder, Path.GetFileName(source));
                if (!File.Exists(target))
                {
                    File.Copy(source, target);
                }

                files.Add(target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to copy it just means starting with an empty library, as before.
        }

        return files;
    }
}
