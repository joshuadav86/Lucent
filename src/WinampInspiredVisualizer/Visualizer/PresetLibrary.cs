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

    public static IReadOnlyList<PresetSource> LoadPresets()
    {
        var files = Directory.Exists(PresetsFolder)
            ? Directory.EnumerateFiles(PresetsFolder, "*.hlsl").Select(path => (Path: path, Code: File.ReadAllText(path))).ToList()
            : new List<(string Path, string Code)>();
        if (files.Count == 0)
        {
            files = typeof(PresetLibrary).Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("Presets/", StringComparison.Ordinal))
                .Select(name => (Path: Path.Combine(PresetsFolder, name["Presets/".Length..]), Code: ReadBuiltIn(name)!))
                .ToList();
        }

        if (Directory.Exists(UserPresetsFolder))
        {
            var own = Directory.EnumerateFiles(UserPresetsFolder, "*.hlsl").Select(path => (Path: path, Code: File.ReadAllText(path))).ToList();
            var ownNames = own.Select(file => Path.GetFileName(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files = files.Where(file => !ownNames.Contains(Path.GetFileName(file.Path))).Concat(own).ToList();
        }

        return files
            .Select(file => PresetSource.Load(file.Path, file.Code))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
