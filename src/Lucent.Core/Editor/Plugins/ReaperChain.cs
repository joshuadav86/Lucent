using System.Buffers.Binary;
using System.Text;

namespace WinampInspiredVisualizer.Editor.Plugins;

/// <summary>
/// Reads a REAPER FX chain (.RfxChain) as a Lucent chain. REAPER writes each effect as a block like
/// <code>
/// BYPASS 0 0 0
/// &lt;VST "VST3: Pro-Q 3 (FabFilter)" "FabFilter Pro-Q 3.vst3" 0 "" 1822302032{72C4DB717A4D459AB97E51745D84B39D} ""
///   (base 64: REAPER's header, the plugin's state, the preset name)
/// &gt;
/// </code>
/// VST3 effects come over with their settings. Lucent only runs VST3, so a VST2 effect becomes the VST3
/// version of it with its standard settings, when that's installed; JS effects and others are left out,
/// and each plugin brought over without its settings carries a <see cref="ChainPresetPlugin.Note"/> saying why.
/// </summary>
/// <remarks>
/// The base 64 decodes to: int32 the effect's id, uint32 0xFEED5EEE or 0xFEED5EEF, int32 input count and a
/// 64-bit pin mask for each, int32 output count and their masks, int32 state size, int32, int32; then the
/// state; then the preset name. A VST3 state is int32 the component state's size, int32 1, the component
/// state, then the controller's (with its own size and flag in front). A state that starts DEADBEEF
/// DEADF00D is a list of parameter values instead, which Lucent can't set yet.
/// </remarks>
public static class ReaperChain
{
    public const string Extension = ".RfxChain";

    private const uint ParameterListMagic1 = 0xDEADBEEF;
    private const uint ParameterListMagic2 = 0xDEADF00D;

    public static bool LooksLikeOne(string path, string text) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ||
        (!text.TrimStart().StartsWith('{') && text.Contains("<VST", StringComparison.Ordinal));

    /// <exception cref="PluginException">There are no effects in it Lucent knows what to do with.</exception>
    public static ChainPreset Parse(string text, string name)
    {
        var preset = new ChainPreset { Name = name };
        var skipped = new List<string>();
        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        var bypassed = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith("BYPASS ", StringComparison.Ordinal))
            {
                bypassed = Tokens(line).ElementAtOrDefault(1) == "1";
                continue;
            }

            if (!line.StartsWith('<'))
            {
                continue;
            }

            var header = Tokens(line);
            var tag = header[0][1..];
            if (tag == "CONTAINER")
            {
                // REAPER 7's containers hold effects of their own; they're brought over one after another.
                continue;
            }

            var body = new List<string>();
            var depth = 1;
            for (i++; i < lines.Count && depth > 0; i++)
            {
                if (lines[i].StartsWith('<'))
                {
                    depth++;
                }
                else if (lines[i] == ">")
                {
                    depth--;
                }
                else if (depth == 1)
                {
                    body.Add(lines[i]);
                }
            }

            i--;
            switch (tag)
            {
                case "VST":
                    preset.Plugins.Add(ReadVst(header, body, bypassed));
                    break;
                case "JS" or "AU" or "CLAP" or "DX" or "LV2" or "VIDEO_EFFECT":
                    skipped.Add(Describe(tag, header));
                    break;
            }

