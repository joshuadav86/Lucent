using System.Text.RegularExpressions;
using Lucent.Settings;

namespace Lucent.Editor.Plugins;

/// <summary>An installed plugin as the plugin browser lists it: its type, and whether it's starred.</summary>
/// <param name="TypeIsChosen">The user gave it this type, rather than Lucent working it out.</param>
public sealed record PluginEntry(PluginInfo Plugin, string Type, bool TypeIsChosen, bool IsFavorite)
{
    /// <summary>Who made it, as the browser groups it.</summary>
    public string Maker => string.IsNullOrWhiteSpace(Plugin.Vendor) ? "Unknown maker" : Plugin.Vendor.Trim();
}

/// <summary>
/// What the editor's plugin browser shows: every installed effect with a type (EQ, Compressor, Reverb...),
/// found by search, and grouped by type or by maker. The type comes from the plugin's own VST3 subcategories,
/// sharpened by its name (VST3 only says "Dynamics" for compressors, limiters, gates and de-essers alike, and
/// some makers leave it out altogether); the user can give any plugin a different one. Stars and chosen types
/// are kept in the editor's settings, by class ID, so they survive updates of the plugin.
/// </summary>
public static class PluginBrowser
{
    /// <summary>The browser's section for the starred plugins (not a type).</summary>
    public const string Favorites = "Favorites";

    public const string Other = "Other";

    /// <summary>Every type, in the order the browser lists them.</summary>
    public static IReadOnlyList<string> Types => TypeOrder;

    private static readonly string[] TypeOrder =
    {
        "EQ", "Compressor", "Limiter", "Gate", "De-esser", "Saturation", "Reverb", "Delay", "Modulation", "Filter",
        "Pitch", "Stereo", "Channel strip", "Restoration", "Metering", "Mastering", "Utility", Other,
    };

