using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer;

/// <summary>
/// Tells Windows that Lucent plays music and video files, so it's listed in Default apps and Open with. Windows
/// doesn't let an app make itself the default: <see cref="OpenDefaultAppsSettings"/> opens the page
/// where people choose it. Everything goes under the current user's part of the registry, so there's
/// nothing to ask permission for, and it's taken out again when Lucent is uninstalled.
/// </summary>
public static class FileAssociations
{
    private const string ProgId = "Lucent.AudioFile";
    private const string VideoProgId = "Lucent.VideoFile";
    private const string AppName = "Lucent";
    private const string CapabilitiesKey = @"Software\Lucent\Capabilities";

    // Every music format Lucent plays except MP4, which is usually a video.
    private static IEnumerable<string> Extensions =>
        AudioReaderFactory.SupportedExtensions.Where(extension => extension != ".mp4").Order();

    // The videos, MP4 among them, as their own file type ("Video file").
    private static IEnumerable<string> VideoExtensions => VideoFiles.Extensions.Order();

    private static IEnumerable<(string Extension, string ProgId)> AllTypes =>
        Extensions.Select(extension => (extension, ProgId)).Concat(VideoExtensions.Select(extension => (extension, VideoProgId)));

    /// <summary>Lists Lucent, as it is now (its .exe), for every music and video file type it plays.</summary>
    public static void Register()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            return;
        }

        var command = $"\"{exe}\" \"%1\"";
        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        foreach (var (progId, name) in new[] { (ProgId, "Music file"), (VideoProgId, "Video file") })
        {
            using var type = classes.CreateSubKey(progId);
            type.SetValue("", name);
            using var icon = type.CreateSubKey("DefaultIcon");
            icon.SetValue("", $"\"{exe}\",0");
            using var open = type.CreateSubKey(@"shell\open\command");
            open.SetValue("", command);
        }

        using (var app = classes.CreateSubKey($@"Applications\{AppName}.exe"))
        {
            app.SetValue("FriendlyAppName", AppName);
            using var open = app.CreateSubKey(@"shell\open\command");
            open.SetValue("", command);
            using var supported = app.CreateSubKey("SupportedTypes");
            foreach (var (extension, _) in AllTypes)
            {
                supported.SetValue(extension, "");
            }
        }

        foreach (var (extension, progId) in AllTypes)
        {
            using var openWith = classes.CreateSubKey($@"{extension}\OpenWithProgids");
            openWith.SetValue(progId, Array.Empty<byte>(), RegistryValueKind.None);
        }

        // What Default apps lists: Lucent, and the file types it can be chosen for.
        using (var capabilities = Registry.CurrentUser.CreateSubKey(CapabilitiesKey))
        {
            capabilities.SetValue("ApplicationName", AppName);
            capabilities.SetValue("ApplicationDescription", "Plays your music, with visuals that move to it, and your videos.");
            capabilities.SetValue("ApplicationIcon", $"\"{exe}\",0");
            using var associations = capabilities.CreateSubKey("FileAssociations");
            foreach (var (extension, progId) in AllTypes)
            {
                associations.SetValue(extension, progId);
            }
        }

        using (var registered = Registry.CurrentUser.CreateSubKey(@"Software\RegisteredApplications"))
        {
            registered.SetValue(AppName, CapabilitiesKey);
        }

        SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Takes Lucent out of Default apps and Open with, when it's uninstalled.</summary>
    public static void Unregister()
    {
        using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
        classes?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        classes?.DeleteSubKeyTree(VideoProgId, throwOnMissingSubKey: false);
        classes?.DeleteSubKeyTree($@"Applications\{AppName}.exe", throwOnMissingSubKey: false);
        foreach (var (extension, progId) in AllTypes)
        {
            using var openWith = classes?.OpenSubKey($@"{extension}\OpenWithProgids", writable: true);
            openWith?.DeleteValue(progId, throwOnMissingValue: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree(@"Software\Lucent", throwOnMissingSubKey: false);
        using var registered = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications", writable: true);
        registered?.DeleteValue(AppName, throwOnMissingValue: false);
        SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>
    /// Opens Settings at Lucent's page in Default apps (on Windows 10, Default apps itself), where
    /// people choose it for their music. Registers Lucent first, so it's there to choose.
    /// </summary>
    public static void OpenDefaultAppsSettings()
    {
        Register();
        Process.Start(new ProcessStartInfo($"ms-settings:defaultapps?registeredAppUser={AppName}") { UseShellExecute = true });
    }

    private const int ShcneAssocChanged = 0x08000000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
