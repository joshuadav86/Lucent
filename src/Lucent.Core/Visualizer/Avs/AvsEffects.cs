using System.Reflection;

namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>
/// Marks an effect class with the number AVS saves for it (its place in rlib.cpp's list), or the name a
/// plug-in effect (APE) saves, so <see cref="AvsEffects.Create"/> can find it. A class can carry several
/// (an effect that began as a plug-in and later became built in is saved both ways).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class AvsEffectAttribute : Attribute
{
    public AvsEffectAttribute(int id)
    {
        Id = id;
    }

    public AvsEffectAttribute(string ape)
    {
        Id = AvsEffects.ApeId;
        Ape = ape;
    }

    public int Id { get; }

    public string? Ape { get; }
}

/// <summary>Every effect Lucent can run, found by the number or plug-in name a preset saves for it.</summary>
public static class AvsEffects
{
    /// <summary>The number saved before a plug-in effect's name (DLLRENDERBASE).</summary>
    public const int ApeId = 16384;

    /// <summary>The number saved for an effect list (LIST_ID, 0xfffffffe).</summary>
    public const int ListId = unchecked((int)0xfffffffe);

    /// <summary>The built-in effects' names, by number, as AVS's editor listed them.</summary>
    private static readonly string[] BuiltInNames =
    {
        "Render / Simple", "Render / Dot Plane", "Render / Oscilloscope Star", "Trans / Fadeout", "Trans / Blitter Feedback",
        "Render / OnBeat Clear", "Trans / Blur", "Render / Bass Spin", "Render / Moving Particle", "Trans / Roto Blitter",
        "Render / SVP Loader", "Trans / Colorfade", "Trans / Color Clip", "Render / Rotating Stars", "Render / Ring",
        "Trans / Movement", "Trans / Scatter", "Render / Dot Grid", "Misc / Buffer Save", "Render / Dot Fountain",
        "Trans / Water", "Misc / Comment", "Trans / Brightness", "Trans / Interleave", "Trans / Grain",
        "Render / Clear screen", "Trans / Mirror", "Render / Starfield", "Render / Text", "Trans / Bump",
        "Trans / Mosaic", "Trans / Water Bump", "Render / AVI", "Misc / Custom BPM", "Render / Picture",
        "Trans / Dynamic Distance Modifier", "Render / SuperScope", "Trans / Invert", "Trans / Unique tone", "Render / Timescope",
        "Misc / Set render mode", "Trans / Interferences", "Trans / Dynamic Shift", "Trans / Dynamic Movement", "Trans / Fast Brightness",
        "Trans / Color Modifier",
    };

    // Plug-ins that later versions of AVS built in under a number, as rlib.cpp's NamedApeToBuiltinTrans.
    private static readonly Dictionary<string, int> ApesNowBuiltIn = new(StringComparer.Ordinal)
    {
        ["Winamp Brightness APE v1"] = 22,
        ["Winamp Interleave APE v1"] = 23,
        ["Winamp Grain APE v1"] = 24,
        ["Winamp ClearScreen APE v1"] = 25,
        ["Nullsoft MIRROR v1"] = 26,
        ["Winamp Starfield v1"] = 27,
        ["Winamp Text v1"] = 28,
        ["Winamp Bump v1"] = 29,
        ["Winamp Mosaic v1"] = 30,
        ["Winamp AVIAPE v1"] = 32,
        ["Nullsoft Picture Rendering v1"] = 34,
        ["Winamp Interf APE v1"] = 41,
    };

    private static readonly Lazy<(Dictionary<int, Type> ById, Dictionary<string, Type> ByApe)> Types = new(FindTypes);

    /// <summary>
    /// Makes the effect saved as <paramref name="id"/> (or, for a plug-in, <paramref name="ape"/>), ready to load
    /// its settings, or null if Lucent doesn't have it.
    /// </summary>
    public static AvsEffect? Create(int id, string? ape, AvsShared shared, int depth = 0)
    {
        if (id == ListId)
        {
            return new AvsEffectList(shared, isRoot: false, depth);
        }

        if (ape is not null && ApesNowBuiltIn.TryGetValue(ape, out var builtIn))
        {
            id = builtIn;
            ape = null;
        }

        var (byId, byApe) = Types.Value;
        var type = ape is null
            ? byId.GetValueOrDefault(id)
            : byApe.GetValueOrDefault(ape);
        return type is null ? null : (AvsEffect)Activator.CreateInstance(type, shared)!;
    }

    /// <summary>Whether Lucent has the effect.</summary>
    public static bool IsAvailable(int id, string? ape)
    {
        if (id == ListId)
        {
            return true;
        }

        if (ape is not null && ApesNowBuiltIn.TryGetValue(ape, out var builtIn))
        {
            id = builtIn;
            ape = null;
        }

        var (byId, byApe) = Types.Value;
        return ape is null ? byId.ContainsKey(id) : byApe.ContainsKey(ape);
    }

    /// <summary>The effect's name as AVS showed it ("Trans / Movement"), or the plug-in's own name.</summary>
    public static string NameOf(int id, string? ape) =>
        id == ListId ? "Effect list"
        : ape is not null ? ape
        : id >= 0 && id < BuiltInNames.Length ? BuiltInNames[id]
        : $"Effect {id}";

    private static (Dictionary<int, Type>, Dictionary<string, Type>) FindTypes()
    {
        var byId = new Dictionary<int, Type>();
        var byApe = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in typeof(AvsEffects).Assembly.GetTypes())
        {
            if (!typeof(AvsEffect).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            foreach (var mark in type.GetCustomAttributes<AvsEffectAttribute>())
            {
                if (mark.Ape is null)
                {
                    byId[mark.Id] = type;
                }
                else
                {
                    byApe[mark.Ape] = type;
                }
            }
        }

        return (byId, byApe);
    }
}
