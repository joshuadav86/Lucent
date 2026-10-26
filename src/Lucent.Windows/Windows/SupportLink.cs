using System.Diagnostics;

namespace WinampInspiredVisualizer;

/// <summary>Where people can chip in for Lucent: the Buy Me a Coffee page behind the options panel's coffee button.</summary>
public static class SupportLink
{
    public const string Url = "https://buymeacoffee.com/joshuadav86";

    /// <summary>Opens the page in the default browser.</summary>
    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser to hand it to. Nothing else in Lucent depends on it.
        }
    }
}
