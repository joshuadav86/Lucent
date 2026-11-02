using System.IO;
using System.Runtime.InteropServices;

namespace Lucent.Updates;

/// <summary>
/// File Explorer windows showing a folder inside Lucent's own program folder (Show shader in the visuals'
/// Advanced menu opens one on a built-in visual). An update swaps that folder for the new build's, and while
/// Explorer has a folder inside it open Windows refuses, so the install fails and the old build opens again.
/// </summary>
internal static class ExplorerWindows
{
    /// <summary>Closes every Explorer window showing <paramref name="folder"/> or a folder inside it.</summary>
    public static void CloseInside(string folder)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)) + Path.DirectorySeparatorChar;
        object? shell = null;
        try
        {
            if (Type.GetTypeFromProgID("Shell.Application") is not { } type)
            {
                return;
            }

            shell = Activator.CreateInstance(type);
            dynamic windows = ((dynamic)shell!).Windows();
            // From the end, since a window that closes leaves the list.
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                try
                {
                    dynamic? window = windows.Item(i);
                    if (window is null)
                    {
                        continue;
                    }

                    string path = window.Document.Folder.Self.Path;
                    var shown = Path.TrimEndingDirectorySeparator(path) + Path.DirectorySeparatorChar;
                    if (shown.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        window.Quit();
                    }
                }
                catch (Exception)
                {
                    // Not a folder window (or it closed meanwhile): leave it.
                }
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Closing Explorer windows in Lucent's folder", ex);
        }
        finally
        {
            if (shell is not null && Marshal.IsComObject(shell))
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
