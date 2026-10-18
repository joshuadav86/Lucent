using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A visual loaded from a .hlsl file in the Presets folder, or a MilkDrop preset from a .milk file.</summary>
public sealed record PresetSource(string Name, string FilePath, string Code)
{
    public const int DefaultParticleCount = 262144;

    private static readonly Regex SimulatePattern = new(@"\bvoid\s+Simulate\s*\(");
    private static readonly Regex ParticleCountPattern = new(@"^\s*//\s*Particles:\s*(\d+)", RegexOptions.Multiline);
    private static readonly Regex FamilyPattern = new(@"^\s*//\s*Family:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex TagsPattern = new(@"^\s*//\s*Tags:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>A MilkDrop preset (a .milk file) rather than a Lucent shader. See Visualizer/MilkDrop.</summary>
    public bool IsMilkDrop { get; } = IsMilkDropFile(FilePath);

    /// <summary>A particle visual: it defines Simulate (and Spawn and Show) as well as Render. See Shaders/Particles.hlsli.</summary>
    public bool IsParticles { get; } = !IsMilkDropFile(FilePath) && SimulatePattern.IsMatch(Code);

    /// <summary>How many particles run on Normal detail, from a "// Particles: 300000" line.</summary>
    public int NormalParticleCount { get; } = ReadParticleCount(Code);

    /// <summary>The folder the visual picker lists it in, from a "// Family: Fractal" line ("Other" without one; "MilkDrop" for MilkDrop presets).</summary>
    public string Family { get; } = IsMilkDropFile(FilePath)
        ? VisualStyles.MilkDropFamily
        : VisualStyles.FamilyName(ReadHeader(FamilyPattern, Code));

    /// <summary>
    /// The picker's tick boxes it answers to, from a "// Tags: Calm, Space" line (none without one).
    /// Particle visuals are always tagged Particles too, and MilkDrop presets Retro.
    /// </summary>
    public IReadOnlyList<string> Tags { get; } = IsMilkDropFile(FilePath)
        ? VisualStyles.TagNames("Retro")
        : VisualStyles.TagNames(ReadHeader(TagsPattern, Code) + (SimulatePattern.IsMatch(Code) ? $", {VisualStyles.ParticlesTag}" : string.Empty));

    /// <summary>A visual from its file, named by its first line.</summary>
    public static PresetSource Load(string path, string code) => new(ReadName(path, code), path, code);

    /// <summary>Whether a file is a MilkDrop preset, by its .milk extension.</summary>
    public static bool IsMilkDropFile(string path) => path.EndsWith(".milk", StringComparison.OrdinalIgnoreCase);

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
