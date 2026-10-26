using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lucent.Editor.Plugins;

/// <summary>
/// A plugin chain written down: which effects, in which order, switched on or off, with their settings.
/// Saved as a .lucentchain file (JSON), in %AppData%\Lucent\Plugins\Chains unless the user picks somewhere else.
/// </summary>
public sealed class ChainPreset
{
    public const string Extension = ".lucentchain";

    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int Version { get; set; } = CurrentVersion;

    public string Name { get; set; } = string.Empty;

    public List<ChainPresetPlugin> Plugins { get; set; } = new();

    /// <summary>What was left out when the chain came from another app, for the user. Not saved.</summary>
    [JsonIgnore]
    public string? Note { get; set; }

    /// <summary>Where chains are kept, so the Chains menu can list them.</summary>
    public static string Folder => Path.Combine(PluginCatalog.Folder, "Chains");

    /// <summary>The chains saved in <see cref="Folder"/>, by name.</summary>
    public static IReadOnlyList<(string Name, string Path)> Saved()
    {
        try
        {
            return Directory.Exists(Folder)
                ? Directory.EnumerateFiles(Folder, "*" + Extension)
                    .Select(path => (Name: System.IO.Path.GetFileNameWithoutExtension(path), Path: path))
                    .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList()
                : Array.Empty<(string, string)>();
        }
        catch (Exception)
        {
            return Array.Empty<(string, string)>();
        }
    }

    /// <summary>Reads a chain file: Lucent's own, or a Reaper FX chain (.RfxChain).</summary>
    /// <exception cref="PluginException">The file can't be read or isn't a chain.</exception>
    public static ChainPreset Load(string path)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PluginException($"{System.IO.Path.GetFileName(path)} couldn't be read ({ex.Message}).");
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        if (ReaperChain.LooksLikeOne(path, text))
        {
            return ReaperChain.Parse(text, name);
        }

        try
        {
            var preset = JsonSerializer.Deserialize<ChainPreset>(text, JsonOptions);
            if (preset is null || preset.Plugins is null)
            {
                throw new JsonException();
            }

            if (string.IsNullOrWhiteSpace(preset.Name))
            {
                preset.Name = name;
            }

            return preset;
        }
        catch (JsonException)
        {
            throw new PluginException($"{System.IO.Path.GetFileName(path)} isn't a plugin chain Lucent can read.");
        }
    }

    /// <exception cref="PluginException">The file couldn't be written.</exception>
    public void Save(string path)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PluginException($"The chain couldn't be saved ({ex.Message}).");
        }
    }

    /// <summary>
    /// Finds each of the chain's plugins among the ones installed: by its class id first, then by name and maker.
    /// Plugins that aren't installed come back with no <see cref="ResolvedPlugin.Plugin"/>.
    /// </summary>
    public IReadOnlyList<ResolvedPlugin> Resolve(IReadOnlyList<PluginInfo> installed) =>
        Plugins.Select(wanted => new ResolvedPlugin(wanted, Find(wanted, installed))).ToList();

    private static PluginInfo? Find(ChainPresetPlugin wanted, IReadOnlyList<PluginInfo> installed)
    {
        if (!string.IsNullOrEmpty(wanted.ClassId))
        {
            var ids = ClassIdForms(wanted.ClassId);
            if (installed.FirstOrDefault(p => ids.Contains(Hex(p.ClassId))) is { } byId)
            {
                return byId;
            }
        }

        var name = Simple(wanted.Name);
        var vendor = Simple(wanted.Vendor);
        var named = installed.Where(p => Simple(p.Name) == name).ToList();
        return named.FirstOrDefault(p => vendor.Length == 0 || Simple(p.Vendor) == vendor) ?? named.FirstOrDefault();
    }

    /// <summary>
    /// A VST3 class id as Lucent writes it and as hosts that store it in Windows's (COM) byte order do, which
    /// swaps the first three groups of bytes.
    /// </summary>
    private static HashSet<string> ClassIdForms(string classId)
    {
        var hex = Hex(classId);
        var forms = new HashSet<string>(StringComparer.Ordinal) { hex };
        if (hex.Length == 32)
        {
            static string Swap(string part) => string.Concat(Enumerable.Range(0, part.Length / 2).Reverse().Select(i => part.Substring(i * 2, 2)));
            forms.Add(Swap(hex[..8]) + Swap(hex[8..12]) + Swap(hex[12..16]) + hex[16..]);
        }

        return forms;
    }

    private static string Hex(string id) => string.Concat(id.Where(Uri.IsHexDigit)).ToUpperInvariant();

    /// <summary>A name with only its letters and digits, in lower case, so "Pro-Q 3" matches "ProQ3".</summary>
    private static string Simple(string? text) => string.Concat((text ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();
}

/// <summary>One plugin in a <see cref="ChainPreset"/>.</summary>
public sealed class ChainPresetPlugin
{
    public string Name { get; set; } = string.Empty;

    public string Vendor { get; set; } = string.Empty;

    /// <summary>The VST3 class id, when it's known.</summary>
    public string? ClassId { get; set; }

    /// <summary>The plugin's file, only to help the user find it if it isn't installed.</summary>
    public string? FileName { get; set; }

    public bool Bypassed { get; set; }

    /// <summary>The plugin's settings, as <see cref="PluginHost.GetSettings"/> gives them, in base 64. Null keeps its defaults.</summary>
    public string? Settings { get; set; }

    /// <summary>Why the settings couldn't be brought over from another app, for the user. Not saved.</summary>
    [JsonIgnore]
    public string? Note { get; set; }

    public byte[]? SettingsBytes()
    {
        try
        {
            return Settings is null ? null : Convert.FromBase64String(Settings);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}

/// <summary>A plugin of a chain, and the installed effect it was matched with (null if it isn't installed).</summary>
public sealed record ResolvedPlugin(ChainPresetPlugin Wanted, PluginInfo? Plugin);
