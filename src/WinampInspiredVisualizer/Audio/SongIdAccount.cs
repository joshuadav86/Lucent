using System.IO;
using System.Security.Cryptography;
using System.Text;
using WinampInspiredVisualizer.Recording;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// The DJ's own ACRCloud key for naming the songs in a recording, kept in %AppData%\Lucent encrypted with
/// Windows' own per-user protection, so only this Windows account can read it back. (The Mac keeps it in the keychain.)
/// </summary>
public static class SongIdAccount
{
    private static string FilePath => Path.Combine(AppDataFolder.Path, "acrcloud-key.dat");

    /// <summary>The saved key, or null when none has been entered (or the file can't be read on this account).</summary>
    public static AcrCloudKey? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? AcrCloudKey.FromText(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser)))
                : null;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Saves the key; false if it couldn't be.</summary>
    public static bool Save(AcrCloudKey key)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder.Path);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(key.ToText()), null, DataProtectionScope.CurrentUser));
            return true;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            File.Delete(FilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left behind, it's still only readable by this Windows account.
        }
    }
}
