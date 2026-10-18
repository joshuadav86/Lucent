using System.IO;

namespace WinampInspiredVisualizer.Visualizer.MilkDrop;

/// <summary>What importing MilkDrop presets did.</summary>
/// <param name="Added">How many presets were copied in.</param>
/// <param name="Skipped">How many files weren't MilkDrop presets (each is in the error log).</param>
/// <param name="First">The name of the first preset copied in, to show it straight away.</param>
public sealed record MilkDropImportResult(int Added, int Skipped, string? First);

/// <summary>
/// Copies MilkDrop presets into the user's MilkDrop folder (<see cref="PresetLibrary.MilkDropFolder"/>),
/// where they load as visuals from then on. Each file is read first, so one that isn't a preset
/// is left behind (and noted in the error log) rather than turning up broken in the list.
/// </summary>
public static class MilkDropImport
{
    /// <summary>Copies the given .milk files in.</summary>
    public static MilkDropImportResult ImportFiles(IEnumerable<string> paths) =>
        Import(paths.Select(path => (Source: path, Relative: Path.GetFileName(path))));

    /// <summary>Copies in every .milk file in a folder and the folders inside it, keeping them together in a folder of the same name.</summary>
    public static MilkDropImportResult ImportFolder(string folder)
    {
        var name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var files = Directory.EnumerateFiles(folder, "*.milk", SearchOption.AllDirectories)
            .Select(path => (Source: path, Relative: Path.Combine(name, Path.GetRelativePath(folder, path))));
        return Import(files);
    }

    private static MilkDropImportResult Import(IEnumerable<(string Source, string Relative)> files)
    {
        var destinationFolder = Path.GetFullPath(PresetLibrary.MilkDropFolder);
        var added = 0;
        var skipped = 0;
        string? first = null;
        foreach (var (source, relative) in files)
        {
            try
            {
                MilkPreset.Parse(File.ReadAllText(source), Path.GetFileNameWithoutExtension(source));
                var destination = Path.GetFullPath(Path.Combine(destinationFolder, relative));
                if (!string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(source, destination, overwrite: true);
                }

                added++;
                first ??= Path.GetFileNameWithoutExtension(source);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                skipped++;
                ErrorLog.Write($"MilkDrop preset {Path.GetFileName(source)} wasn't imported", ex.Message);
            }
        }

        return new MilkDropImportResult(added, skipped, first);
    }
}
