using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// A MilkDrop preset (.milk file, from MilkDrop 1 or 2 or projectM), read but not yet compiled:
/// its starting values, its equations and, for MilkDrop 2 presets, its warp and composite shaders.
/// The file is INI-like: a [preset00] line, then key=value lines. Equations come as numbered lines
/// (per_frame_1=..., per_pixel_1=...) and shader lines start with a backtick.
/// </summary>
public sealed class MilkPreset
{
    /// <summary>How many custom waves and shapes are read (MilkDrop 2 has 4 of each; projectM allows more).</summary>
    public const int MaxCustomItems = 16;

    private static readonly Regex WaveCodePattern = new(@"^wave_(\d+)_(init|per_frame|per_point)(\d+)$", RegexOptions.Compiled);
    private static readonly Regex ShapeCodePattern = new(@"^shape_(\d+)_(init|per_frame)(\d+)$", RegexOptions.Compiled);
    private static readonly Regex WaveValuePattern = new(@"^wavecode_(\d+)_(\w+)$", RegexOptions.Compiled);
    private static readonly Regex ShapeValuePattern = new(@"^shapecode_(\d+)_(\w+)$", RegexOptions.Compiled);
    private static readonly Regex NumberedPattern = new(@"^(per_frame_init|per_frame|per_pixel|warp|comp)_(\d+)$", RegexOptions.Compiled);

    private MilkPreset(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>The preset's numeric settings, by their lower-case name in the file (fdecay, zoom, wave_r...).</summary>
    public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);

    public string PerFrameInit { get; private set; } = string.Empty;

    public string PerFrame { get; private set; } = string.Empty;

    public string PerPixel { get; private set; } = string.Empty;

    /// <summary>The MilkDrop 2 warp shader's HLSL, or empty for the classic (shaderless) warp.</summary>
    public string WarpShader { get; private set; } = string.Empty;

    /// <summary>The MilkDrop 2 composite shader's HLSL, or empty for the classic composite.</summary>
    public string CompShader { get; private set; } = string.Empty;

    public IReadOnlyList<MilkCustomWave> Waves { get; private set; } = Array.Empty<MilkCustomWave>();

    public IReadOnlyList<MilkCustomShape> Shapes { get; private set; } = Array.Empty<MilkCustomShape>();

    /// <summary>MILKDROP_PRESET_VERSION: 100-ish for MilkDrop 1 presets, 200 and up for MilkDrop 2.</summary>
    public int Version => (int)Value("milkdrop_preset_version", 100);

    /// <summary>Whether the warp shader should be used: a MilkDrop 2 preset whose warp shader targets pixel shader 2 or later.</summary>
    public bool HasWarpShader => WarpShader.Contains("shader_body", StringComparison.Ordinal) && ShaderVersion("psversion_warp") >= 2;

    /// <summary>Whether the composite shader should be used, as for <see cref="HasWarpShader"/>.</summary>
    public bool HasCompShader => CompShader.Contains("shader_body", StringComparison.Ordinal) && ShaderVersion("psversion_comp") >= 2;

    public double Value(string key, double fallback) => Values.TryGetValue(key, out var value) ? value : fallback;

    private double ShaderVersion(string key) => Value(key, Value("psversion", Version >= 200 ? 2 : 0));

    /// <summary>Reads a preset from the text of its .milk file. Throws <see cref="FormatException"/> if it isn't one.</summary>
    public static MilkPreset Parse(string text, string name)
    {
        var preset = new MilkPreset(name);
        var code = new Dictionary<string, SortedDictionary<int, string>>(StringComparer.Ordinal);
        var waves = new Dictionary<int, MilkCustomWave>();
        var shapes = new Dictionary<int, MilkCustomShape>();
        var recognised = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var equals = line.IndexOf('=');
            if (equals <= 0 || line.TrimStart().StartsWith('['))
            {
                continue;
            }

            var key = line[..equals].Trim().ToLowerInvariant();
            var value = line[(equals + 1)..];

            Match match;
            if ((match = NumberedPattern.Match(key)).Success)
            {
                Add(code, match.Groups[1].Value, match.Groups[2].Value, value);
                recognised++;
            }
            else if ((match = WaveCodePattern.Match(key)).Success && Item(match, waves, i => new MilkCustomWave(i)) is { } wave)
            {
                Add(wave.Code, match.Groups[2].Value, match.Groups[3].Value, value);
                recognised++;
            }
            else if ((match = ShapeCodePattern.Match(key)).Success && Item(match, shapes, i => new MilkCustomShape(i)) is { } shape)
            {
                Add(shape.Code, match.Groups[2].Value, match.Groups[3].Value, value);
                recognised++;
            }
            else if (TryNumber(value, out var number))
            {
                if ((match = WaveValuePattern.Match(key)).Success && Item(match, waves, i => new MilkCustomWave(i)) is { } waveValues)
                {
                    waveValues.Values[match.Groups[2].Value] = number;
                }
                else if ((match = ShapeValuePattern.Match(key)).Success && Item(match, shapes, i => new MilkCustomShape(i)) is { } shapeValues)
                {
                    shapeValues.Values[match.Groups[2].Value] = number;
                }
                else
                {
                    preset.Values[key] = number;
                }

                recognised++;
            }
        }

