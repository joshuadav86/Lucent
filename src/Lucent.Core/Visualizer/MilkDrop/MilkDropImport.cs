namespace WinampInspiredVisualizer.Visualizer.MilkDrop;

/// <summary>What importing MilkDrop presets did.</summary>
/// <param name="Added">How many presets were copied in.</param>
/// <param name="Skipped">The files that weren't MilkDrop presets, or couldn't be copied, and why, for the error log.</param>
/// <param name="First">The name of the first preset copied in, to show it straight away.</param>
public sealed record MilkDropImportResult(int Added, IReadOnlyList<(string File, string Reason)> Skipped, string? First);

/// <summary>
/// Copies MilkDrop presets into the user's MilkDrop folder (in their own Presets folder), where they load
/// as visuals from then on. Each file is read first, so one that isn't a preset is left behind (and listed
/// in the result for the error log) rather than turning up broken in the list.
/// </summary>
public static class MilkDropImport
{
    /// <summary>Copies the given .milk files into <paramref name="destination"/>.</summary>
    public static MilkDropImportResult ImportFiles(IEnumerable<string> paths, string destination) =>
        Import(paths.Select(path => (Source: path, Relative: Path.GetFileName(path))), destination);

    /// <summary>Copies in every .milk file in a folder and the folders inside it, keeping them together in a folder of the same name.</summary>
    public static MilkDropImportResult ImportFolder(string folder, string destination)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var files = Directory.EnumerateFiles(folder, "*.milk", SearchOption.AllDirectories)
            .Select(path => (Source: path, Relative: Path.Combine(name, Path.GetRelativePath(folder, path))));
        return Import(files, destination);
    }

    private static MilkDropImportResult Import(IEnumerable<(string Source, string Relative)> files, string destination)
    {
        var destinationFolder = Path.GetFullPath(destination);
        var added = 0;
        var skipped = new List<(string File, string Reason)>();
        string? first = null;
        foreach (var (source, relative) in files)
        {
            try
            {
                MilkPreset.Parse(File.ReadAllText(source), Path.GetFileNameWithoutExtension(source));
                var target = Path.GetFullPath(Path.Combine(destinationFolder, relative));
                if (!string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(source, target, overwrite: true);
                }

                added++;
                first ??= Path.GetFileNameWithoutExtension(source);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                skipped.Add((Path.GetFileName(source), ex.Message));
            }
        }

        return new MilkDropImportResult(added, skipped, first);
    }
}
