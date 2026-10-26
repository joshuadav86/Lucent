using System.IO;

namespace Lucent.Settings;

/// <summary>
/// %AppData%\Lucent, where settings, saved playlists and the user's own visuals live. The app was called
/// Lucid before, and kept them in %AppData%\Lucid; the first run under the new name copies them across.
/// On a Mac it's ~/Library/Application Support/Lucent. LUCENT_DATA_FOLDER puts it somewhere else
/// (the Mac app's screenshot tool uses that, so it never touches real settings).
/// </summary>
public static class AppDataFolder
{
    private static readonly string Roaming = Environment.GetFolderPath(OperatingSystem.IsMacOS()
        ? Environment.SpecialFolder.LocalApplicationData
        : Environment.SpecialFolder.ApplicationData);

    /// <summary>Whether LUCENT_DATA_FOLDER put the folder somewhere else.</summary>
    public static bool IsCustom { get; } = Environment.GetEnvironmentVariable("LUCENT_DATA_FOLDER") is { Length: > 0 };

    public static string Path { get; } = Environment.GetEnvironmentVariable("LUCENT_DATA_FOLDER") is { Length: > 0 } custom
        ? custom
        : System.IO.Path.Combine(Roaming, "Lucent");

    private static string OldPath => System.IO.Path.Combine(Roaming, "Lucid");

    /// <summary>
    /// Copies everything from the old Lucid folder if there's no Lucent folder yet. The old folder is left
    /// as it was, so nothing is lost if the copy stops partway or an older build is run again.
    /// </summary>
    public static void CopyFromOldName()
    {
        try
        {
            if (Directory.Exists(Path) || !Directory.Exists(OldPath))
            {
                return;
            }

            // Copied to a temporary folder first and then renamed, so a copy that stops partway is
            // tried again next time instead of leaving a half-filled Lucent folder.
            var temp = Path + ".copying";
            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, recursive: true);
            }

            CopyDirectory(OldPath, temp);
            Directory.Move(temp, Path);
        }
        catch (Exception)
        {
            // Not being able to copy them shouldn't stop the app starting; it starts fresh instead.
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetFileName(file)));
        }

        foreach (var folder in Directory.EnumerateDirectories(from))
        {
            CopyDirectory(folder, System.IO.Path.Combine(to, System.IO.Path.GetFileName(folder)));
        }
    }
}
