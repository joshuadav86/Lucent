using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A visual loaded from a .hlsl file in the Presets folder.</summary>
public sealed record PresetSource(string Name, string FilePath, string Code)
{
    public const int DefaultParticleCount = 262144;

    private static readonly Regex SimulatePattern = new(@"\bvoid\s+Simulate\s*\(");
    private static readonly Regex ParticleCountPattern = new(@"^\s*//\s*Particles:\s*(\d+)", RegexOptions.Multiline);

    /// <summary>A particle visual: it defines Simulate (and Spawn and Show) as well as Render. See Shaders/Particles.hlsli.</summary>
    public bool IsParticles { get; } = SimulatePattern.IsMatch(Code);

    /// <summary>How many particles run on Normal detail, from a "// Particles: 300000" line.</summary>
    public int NormalParticleCount { get; } = ReadParticleCount(Code);

    private static int ReadParticleCount(string code)
    {
        var match = ParticleCountPattern.Match(code);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0
            ? count
            : DefaultParticleCount;
    }
}

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
