using System.IO;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Finds the preset and engine shader files that ship next to the app. Lucent.exe carries its own
/// copies too, used when it's run without its folder (straight from the zip, say).
/// </summary>
public static class PresetLibrary
{
    public static string PresetsFolder => Path.Combine(AppContext.BaseDirectory, "Presets");

    public static string ShadersFolder => Path.Combine(AppContext.BaseDirectory, "Shaders");

    /// <summary>
    /// The user's own visuals, in %AppData%\Lucent\Presets. They load alongside the built-in ones (one
    /// with the same file name replaces it) and, unlike the app's folder, survive updates.
    /// </summary>
    public static string UserPresetsFolder => Path.Combine(AppDataFolder.Path, "Presets");

    public static string ReadShader(string fileName)
    {
        var path = Path.Combine(ShadersFolder, fileName);
        return File.Exists(path) ? File.ReadAllText(path) : ReadBuiltIn($"Shaders/{fileName}")
            ?? throw new FileNotFoundException($"The {fileName} shader is missing.", path);
    }

    /// <summary>
    /// Where the user's MilkDrop presets go, %AppData%\Lucent\Presets\MilkDrop ("Import MilkDrop presets…" copies
    /// them here). .milk files anywhere under either Presets folder load too, so a whole pack's folder can be dropped in.
    /// </summary>
    public static string MilkDropFolder => Path.Combine(UserPresetsFolder, "MilkDrop");

    public static IReadOnlyList<PresetSource> LoadPresets()
    {
        var files = Read(PresetsFolder);
        if (files.Count == 0)
        {
            files = typeof(PresetLibrary).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("Presets/", StringComparison.Ordinal))
                .Select(name => (Path: Path.Combine(PresetsFolder, name["Presets/".Length..]), Code: ReadBuiltIn(name)!))
                .ToList();
        }

        var own = Read(UserPresetsFolder);
        if (own.Count > 0)
        {
            var ownNames = own.Select(file => Path.GetFileName(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files = files.Where(file => !ownNames.Contains(Path.GetFileName(file.Path))).Concat(own).ToList();
        }

        return PresetSource.WithUniqueNames(files
            .Select(file => PresetSource.Load(file.Path, file.Code))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(preset => preset.Author, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>The .hlsl visuals in a folder, and the MilkDrop presets in it and in the folders inside it.</summary>
    private static List<(string Path, string Code)> Read(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return new List<(string Path, string Code)>();
        }

        var files = Directory.EnumerateFiles(folder, "*.hlsl").Select(path => (Path: path, Code: File.ReadAllText(path))).ToList();
        try
        {
            files.AddRange(Directory.EnumerateFiles(folder, "*.milk", SearchOption.AllDirectories)
                .Select(path => (Path: path, Code: File.ReadAllText(path))));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preset pack that can't be read: the visuals that could be still load.
            ErrorLog.Write("MilkDrop presets", ex);
        }

        return files;
    }

    private static string? ReadBuiltIn(string name)
    {
        using var stream = typeof(PresetLibrary).Assembly.GetManifestResourceStream(name);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