        if (recognised == 0)
        {
            throw new FormatException("This doesn't look like a MilkDrop preset: it has no settings or equations.");
        }

        preset.PerFrameInit = Join(code, "per_frame_init");
        preset.PerFrame = Join(code, "per_frame");
        preset.PerPixel = Join(code, "per_pixel");
        preset.WarpShader = JoinShader(code, "warp");
        preset.CompShader = JoinShader(code, "comp");
        foreach (var wave in waves.Values)
        {
            wave.Init = Join(wave.Code, "init");
            wave.PerFrame = Join(wave.Code, "per_frame");
            wave.PerPoint = Join(wave.Code, "per_point");
        }

        foreach (var shape in shapes.Values)
        {
            shape.Init = Join(shape.Code, "init");
            shape.PerFrame = Join(shape.Code, "per_frame");
        }

        preset.Waves = waves.Values.OrderBy(wave => wave.Index).ToList();
        preset.Shapes = shapes.Values.OrderBy(shape => shape.Index).ToList();
        return preset;
    }

    private static T? Item<T>(Match match, Dictionary<int, T> items, Func<int, T> create)
        where T : class
    {
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0 || index >= MaxCustomItems)
        {
            return null;
        }

        if (!items.TryGetValue(index, out var item))
        {
            item = create(index);
            items[index] = item;
        }

        return item;
    }

    private static void Add(Dictionary<string, SortedDictionary<int, string>> code, string kind, string number, string line)
    {
        if (!int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var order))
        {
            return;
        }

        if (!code.TryGetValue(kind, out var lines))
        {
            lines = new SortedDictionary<int, string>();
            code[kind] = lines;
        }

        lines[order] = line;
    }

    private static string Join(Dictionary<string, SortedDictionary<int, string>> code, string kind) =>
        code.TryGetValue(kind, out var lines) ? string.Join("\n", lines.Values) : string.Empty;

    /// <summary>Shader lines start with a backtick (so leading spaces survive), which isn't part of the HLSL.</summary>
    private static string JoinShader(Dictionary<string, SortedDictionary<int, string>> code, string kind)
    {
        if (!code.TryGetValue(kind, out var lines))
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        foreach (var line in lines.Values)
        {
            text.Append(line.StartsWith('`') ? line[1..] : line).Append('\n');
        }

        return text.ToString();
    }

    private static bool TryNumber(string text, out double value)
    {
        text = text.Trim();
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // A few hand-edited presets have a comment or stray text after the number.
        var end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        return end > 0 && double.TryParse(text[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>A custom wave (wavecode_N_... and wave_N_... lines): its settings and its equations.</summary>
public sealed class MilkCustomWave
{
    public MilkCustomWave(int index)
    {
        Index = index;
    }

    public int Index { get; }

    /// <summary>Its settings by lower-case name: enabled, samples, sep, bspectrum, busedots, bdrawthick, badditive, scaling, smoothing, r, g, b, a.</summary>
    public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);

    public string Init { get; internal set; } = string.Empty;

    public string PerFrame { get; internal set; } = string.Empty;

    public string PerPoint { get; internal set; } = string.Empty;

    internal Dictionary<string, SortedDictionary<int, string>> Code { get; } = new(StringComparer.Ordinal);

    public double Value(string key, double fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
}

/// <summary>A custom shape (shapecode_N_... and shape_N_... lines): its settings and its equations.</summary>
public sealed class MilkCustomShape
{
    public MilkCustomShape(int index)
    {
        Index = index;
    }

    public int Index { get; }

    /// <summary>Its settings by lower-case name: enabled, sides, additive, thickoutline, textured, num_inst, x, y, rad, ang, colours...</summary>
    public Dictionary<string, double> Values { get; } = new(StringComparer.Ordinal);

    public string Init { get; internal set; } = string.Empty;

    public string PerFrame { get; internal set; } = string.Empty;

    internal Dictionary<string, SortedDictionary<int, string>> Code { get; } = new(StringComparer.Ordinal);

    public double Value(string key, double fallback) => Values.TryGetValue(key, out var value) ? value : fallback;
}
