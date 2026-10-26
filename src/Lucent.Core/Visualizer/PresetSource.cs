using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucent.Visualizer;

/// <summary>
/// A visual loaded from a .hlsl file in the Presets folder, a MilkDrop preset from a .milk file, or a Winamp
/// AVS preset from a .avs file. An AVS preset is binary, so its code is the file's bytes as Latin-1 characters
/// (see <see cref="ReadCode"/>).
/// </summary>
public sealed record PresetSource(string Name, string FilePath, string Code)
{
    public const int DefaultParticleCount = 262144;

    private static readonly Regex SimulatePattern = new(@"\bvoid\s+Simulate\s*\(");
    private static readonly Regex ParticleCountPattern = new(@"^\s*//\s*Particles:\s*(\d+)", RegexOptions.Multiline);
    private static readonly Regex FamilyPattern = new(@"^\s*//\s*Family:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex TagsPattern = new(@"^\s*//\s*Tags:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex AddedPattern = new(@"^\s*//\s*Added:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>A MilkDrop preset (a .milk file) rather than a Lucent shader. See Visualizer/MilkDrop.</summary>
    public bool IsMilkDrop { get; } = IsMilkDropFile(FilePath);

    /// <summary>A Winamp AVS preset (a .avs file) rather than a Lucent shader. See Visualizer/Avs.</summary>
    public bool IsAvs { get; } = IsAvsFile(FilePath);

    /// <summary>A preset from Winamp's world (MilkDrop or AVS), named "Author - Title" and listed in its own folder.</summary>
    public bool IsCommunityPreset => IsMilkDrop || IsAvs;

    /// <summary>One of the user's own visuals, from their Presets folder rather than the app's.</summary>
    public bool IsOwn { get; init; }

    /// <summary>
    /// One of Lucent's original shaders, the .hlsl visuals that ship with the app (not MilkDrop or AVS presets,
    /// and not the user's own), which "Lucent originals" switches on or off.
    /// </summary>
    public bool IsLucentOriginal => !IsCommunityPreset && !IsOwn;

    /// <summary>A particle visual: it defines Simulate (and Spawn and Show) as well as Render. See Shaders/Particles.hlsli.</summary>
    public bool IsParticles { get; } = !IsMilkDropFile(FilePath) && !IsAvsFile(FilePath) && SimulatePattern.IsMatch(Code);

    /// <summary>How many particles run on Normal detail, from a "// Particles: 300000" line.</summary>
    public int NormalParticleCount { get; } = ReadParticleCount(Code);

    /// <summary>
    /// The folder the visual picker lists it in, from a "// Family: Fractal" line ("Other" without one; "MilkDrop"
    /// for MilkDrop presets, "Winamp AVS" for AVS presets).
    /// </summary>
    public string Family { get; } = IsMilkDropFile(FilePath) ? VisualStyles.MilkDropFamily
        : IsAvsFile(FilePath) ? VisualStyles.AvsFamily
        : VisualStyles.FamilyName(ReadHeader(FamilyPattern, Code));

    /// <summary>
    /// The picker's tick boxes it answers to, from a "// Tags: Calm, Space" line (none without one).
    /// Particle visuals are always tagged Particles too, and MilkDrop and AVS presets Retro and MilkDrop or Winamp AVS,
    /// so they can be picked out with a tick box.
    /// </summary>
    public IReadOnlyList<string> Tags { get; } = IsMilkDropFile(FilePath) ? VisualStyles.TagNames($"Retro, {VisualStyles.MilkDropFamily}")
        : IsAvsFile(FilePath) ? VisualStyles.TagNames($"Retro, {VisualStyles.AvsFamily}")
        : VisualStyles.TagNames(ReadHeader(TagsPattern, Code) + (SimulatePattern.IsMatch(Code) ? $", {VisualStyles.ParticlesTag}" : string.Empty));

    /// <summary>
    /// When it joined Lucent's visuals, from a "// Added: 2026-10-17 21:37" line, for the picker's "Newest first"
    /// order. Without one (the user's own visuals, MilkDrop and AVS presets) it's when the file was made, or null if
    /// there's no file to ask.
    /// </summary>
    public DateTime? Added { get; } = ReadAdded(FilePath, Code);

    /// <summary>
    /// Who made it, for MilkDrop and AVS presets named the community's way ("Geiss - Reaction Diffusion.milk"),
    /// or null. The name is then just the title.
    /// </summary>
    public string? Author { get; init; }

    /// <summary>The name with its author, as MilkDrop listed it ("Reaction Diffusion by Geiss"), for showing on screen.</summary>
    public string Label => LabelFor(Name, Author);

    /// <summary>"Title by Author", or just the name when there's no author or the name already carries it ("Tunnel (Geiss)").</summary>
    public static string LabelFor(string name, string? author) =>
        author is null || name.EndsWith($"({author})", StringComparison.OrdinalIgnoreCase) ? name : $"{name} by {author}";

    /// <summary>
    /// A visual from its file, named by its first line (MilkDrop and AVS presets by their file name). Own says it
    /// came from the user's Presets folder.
    /// </summary>
    public static PresetSource Load(string path, string code, bool own = false)
    {
        if (!IsMilkDropFile(path) && !IsAvsFile(path))
        {
            return new(ReadName(path, code), path, code) { IsOwn = own };
        }

        var (title, author) = MilkDropName(path);
        return new(title, path, code) { Author = author, IsOwn = own };
    }

    /// <summary>
    /// The title and author in a MilkDrop or AVS preset's file name. Preset packs name them "Author - Title" (and
    /// collaborations "Author1 + Author2 - Title"); a name without " - " is all title.
    /// </summary>
    public static (string Title, string? Author) MilkDropName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path).Trim();
        if (UnusualNames.TryGetValue(name, out var known))
        {
            return known;
        }