            bypassed = false;
        }

        if (preset.Plugins.Count == 0)
        {
            throw new PluginException(skipped.Count > 0
                ? $"This REAPER chain only has effects Lucent can't run ({string.Join(", ", skipped)}). Lucent runs VST3 plugins."
                : "There are no effects in this REAPER chain.");
        }

        if (skipped.Count > 0)
        {
            preset.Note = $"Left out, because Lucent runs only VST3 plugins: {string.Join(", ", skipped)}.";
        }

        return preset;
    }

    private static ChainPresetPlugin ReadVst(IReadOnlyList<string> header, IReadOnlyList<string> body, bool bypassed)
    {
        // "VST3: Pro-Q 3 (FabFilter)", "VSTi: Podolski (u-he)", or "VST3: dpMeter2 (TBProAudio) (6ch)".
        var display = header.ElementAtOrDefault(1) ?? string.Empty;
        var colon = display.IndexOf(": ", StringComparison.Ordinal);
        var format = colon > 0 ? display[..colon] : "VST";
        var (pluginName, vendor) = SplitName(colon > 0 ? display[(colon + 2)..] : display);
        var id = header.ElementAtOrDefault(5) ?? string.Empty;
        var isVst3 = format.StartsWith("VST3", StringComparison.OrdinalIgnoreCase) || id.Contains('{');
        var plugin = new ChainPresetPlugin
        {
            Name = pluginName,
            Vendor = vendor,
            FileName = header.ElementAtOrDefault(2),
            Bypassed = bypassed,
        };

        if (!isVst3)
        {
            plugin.Note = "REAPER used the VST2 version of this plugin, so its settings can't be brought over.";
            return plugin;
        }

        var brace = id.IndexOf('{');
        if (brace >= 0 && id.IndexOf('}', brace) is var end && end > brace)
        {
            plugin.ClassId = id[(brace + 1)..end];
        }

        try
        {
            var data = Decode(body);
            var (state, isParameterList) = SplitState(data);
            if (isParameterList)
            {
                plugin.Note = "REAPER saved this plugin's settings as a list of knob positions, which Lucent can't bring over yet.";
            }
            else if (VstState(state) is { } settings)
            {
                plugin.Settings = Convert.ToBase64String(settings);
            }
            else
            {
                plugin.Note = "Its settings in the REAPER chain couldn't be read.";
            }
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            plugin.Note = "Its settings in the REAPER chain couldn't be read.";
        }

        return plugin;
    }

    /// <summary>The plugin's own state out of REAPER's data, and whether it's a parameter list rather than a state.</summary>
    private static (byte[] State, bool IsParameterList) SplitState(byte[] data)
    {
        var at = 8;
        var inputs = ReadInt(data, ref at);
        at += 8 * Math.Max(0, inputs);
        var outputs = ReadInt(data, ref at);
        at += 8 * Math.Max(0, outputs);
        var size = ReadInt(data, ref at);
        at += 8;
        if (inputs < 0 || outputs < 0 || size < 0 || at + size > data.Length)
        {
            throw new FormatException("REAPER's header doesn't add up.");
        }

        var state = data.AsSpan(at, size).ToArray();
        var isList = size >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(state) == ParameterListMagic1 &&
                     BinaryPrimitives.ReadUInt32LittleEndian(state.AsSpan(4)) == ParameterListMagic2;
        return (state, isList);
    }

    /// <summary>
    /// A VST3 state from REAPER as Lucent's plugin helper takes it (int32 size, component state, int32 size,
    /// controller state), or null if it doesn't make sense.
    /// </summary>
    internal static byte[]? VstState(byte[] state)
    {
        if (state.Length < 8)
        {
            return null;
        }

        var componentSize = BinaryPrimitives.ReadInt32LittleEndian(state);
        if (componentSize < 0 || 8 + componentSize > state.Length)
        {
            return null;
        }

        var component = state.AsSpan(8, componentSize);
        var rest = state.AsSpan(8 + componentSize);
        var controller = ReadOnlySpan<byte>.Empty;
        if (rest.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(rest) is var controllerSize && controllerSize > 0)
        {
            // Normally the size and a flag come first, as for the component; some files leave out the flag.
            if (rest.Length >= 8 + controllerSize)
            {
                controller = rest.Slice(8, controllerSize);
            }
            else if (rest.Length >= 4 + controllerSize)
            {
                controller = rest.Slice(4, controllerSize);
            }
        }

        var settings = new byte[8 + component.Length + controller.Length];
        BinaryPrimitives.WriteInt32LittleEndian(settings, component.Length);
        component.CopyTo(settings.AsSpan(4));
        BinaryPrimitives.WriteInt32LittleEndian(settings.AsSpan(4 + component.Length), controller.Length);
        controller.CopyTo(settings.AsSpan(8 + component.Length));
        return settings;
    }

    /// <summary>
    /// The bytes of the base 64 lines. REAPER writes each part (header, state, name) on lines of its own, each
    /// decodable alone, so lines are decoded one at a time and the bytes run on.
    /// </summary>
    private static byte[] Decode(IEnumerable<string> lines)
    {
        var bytes = new List<byte>();
        var pending = new StringBuilder();
        foreach (var line in lines.Where(l => l.Length > 0))
        {
            pending.Append(line);
            if (pending.Length % 4 == 0)
            {
                bytes.AddRange(Convert.FromBase64String(pending.ToString()));
                pending.Clear();
            }
        }

        if (pending.Length > 0)
        {
            throw new FormatException("The base 64 is cut short.");
        }

        return bytes.ToArray();
    }

    private static int ReadInt(byte[] data, ref int at)
    {
        if (at + 4 > data.Length)
        {
            throw new FormatException("REAPER's header is cut short.");
        }

        var value = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at));
        at += 4;
        return value;
    }

    /// <summary>"Pro-Q 3 (FabFilter)" into the name and the maker; a channel count like "(6ch)" is dropped.</summary>
    internal static (string Name, string Vendor) SplitName(string text)
    {
        text = text.Trim();
        if (text.EndsWith("ch)", StringComparison.Ordinal) && text.LastIndexOf(" (", StringComparison.Ordinal) is var channels && channels > 0 &&
            text[(channels + 2)..^3].All(char.IsDigit))
        {
            text = text[..channels].TrimEnd();
        }

        if (text.EndsWith(')') && text.LastIndexOf(" (", StringComparison.Ordinal) is var open && open > 0)
        {
            return (text[..open].Trim(), text[(open + 2)..^1].Trim());
        }

        return (text, string.Empty);
    }

    private static string Describe(string tag, IReadOnlyList<string> header)
    {
        var kind = tag switch
        {
            "JS" => "JS",
            "AU" => "Audio Unit",
            "CLAP" => "CLAP",
            "DX" => "DirectX",
            "LV2" => "LV2",
            _ => "video",
        };
        var title = header.ElementAtOrDefault(1) ?? kind;
        var colon = title.IndexOf(": ", StringComparison.Ordinal);
        return $"{(colon > 0 ? title[(colon + 2)..] : title)} ({kind})";
    }

    /// <summary>Splits a line into words, keeping "quoted words" together. REAPER quotes with ", ' or `.</summary>
    internal static List<string> Tokens(string line)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < line.Length)
        {
            if (char.IsWhiteSpace(line[i]))
            {
                i++;
                continue;
            }

            if (line[i] is '"' or '\'' or '`')
            {
                var quote = line[i];
                var close = line.IndexOf(quote, i + 1);
                if (close < 0)
                {
                    close = line.Length;
                }

                tokens.Add(line[(i + 1)..close]);
                i = close + 1;
                continue;
            }

            var start = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i]))
            {
                i++;
            }

            tokens.Add(line[start..i]);
        }

        return tokens;
    }
}
