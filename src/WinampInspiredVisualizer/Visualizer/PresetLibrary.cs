using System.IO;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A visual loaded from a .hlsl file in the Presets folder.</summary>
public sealed record PresetSource(string Name, string FilePath, string Code);

/// <summary>Finds the preset and engine shader files that ship next to the app.</summary>
public static class PresetLibrary
{
    public static string PresetsFolder => Path.Combine(AppContext.BaseDirectory, "Presets");

    public static string ShadersFolder => Path.Combine(AppContext.BaseDirectory, "Shaders");

    public static string ReadShader(string fileName) => File.ReadAllText(Path.Combine(ShadersFolder, fileName));

    public static IReadOnlyList<PresetSource> LoadPresets()
    {
        if (!Directory.Exists(PresetsFolder))
        {
            return Array.Empty<PresetSource>();
        }

        return Directory.EnumerateFiles(PresetsFolder, "*.hlsl")
            .Select(path => new PresetSource(ReadName(path), path, File.ReadAllText(path)))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Uses a "// Name: ..." first line if present, otherwise the file name.</summary>
    private static string ReadName(string path)
    {
        var firstLine = File.ReadLines(path).FirstOrDefault()?.Trim() ?? string.Empty;
        const string prefix = "// Name:";
        return firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? firstLine[prefix.Length..].Trim()
            : Path.GetFileNameWithoutExtension(path);
    }
}
