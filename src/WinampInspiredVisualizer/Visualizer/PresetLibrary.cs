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
    private static readonly Regex FamilyPattern = new(@"^\s*//\s*Family:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex TagsPattern = new(@"^\s*//\s*Tags:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>A particle visual: it defines Simulate (and Spawn and Show) as well as Render. See Shaders/Particles.hlsli.</summary>
    public bool IsParticles { get; } = SimulatePattern.IsMatch(Code);

    /// <summary>How many particles run on Normal detail, from a "// Particles: 300000" line.</summary>
    public int NormalParticleCount { get; } = ReadParticleCount(Code);

    /// <summary>The folder the visual picker lists it in, from a "// Family: Fractal" line ("Other" without one).</summary>
    public string Family { get; } = VisualStyles.FamilyName(ReadHeader(FamilyPattern, Code));

    /// <summary>
    /// The picker's tick boxes it answers to, from a "// Tags: Calm, Space" line (none without one).
    /// Particle visuals are always tagged Particles too.
    /// </summary>
    public IReadOnlyList<string> Tags { get; } = VisualStyles.TagNames(
        ReadHeader(TagsPattern, Code) + (SimulatePattern.IsMatch(Code) ? $", {VisualStyles.ParticlesTag}" : string.Empty));

    private static string? ReadHeader(Regex pattern, string code)
    {
        var match = pattern.Match(code);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static int ReadParticleCount(string code)
    {
        var match = ParticleCountPattern.Match(code);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0
            ? count
            : DefaultParticleCount;
    }
}

/// <summary>
/// Finds the preset and engine shader files that ship next to the app. Lucid.exe carries its own
/// copies too, used when it's run without its folder (straight from the zip, say).
/// </summary>
public static class PresetLibrary
{
    public static string PresetsFolder => Path.Combine(AppContext.BaseDirectory, "Presets");

    public static string ShadersFolder => Path.Combine(AppContext.BaseDirectory, "Shaders");

    /// <summary>
    /// The user's own visuals, in %AppData%\Lucid\Presets. They load alongside the built-in ones (one
    /// with the same file name replaces it) and, unlike the app's folder, survive updates.
    /// </summary>
    public static string UserPresetsFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Lucid", "Presets");

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
            .Select(file => new PresetSource(ReadName(file.Path, file.Code), file.Path, file.Code))
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

    /// <summary>Uses a "// Name: ..." first line if present, otherwise the file name.</summary>
    private static string ReadName(string path, string code)
    {
        var firstLine = new StringReader(code).ReadLine()?.Trim() ?? string.Empty;
        const string prefix = "// Name:";
        return firstLine.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? firstLine[prefix.Length..].Trim()
            : Path.GetFileNameWithoutExtension(path);
    }
}
