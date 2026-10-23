using System.IO;

namespace WinampInspiredVisualizer.Settings;

/// <summary>
/// Before 1.0, a build can give everyone who updates the same first start a new install gets: just the
/// player, the equalizer and the visuals, with "Self Actualize" playing. The old settings, library,
/// favourites, playlists and visuals are moved aside to a "Lucent before fresh start" folder beside the
/// data folder, not deleted, so they can be put back by hand. Sign-ins and the stem model download stay.
/// </summary>
public static class FreshStart
{
    /// <summary>
    /// 0 keeps everyone's settings between updates (Joshua's call, 2026-10-23). Raising it above 0 would give
    /// everyone a fresh start once on their next update.
    /// </summary>
    public static readonly int Generation = 0;

    /// <summary>What a fresh start leaves in the data folder: sign-ins and downloads, not preferences.</summary>
    private static readonly string[] Kept =
    {
        "Stems",
        "keychain-owners.txt",
        "spotify-sign-in.dat",
        "bandcamp-sign-in.dat",
        "acrcloud-key.dat",
    };

    private static string MarkerPath => System.IO.Path.Combine(AppDataFolder.Path, "fresh-start.txt");

    /// <summary>
    /// Moves an existing data folder aside once per <see cref="Generation"/>. A new install just gets the marker.
    /// Runs before anything opens a file in the data folder.
    /// </summary>
    public static void Apply()
    {
        // The screenshot and launch checks use folders of their own, set up the way they need them.
        if (Generation <= 0 || AppDataFolder.IsCustom)
        {
            return;
        }

        try
        {
            if (Directory.Exists(AppDataFolder.Path) && ReadMarker() < Generation && HasPreferences())
            {
                MoveAside();
            }

            Directory.CreateDirectory(AppDataFolder.Path);
            File.WriteAllText(MarkerPath, Generation.ToString());
        }
        catch (Exception)
        {
            // If the folder can't be moved (a file in it is open), the old settings carry on and it's tried next time.
        }
    }

    private static int ReadMarker()
    {
        try
        {
            return File.Exists(MarkerPath) && int.TryParse(File.ReadAllText(MarkerPath).Trim(), out var generation) ? generation : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>Whether there's anything to start fresh from, rather than just a folder an earlier start made.</summary>
    private static bool HasPreferences() =>
        Directory.EnumerateFileSystemEntries(AppDataFolder.Path)
            .Any(entry => !Kept.Contains(System.IO.Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase));

    private static void MoveAside()
    {
        var parent = System.IO.Path.GetDirectoryName(AppDataFolder.Path)!;
        var name = $"Lucent before fresh start {DateTime.Now:yyyy-MM-dd}";
        var backup = System.IO.Path.Combine(parent, name);
        for (var n = 2; Directory.Exists(backup); n++)
        {
            backup = System.IO.Path.Combine(parent, $"{name} ({n})");
        }

        Directory.Move(AppDataFolder.Path, backup);
        Directory.CreateDirectory(AppDataFolder.Path);
        foreach (var kept in Kept)
        {
            var from = System.IO.Path.Combine(backup, kept);
            var to = System.IO.Path.Combine(AppDataFolder.Path, kept);
            try
            {
                if (Directory.Exists(from))
                {
                    Directory.Move(from, to);
                }
                else if (File.Exists(from))
                {
                    File.Move(from, to);
                }
            }
            catch (Exception)
            {
                // Losing one of these only means signing in again or downloading the model again.
            }
        }
    }
}
