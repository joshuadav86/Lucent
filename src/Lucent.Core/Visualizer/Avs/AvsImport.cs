using System.IO;

namespace Lucent.Visualizer.Avs;

/// <summary>What importing AVS presets did.</summary>
/// <param name="Added">How many presets were copied in.</param>
/// <param name="Skipped">The files that weren't AVS presets, or couldn't be copied, and why, for the error log.</param>
/// <param name="First">The name of the first preset copied in, to show it straight away.</param>
public sealed record AvsImportResult(int Added, IReadOnlyList<(string File, string Reason)> Skipped, string? First);

/// <summary>
/// Copies Winamp AVS presets into the user's AVS folder (in their own Presets folder), where they load as
/// visuals from then on. Each file is read first, so one that isn't a preset is left behind (and listed in
/// the result for the error log) rather than turning up broken in the list. A whole pack's folder brings
/// its pictures (.bmp files) along, as presets' Picture and Texer effects draw them.
/// </summary>
public static class AvsImport
{
    /// <summary>Copies the given .avs files into <paramref name="destination"/>, with any pictures beside them.</summary>
    public static AvsImportResult ImportFiles(IEnumerable<string> paths, string destination)
    {
        var files = paths.ToList();
        var result = Import(files.Select(path => (Source: path, Relative: Path.GetFileName(path))), destination);
        foreach (var folder in files.Select(Path.GetDirectoryName).Distinct())
        {
            if (folder is not null)
            {
                CopyPictures(Directory.EnumerateFiles(folder, "*.bmp").Select(path => (path, Path.GetFileName(path))), destination);
            }
        }

        return result;
    }

    /// <summary>Copies in every .avs file in a folder and the folders inside it, keeping them together in a folder of the same name.</summary>
    public static AvsImportResult ImportFolder(string folder, string destination)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        (string, string) Relative(string path) => (path, Path.Combine(name, Path.GetRelativePath(folder, path)));
        var result = Import(Directory.EnumerateFiles(folder, "*.avs", SearchOption.AllDirectories).Select(Relative), destination);
        if (result.Added > 0)
        {
            CopyPictures(Directory.EnumerateFiles(folder, "*.bmp", SearchOption.AllDirectories).Select(Relative), destination);
        }

        return result;
    }

    private static AvsImportResult Import(IEnumerable<(string Source, string Relative)> files, string destination)
    {
        var added = 0;
        var skipped = new List<(string File, string Reason)>();
        string? first = null;
        foreach (var (source, relative) in files)
        {
            try
            {
                AvsPreset.Parse(File.ReadAllBytes(source), Path.GetFileNameWithoutExtension(source));
                Copy(source, relative, destination);
                added++;
                first ??= Path.GetFileNameWithoutExtension(source);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                skipped.Add((Path.GetFileName(source), ex.Message));
            }
        }

        return new AvsImportResult(added, skipped, first);
    }

    private static void CopyPictures(IEnumerable<(string Source, string Relative)> pictures, string destination)
    {
        foreach (var (source, relative) in pictures)
        {
            try
            {
                Copy(source, relative, destination);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Pictures are a bonus: one that can't be copied leaves its effect drawing nothing, as in AVS.
            }
        }
    }

    private static void Copy(string source, string relative, string destination)
    {
        var target = Path.GetFullPath(Path.Combine(Path.GetFullPath(destination), relative));
        if (!string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
        }
    }
}