    /// <summary>VST3's standard subcategories, and the type each one means.</summary>
    private static readonly Dictionary<string, string> Subcategories = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EQ"] = "EQ",
        ["Dynamics"] = "Compressor",
        ["Distortion"] = "Saturation",
        ["Reverb"] = "Reverb",
        ["Delay"] = "Delay",
        ["Modulation"] = "Modulation",
        ["Filter"] = "Filter",
        ["Pitch Shift"] = "Pitch",
        ["Spatial"] = "Stereo",
        ["Restoration"] = "Restoration",
        ["Analyzer"] = "Metering",
        ["Mastering"] = "Mastering",
        ["Tools"] = "Utility",
        ["Generator"] = "Utility",
    };

    /// <summary>Subcategories too broad to beat what the name says ("Tools" could be anything).</summary>
    private static readonly HashSet<string> VagueTypes = new() { "Mastering", "Utility", "Stereo" };

    private static readonly HashSet<string> DynamicsTypes = new() { "Compressor", "Limiter", "Gate", "De-esser", "Channel strip", "Restoration" };

    /// <summary>
    /// Words in plugin names that give their type away, tried in this order, so the more particular ones win
    /// ("De-clip" is restoration before "clip" makes it a limiter; "Tape Echo" is a delay before "tape" makes it saturation).
    /// </summary>
    private static readonly (string Type, Regex Pattern)[] NameRules =
    {
        ("Restoration", Words(@"de-?nois|noise ?reduc|de-?click|de-?clip|de-?crackle|de-?hum|de-?reverb|de-?bleed|restor|rx ?\d|\brx\b|spectral ?(de|repair)|unveil|dialogue|voice ?isolat")),
        ("Channel strip", Words(@"channel ?strip|\bstrip\b|neutron|console ?1|\b[eg]-?channel|mixhub|\b4000 ?e\b|\b9000 ?j\b")),
        ("Metering", Words(@"meter|analy[sz]|scope|loudness|\blufs\b|spectrum|tuner|correlat|insight|\bspan\b")),
        ("De-esser", Words(@"de-?ess|pro-ds|sibilan")),
        ("Gate", Words(@"\bgate\b|noise ?gate|expander|pro-g\b")),
        ("Limiter", Words(@"limit|maximi[sz]|\bl[123]\b|pro-l\b|clipper|\bclip\b")),
        ("Compressor", Words(@"compress|\bcomp\b|1176|la-?[23]a|fairchild|vari-?mu|\bopto|pro-c\b|\bglue\b|distressor|kotelnikov|\bdbx|\bfet\b|pressure|transient|\bott\b|\b76\b|cla-?\d|weiss ds")),
        ("EQ", Words(@"(?<!fr)eq(?![a-z])|eq\d|equali[sz]|pro-q|pultec|soothe|gullfoss|\bnova\b|sie-?q|trackspacer|smooth operator")),
        ("Delay", Words(@"delay|echo|primaltap|timeless|repeater|\bddl\b|re-?201|memory ?man|\btap\b|crystallizer")),
        ("Reverb", Words(@"verb|room\b|plate|\bhall\b|spring|chamber|convol|shimmer|supermassive|seventh heaven|blackhole|cloudseed|pro-r\b|lexicon|\bemt\b")),
        ("Saturation", Words(@"distort|saturat|drive|\btape\b|\btube\b|valve|exciter|harmonic|decapitator|devil-?loc|radiator|saturn|trash|fuzz|crush|lo-?fi|amp\b|amplifier|warm|j37|kramer|hg-?2|retro colou?r")),
        ("Pitch", Words(@"pitch|tune|tuning|melodyne|alterboy|harmoni[sz]er|formant|octav|transpos|vocoder")),
        ("Modulation", Words(@"chorus|flang|phaser|mistress|tremol|vibrato|rotary|leslie|ensemble|modulat|microshift|dimension|\bwah|auto-?pan|panman|gater|stutter|glitch|ring ?mod|doubler|kickstart|lfo ?tool|shaperbox|volume ?shap|\bduck")),
        ("Filter", Words(@"filt|\bvcf\b|\b[lh]pf\b|volcano|wobble|resonat")),
        ("Stereo", Words(@"stereo|imag(er|ing)|width|wider|\bm/?s\b|mid[ -/]?side|panner|\bpan\b|spatial|binaural|haas|upmix|surround")),
        ("Mastering", Words(@"ozone|master")),
        ("Utility", Words(@"utility|gain|trim|\bmono\b|polarity|volume|balance|tone ?gen|oscillator|crossover|router|\bsend\b")),
    };

    /// <summary>The type Lucent gives a plugin, from its subcategories and its name, before the user says otherwise.</summary>
    public static string Guess(PluginInfo plugin)
    {
        var categories = plugin.Categories.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var fromName = NameRules.FirstOrDefault(rule => rule.Pattern.IsMatch(plugin.Name)).Type;

        // An EQ and dynamics together is a channel strip.
        if (categories.Contains("EQ", StringComparer.OrdinalIgnoreCase) && categories.Contains("Dynamics", StringComparer.OrdinalIgnoreCase))
        {
            return "Channel strip";
        }

        var fromCategories = categories.Select(c => Subcategories.GetValueOrDefault(c)).FirstOrDefault(type => type is not null);
        return fromCategories switch
        {
            // "Dynamics": the name says which kind, or it's most likely a compressor.
            "Compressor" => fromName is not null && DynamicsTypes.Contains(fromName) ? fromName : "Compressor",
            null => fromName ?? Other,
            _ when VagueTypes.Contains(fromCategories) => fromName ?? fromCategories,
            // Channel strips often call themselves EQs.
            _ => fromName == "Channel strip" ? fromName : fromCategories,
        };
    }

    /// <summary>The installed plugins as the browser lists them, with the user's stars and types.</summary>
    public static IReadOnlyList<PluginEntry> Entries(IReadOnlyList<PluginInfo> plugins, EditorSettings settings)
    {
        var favorites = new HashSet<string>(settings.FavoritePlugins, StringComparer.OrdinalIgnoreCase);
        return plugins.Select(plugin =>
        {
            var chosen = settings.PluginTypes.TryGetValue(plugin.ClassId, out var type) && Types.Contains(type);
            return new PluginEntry(plugin, chosen ? type! : Guess(plugin), chosen, favorites.Contains(plugin.ClassId));
        }).ToList();
    }

    /// <summary>
    /// Whether a plugin belongs in a section of the browser: null for all of them, <see cref="Favorites"/>,
    /// or one of the <see cref="Types"/>.
    /// </summary>
    public static bool InSection(PluginEntry entry, string? section) => section switch
    {
        null => true,
        Favorites => entry.IsFavorite,
        _ => entry.Type == section,
    };

    /// <summary>Whether every word typed is in the plugin's name, maker, or type (or its own subcategories).</summary>
    public static bool Matches(PluginEntry entry, string search)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var plugin = entry.Plugin;
        return words.All(word => plugin.Name.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                                 || plugin.Vendor.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                                 || entry.Type.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                                 || plugin.Categories.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// The plugins under headings: their types in the usual order, or their makers A to Z. Inside each
    /// heading they stay in the order given (A to Z, from the catalog).
    /// </summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<PluginEntry> Plugins)> Group(IEnumerable<PluginEntry> entries, bool byMaker)
    {
        var groups = byMaker
            ? entries.GroupBy(e => e.Maker, StringComparer.CurrentCultureIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
            : entries.GroupBy(e => e.Type).OrderBy(g => Order(g.Key));
        return groups.Select(g => (g.Key, (IReadOnlyList<PluginEntry>)g.ToList())).ToList();
    }

    /// <summary>Where a type comes in the usual order.</summary>
    public static int Order(string type)
    {
        var index = Array.IndexOf(TypeOrder, type);
        return index < 0 ? TypeOrder.Length : index;
    }

    public static void SetFavorite(EditorSettings settings, PluginInfo plugin, bool favorite)
    {
        settings.FavoritePlugins.RemoveAll(id => string.Equals(id, plugin.ClassId, StringComparison.OrdinalIgnoreCase));
        if (favorite)
        {
            settings.FavoritePlugins.Add(plugin.ClassId);
        }
    }

    /// <summary>Gives a plugin a type of the user's choosing, or (with null) goes back to the one Lucent works out.</summary>
    public static void SetType(EditorSettings settings, PluginInfo plugin, string? type)
    {
        if (type is null || type == Guess(plugin))
        {
            settings.PluginTypes.Remove(plugin.ClassId);
        }
        else
        {
            settings.PluginTypes[plugin.ClassId] = type;
        }
    }

    private static Regex Words(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}
