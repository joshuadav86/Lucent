using System.Text;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Streaming;

/// <summary>
/// The Spotify sign-in (its refresh token), kept in the user's login keychain, so only this Mac account
/// can read it back. (Windows keeps it in %AppData%\Lucent encrypted with DPAPI instead.)
/// </summary>
public static class SpotifyAccount
{
    private const string Service = "Lucent Spotify sign-in";
    private const string Account = "spotify.com";
    private const string Label = "Lucent: Spotify sign-in";

    // Read once and kept: the keychain can ask the user before handing an item to a newly updated app,
    // and the Add music menu asks whether Lucent is signed in every time it opens.
    private static readonly object Gate = new();
    private static bool _loaded;
    private static string? _token;

    /// <summary>The saved refresh token, or null when not signed in (or the keychain won't hand it over).</summary>
    public static string? Load()
    {
        lock (Gate)
        {
            if (!_loaded)
            {
                _token = Read();
                _loaded = true;
            }

            return _token;
        }
    }

    public static void Save(string refreshToken)
    {
        lock (Gate)
        {
            _token = refreshToken;
            _loaded = true;
        }

        try
        {
            Keychain.Write(Service, Account, Label, Encoding.UTF8.GetBytes(refreshToken));
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Not saved: Spotify asks for a sign-in again next time Lucent starts.
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _token = null;
            _loaded = true;
        }

        try
        {
            Keychain.Delete(Service, Account);
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            // If it can't be removed it's still only readable by this Mac account.
        }
    }

    private static string? Read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return Keychain.Read(Service, Account, Label) is { } bytes ? Encoding.UTF8.GetString(bytes) : null;
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
