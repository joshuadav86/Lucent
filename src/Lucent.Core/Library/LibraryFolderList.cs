using System.IO;

namespace WinampInspiredVisualizer.Library;

/// <summary>
/// Adds and removes library folders. The library is the linked folders, minus any folders inside them
/// the user took out. Nothing here touches the files themselves.
/// </summary>
public static class LibraryFolderList
{
    /// <summary>Puts a folder and everything in it into the library, bringing back any of it that was taken out.</summary>
    public static void Add(List<string> linked, List<string> hidden, string folder)
    {
        folder = Normalize(folder);
        hidden.RemoveAll(h => Same(h, folder) || IsInside(h, folder));
        if (linked.Any(root => Same(root, folder)))
        {
            return;
        }

        // A folder inside a linked one is back in the library now, unless a folder around it is still taken out.
        var alreadyListed = linked.Any(root => IsInside(folder, root)
            && !hidden.Any(h => IsInside(h, root) && IsInside(folder, h)));
        if (!alreadyListed)
        {
            linked.Add(folder);
        }
    }

    /// <summary>Takes a folder and everything in it out of the library. The files stay where they are.</summary>
    public static void Remove(List<string> linked, List<string> hidden, string folder)
    {
        folder = Normalize(folder);
        linked.RemoveAll(root => Same(root, folder) || IsInside(root, folder));
        hidden.RemoveAll(h => Same(h, folder) || IsInside(h, folder));

        // Inside a folder that stays linked: remember to leave this one out of it.
        if (linked.Any(root => IsInside(folder, root)) && !hidden.Any(h => IsInside(folder, h)))
        {
            hidden.Add(folder);
        }
    }

    /// <summary>Takes the single files in a folder, or in the folders inside it, out of the library.</summary>
    public static void RemoveFilesIn(List<string> files, string folder) =>
        files.RemoveAll(file => IsInside(file, folder));

    /// <summary>The folders taken out of this linked folder.</summary>
    public static List<string> HiddenIn(string root, IEnumerable<string> hidden) =>
        hidden.Where(h => IsInside(h, root)).ToList();

    /// <summary>Whether a path is inside one of these folders.</summary>
    public static bool IsInsideAny(string path, IReadOnlyList<string> folders) =>
        folders.Any(folder => IsInside(path, folder));

    /// <summary>"Music", or the whole path for a drive like D:\.</summary>
    public static string Name(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        return string.IsNullOrEmpty(name) ? folder : name;
    }

    private static bool Same(string a, string b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a path is somewhere below a folder (not the folder itself).</summary>
    private static bool IsInside(string path, string folder)
    {
        path = Normalize(path);
        folder = Normalize(folder);
        var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
        return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string folder)
    {
        try
        {
            folder = Path.GetFullPath(folder);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep it as written.
        }

        return Path.TrimEndingDirectorySeparator(folder);
    }
}
