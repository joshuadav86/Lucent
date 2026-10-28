using System.IO;
using System.Security.Cryptography;
using System.Text;
using Lucent.Settings;

namespace Lucent.Lastfm;

/// <summary>
/// The Last.fm sign-in (its session key), kept in %AppData%\Lucent encrypted with Windows' own
/// per-user protection, so only this Windows account can read it back. (The Mac keeps it in the keychain.)
/// </summary>
public static class LastfmAccount
{
    private static string FilePath => Path.Combine(AppDataFolder.Path, "lastfm-sign-in.dat");

    /// <summary>The saved session key, or null when not signed in (or the file can't be read on this account).</summary>
    public static string? Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser))
                : null;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(string sessionKey)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder.Path);
            File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(sessionKey), null, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or UnauthorizedAccessException)
        {
            // Not saved: Last.fm asks for a sign-in again next time Lucent starts.
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
