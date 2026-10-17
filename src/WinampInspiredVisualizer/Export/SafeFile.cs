using System.IO;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Writing files without ever leaving a half-written one behind: everything is written to a
/// hidden temporary file in the same folder first, and only swapped into place once it is complete.
/// If anything fails, the original file is untouched.
/// </summary>
public static class SafeFile
{
    /// <summary>A temporary file next to <paramref name="finalPath"/>, keeping its extension so decoders and TagLib recognise it.</summary>
    public static string TemporaryPathFor(string finalPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(finalPath)) ?? ".";
        var name = Path.GetFileNameWithoutExtension(finalPath);
        if (name.Length > 120)
        {
            name = name[..120];
        }

        var unique = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(folder, $".{name}.lucent-{unique}{Path.GetExtension(finalPath)}");
    }

    /// <summary>Hides a finished temporary file, so the library doesn't list it while it is checked.</summary>
    public static void Hide(string temporaryPath)
    {
        try
        {
            File.SetAttributes(temporaryPath, File.GetAttributes(temporaryPath) | FileAttributes.Hidden);
        }
        catch (Exception)
        {
            // Only cosmetic.
        }
    }

    /// <summary>
    /// Moves a finished temporary file into place. With <paramref name="replace"/>, an existing file at
    /// <paramref name="finalPath"/> is swapped out in one step (it keeps its place, and is never left truncated);
    /// otherwise an existing file makes this fail.
    /// </summary>
    public static void Commit(string temporaryPath, string finalPath, bool replace)
    {
        File.SetAttributes(temporaryPath, File.GetAttributes(temporaryPath) & ~FileAttributes.Hidden);
        if (replace && File.Exists(finalPath))
        {
            // ReplaceFile on Windows: the new file takes the old one's name in a single operation.
            File.Replace(temporaryPath, finalPath, destinationBackupFileName: null, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(temporaryPath, finalPath, overwrite: false);
        }
    }

    /// <summary>Deletes a temporary file, ignoring problems (it may never have been created).</summary>
    public static void Discard(string temporaryPath)
    {
        try
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
        catch (Exception)
        {
            // A leftover hidden temp file is harmless.
        }
    }

    /// <summary>True for errors that mean another program (or Lucent's own player) has the file open.</summary>
    public static bool IsFileInUse(Exception ex) =>
        ex is IOException io && (io.HResult & 0xFFFF) is 32 or 33; // ERROR_SHARING_VIOLATION, ERROR_LOCK_VIOLATION
}
