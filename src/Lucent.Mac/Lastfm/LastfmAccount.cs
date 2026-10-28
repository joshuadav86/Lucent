using System.Text;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Lastfm;

/// <summary>
/// The Last.fm sign-in (its session key), kept in the user's login keychain, so only this Mac account
/// can read it back. (Windows keeps it in %AppData%\Lucent encrypted with DPAPI instead.)
/// </summary>
public static class LastfmAccount
{
    private const string Service = "Lucent Last.fm sign-in";
    private const string Account = "last.fm";
    private const string Label = "Lucent: Last.fm sign-in";

    // Read once and kept: the keychain can ask the user before handing an item to a newly updated app,
    // and the player asks whether Lucent is signed in when it starts and whenever Options opens.
    private static readonly object Gate = new();
    private static bool _loaded;
    private static string? _token;

    /// <summary>The saved session key, or null when not signed in (or the keychain won't hand it over).</summary>
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

    public static void Save(string sessionKey)
    {
        lock (Gate)
        {
            _token = sessionKey;
            _loaded = true;
        }

        try
        {
            Keychain.Write(Service, Account, Label, Encoding.UTF8.GetBytes(sessionKey));
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            // Not saved: Last.fm asks for a sign-in again next time Lucent starts.
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
