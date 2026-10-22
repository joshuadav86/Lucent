using System.Text;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Stores;

/// <summary>
/// The Bandcamp sign-in (the website's cookies), kept in the user's login keychain, so only this Mac
/// account can read it back. (Windows keeps it in %AppData%\Lucent encrypted with DPAPI instead.)
/// </summary>
public static class BandcampSignIn
{
    private const string Service = "Lucent Bandcamp sign-in";
    private const string Account = "bandcamp.com";
    private const string Label = "Lucent: Bandcamp sign-in";

    // Read once and kept: the keychain can ask the user before handing an item to a newly updated app,
    // and the Bandcamp window asks whether Lucent is signed in every time it redraws.
    private static readonly object Gate = new();
    private static bool _loaded;
    private static string? _cookies;

    /// <summary>The saved cookies, or null when not signed in (or the keychain won't hand them over).</summary>
    public static string? Load()
    {
        lock (Gate)
        {
            if (!_loaded)
            {
                _cookies = Read();
                _loaded = true;
            }

            return _cookies;
        }
    }

    /// <summary>Saves the cookies in the keychain. Throws <see cref="KeychainException"/> if the keychain refuses.</summary>
    public static void Save(string cookies)
    {
        Keychain.Write(Service, Account, Label, Encoding.UTF8.GetBytes(cookies));
        lock (Gate)
        {
            _cookies = cookies;
            _loaded = true;
        }
    }

    /// <summary>For the screenshot tool: acts as signed in with these cookies, without touching the keychain.</summary>
    internal static void UseForPreview(string? cookies)
    {
        lock (Gate)
        {
            _cookies = cookies;
            _loaded = true;
        }
    }

    /// <summary>Forgets the sign-in, and the sign-in window's browser data with it.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _cookies = null;
            _loaded = true;
        }

        try
        {
            // If it can't be removed it's still only readable by this Mac account.
            Keychain.Delete(Service, Account);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not on a Mac (the screenshot tool): there's no keychain to clear.
        }

        // The browser's cookies too; the website's own sign-out still applies if this can't happen.
        BandcampSignInWindow.ClearBrowserData();
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