        var split = name.IndexOf(" - ", StringComparison.Ordinal);
        if (split <= 0)
        {
            return (name, null);
        }

        // "Phat_shifter" is two people, as MilkDrop's forums wrote collaborations.
        var author = name[..split].Trim().Replace("_", " + ", StringComparison.Ordinal);
        var title = name[(split + 3)..].Trim();
        return title.Length == 0 || author.Length == 0 ? (name, null) : (title, author);
    }

    /// <summary>
    /// Presets Lucent ships whose file names don't say who made them the usual way, with their title and
    /// who to credit (see the CREDITS.md next to them).
    /// </summary>
    private static readonly Dictionary<string, (string Title, string? Author)> UnusualNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["phat_RippleNebula9"] = ("RippleNebula9", "Phat"),
        ["Unchained-liquardoblitz [shifter's esc shader]"] = ("liquardoblitz [shifter's esc shader]", "Unchained"),
        // Winamp's own AVS presets that never carried an author's name: they came from Nullsoft.
        ["New Taste"] = ("New Taste", "Nullsoft"),
        ["Pulsing Shit"] = ("Pulsing Shit", "Nullsoft"),
        ["Yay"] = ("Yay", "Nullsoft"),
        ["Yay Mk II"] = ("Yay Mk II", "Nullsoft"),
        ["Yay Mk II Dots"] = ("Yay Mk II Dots", "Nullsoft"),
        ["Yay Mk III"] = ("Yay Mk III", "Nullsoft"),
    };

    /// <summary>
    /// Gives every visual its own name, since favorites, lists and the last visual shown are kept by name.
    /// A MilkDrop or AVS preset whose title another visual already has gets its author added, "Tunnel (Geiss)",
    /// and anything still the same is numbered.
    /// </summary>
    public static IReadOnlyList<PresetSource> WithUniqueNames(IReadOnlyList<PresetSource> presets)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new PresetSource[presets.Count];
        // Lucent's own visuals keep their names; MilkDrop and AVS presets give way.
        foreach (var (preset, index) in presets.Select((preset, index) => (preset, index)).OrderBy(item => item.preset.IsCommunityPreset))
        {
            var name = preset.Name;
            if (taken.Contains(name) && preset.Author is not null)
            {
                name = $"{preset.Name} ({preset.Author})";
            }

            var unique = name;
            for (var n = 2; taken.Contains(unique); n++)
            {
                unique = $"{name} ({n})";
            }

            taken.Add(unique);
            result[index] = unique == preset.Name ? preset : preset with { Name = unique };
        }

        return result;
    }

    /// <summary>Whether a file is a MilkDrop preset, by its .milk extension.</summary>
    public static bool IsMilkDropFile(string path) => path.EndsWith(".milk", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a file is a Winamp AVS preset, by its .avs extension.</summary>
    public static bool IsAvsFile(string path) => path.EndsWith(".avs", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A visual's file as Lucent keeps its code: text for shaders and MilkDrop presets, and for AVS presets (which
    /// are binary) each byte as the Latin-1 character of the same number, so <see cref="Encoding.Latin1"/> gives the bytes back.
    /// </summary>
    public static string ReadCode(string path) =>
        IsAvsFile(path) ? File.ReadAllText(path, Encoding.Latin1) : File.ReadAllText(path);

    /// <summary><see cref="ReadCode"/> for a visual read from a stream (one built into the app).</summary>
    public static string ReadCode(string path, Stream stream)
    {
        using var reader = IsAvsFile(path) ? new StreamReader(stream, Encoding.Latin1) : new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string? ReadHeader(Regex pattern, string code)
    {
        var match = pattern.Match(code);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static DateTime? ReadAdded(string path, string code)
    {
        if (!IsMilkDropFile(path) && !IsAvsFile(path)
            && DateTime.TryParse(ReadHeader(AddedPattern, code), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var added))
        {
            return added;
        }

        try
        {
            return File.Exists(path) ? File.GetCreationTimeUtc(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
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
