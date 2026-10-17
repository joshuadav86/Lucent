using System.IO;
using System.Security.Cryptography;
using System.Text;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Stores;

/// <summary>
/// The Bandcamp sign-in (the website's cookies), kept in %AppData%\Lucent encrypted with Windows' own
/// per-user protection, so only this Windows account can read it back.
/// </summary>
public static class BandcampSignIn
{
    private static string FilePath => Path.Combine(AppDataFolder.Path, "bandcamp-sign-in.dat");

    /// <summary>Where the sign-in window keeps its browser data, so signing in again is quick.</summary>
    public static string BrowserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lucent", "Bandcamp browser");

    /// <summary>The saved cookies, or null when not signed in (or the file can't be read on this account).</summary>
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

    public static void Save(string cookies)
    {
        Directory.CreateDirectory(AppDataFolder.Path);
        File.WriteAllBytes(FilePath, ProtectedData.Protect(Encoding.UTF8.GetBytes(cookies), null, DataProtectionScope.CurrentUser));
    }

    /// <summary>Forgets the sign-in, and the sign-in window's browser data with it.</summary>
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

        try
        {
            if (Directory.Exists(BrowserDataFolder))
            {
                Directory.Delete(BrowserDataFolder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The browser may still be closing; the website's own sign-out still applies.
        }
    }
}
