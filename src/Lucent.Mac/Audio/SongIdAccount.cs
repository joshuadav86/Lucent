using System.Text;
using Lucent.Mac.Interop;
using Lucent.Recording;

namespace Lucent.Mac.Audio;

/// <summary>
/// The DJ's own ACRCloud key for naming the songs in a recording, kept in the user's login keychain, so only
/// this Mac account can read it back. (Windows keeps it in %AppData%\Lucent encrypted with DPAPI instead.)
/// </summary>
public static class SongIdAccount
{
    private const string Service = "Lucent ACRCloud key";
    private const string Account = "acrcloud.com";
    private const string Label = "Lucent: ACRCloud key for song identification";

    /// <summary>The saved key, or null when none has been entered (or the keychain won't hand it over).</summary>
    public static AcrCloudKey? Load()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return Keychain.Read(Service, Account, Label) is { } bytes ? AcrCloudKey.FromText(Encoding.UTF8.GetString(bytes)) : null;
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Saves the key; false if it couldn't be.</summary>
    public static bool Save(AcrCloudKey key)
    {
        try
        {
            Keychain.Write(Service, Account, Label, Encoding.UTF8.GetBytes(key.ToText()));
            return true;
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    public static void Clear()
    {
        try
        {
            Keychain.Delete(Service, Account);
        }
        catch (Exception ex) when (ex is KeychainException or DllNotFoundException or EntryPointNotFoundException)
        {
            // If it can't be removed it's still only readable by this Mac account.
        }
    }
}
