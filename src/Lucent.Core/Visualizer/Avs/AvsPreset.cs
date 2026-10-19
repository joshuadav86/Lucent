using System.Text;

namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>
/// An AVS preset (a .avs file from Winamp's Advanced Visualization Studio), read but not yet running.
/// The file is binary: "Nullsoft AVS Preset 0.2" and a 0x1A byte, then the main effect list's settings,
/// which hold every effect in turn: a 32-bit effect number (or, for plug-in effects, 16384 and then the
/// plug-in's 32-byte name), the length of its settings, then the settings themselves.
/// </summary>
public sealed class AvsPreset
{
    private const string Signature = "Nullsoft AVS Preset 0.";

    private AvsPreset(string name, byte[] data)
    {
        Name = name;
        Data = data;
    }

    public string Name { get; }

    /// <summary>The whole file.</summary>
    public byte[] Data { get; }

    /// <summary>Where the main effect list's settings start (just past the signature).</summary>
    public static int RootStart => Signature.Length + 2;

    /// <summary>The main effect list's settings.</summary>
    public AvsConfigReader Root => new(Data, RootStart, Data.Length - RootStart);

    /// <summary>
    /// Reads a preset. Throws <see cref="FormatException"/> if the data isn't an AVS preset (or is too
    /// damaged to find its effects in), so a stray file isn't listed as a visual.
    /// </summary>
    public static AvsPreset Parse(byte[] data, string name)
    {
        if (data.Length < RootStart + 1
            || Encoding.ASCII.GetString(data, 0, Signature.Length) != Signature
            || data[Signature.Length] is not ((byte)'1' or (byte)'2')
            || data[Signature.Length + 1] != 0x1a)
        {
            throw new FormatException("It isn't an AVS preset (it doesn't start \"Nullsoft AVS Preset\").");
        }

        var preset = new AvsPreset(name, data);
        var count = CountEffects(preset.Root, 0);
        if (count == 0)
        {
            throw new FormatException("The AVS preset has no effects in it.");
        }

        return preset;
    }

    /// <summary>Reads a preset from its text as Lucent keeps visuals' code: the file's bytes as Latin-1 characters.</summary>
    public static AvsPreset Parse(string code, string name) => Parse(Encoding.Latin1.GetBytes(code), name);

    /// <summary>The names of the effects in the preset, outermost first, for checking what a preset uses.</summary>
    public IEnumerable<string> EffectNames()
    {
        var names = new List<string>();
        foreach (var entry in AvsEffectList.Entries(Root))
        {
            Collect(entry, names);
        }

        return names;
    }

    private static void Collect(AvsEffectEntry entry, List<string> names)
    {
        names.Add(AvsEffects.NameOf(entry.Id, entry.Ape));
        if (entry.Id == AvsEffects.ListId)
        {
            foreach (var child in AvsEffectList.Entries(entry.Config))
            {
                Collect(child, names);
            }
        }
    }

    private static int CountEffects(AvsConfigReader list, int depth)
    {
        if (depth > 64)
        {
            throw new FormatException("The AVS preset's effect lists go too deep.");
        }

        var count = 0;
        foreach (var entry in AvsEffectList.Entries(list))
        {
            count++;
            if (entry.Id == AvsEffects.ListId)
            {
                count += CountEffects(entry.Config, depth + 1);
            }
        }

        return count;
    }
}

/// <summary>One effect in an effect list, as saved: its number (or plug-in name) and its settings.</summary>
/// <param name="Id">The effect's number (see <see cref="AvsEffects"/>), or <see cref="AvsEffects.ApeId"/> for a plug-in.</param>
/// <param name="Ape">A plug-in effect's name ("Texer II", "Color Map"...), or null for a built-in one.</param>
public readonly record struct AvsEffectEntry(int Id, string? Ape, AvsConfigReader Config);
