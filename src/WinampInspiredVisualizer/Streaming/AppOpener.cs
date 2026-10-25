using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Windows.Management.Deployment;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Finds and starts the apps Lucent controls, for the player's Open button (<see cref="IRemoteOpener"/>): by a link
/// the app answers to (spotify:), by where its installer puts it, or as a Microsoft Store app.
/// </summary>
/// <remarks>The Mac starts apps with <c>open</c> instead (see the Mac's Streaming/AppOpener.cs).</remarks>
internal static class AppOpener
{
    /// <summary>True when a process named one of <paramref name="names"/> (as Task Manager names it, without .exe) is running.</summary>
    public static bool IsRunning(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var process in processes)
            {
                process.Dispose();
            }

            if (processes.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Opens a link like spotify: when an app on this PC answers to it. False when none does, as Windows would otherwise
    /// ask the user to find an app in the Store.
    /// </summary>
    public static bool TryLink(string scheme)
    {
        using var key = Registry.ClassesRoot.OpenSubKey(scheme);
        return key?.GetValue("URL Protocol") is not null && TryStart(scheme + ":");
    }

    /// <summary>Starts the program at <paramref name="path"/>, when it's there.</summary>
    public static bool TryProgram(string path) => File.Exists(path) && TryStart(path);

    /// <summary>
    /// Starts the Microsoft Store app whose package family name starts with <paramref name="family"/> (its publisher's
    /// name and the app's, such as "AppleInc.AppleMusicWin"), when it's installed.
    /// </summary>
    public static async Task<bool> TryStoreAppAsync(string family)
    {
        try
        {
            var packages = await Task.Run(() => new PackageManager().FindPackagesForUser(string.Empty)
                .Where(package => package.Id.FamilyName.StartsWith(family, StringComparison.OrdinalIgnoreCase))
                .ToList());
            foreach (var package in packages)
            {
                var entries = await package.GetAppListEntriesAsync();
                if (entries.Count > 0 && await entries[0].LaunchAsync())
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't start {family} as a Store app: {ex}");
        }

        return false;
    }

    /// <summary>Opens <paramref name="url"/> in the default web browser.</summary>
    public static void OpenWebsite(string url)
    {
        if (!TryStart(url))
        {
            throw new RemoteProblem($"Lucent couldn't open {url} in your web browser.");
        }
    }

    private static bool TryStart(string target)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't open {target}: {ex}");
            return false;
        }
    }
}
