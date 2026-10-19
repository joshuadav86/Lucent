using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Editor;

/// <summary>What a sound needs for an effect in the effects menu to make sense.</summary>
public enum EffectNeeds
{
    Nothing,
    /// <summary>Two channels (swapping or widening them).</summary>
    Stereo,
    /// <summary>One channel (making it stereo).</summary>
    Mono,
}

/// <summary>One thing the editor's effects menu can do.</summary>
/// <param name="Id">Which effect the editor runs for it.</param>
/// <param name="Title">Its name in the menu; one that opens a card of settings first ends with an ellipsis.</param>
/// <param name="Hint">A line under the name saying what it does.</param>
/// <param name="Keywords">Other words people might search for it by.</param>
public sealed record EffectItem(string Id, string Category, string Title, string Hint, string Keywords = "", EffectNeeds Needs = EffectNeeds.Nothing);

/// <summary>
/// The editor's built-in effects and tools, as the effects menu lists them: under categories, found by search
/// (every word typed has to be in the name, the line under it, the category, or the keywords), with the ones
/// used last kept under Recent. The same list on Windows and the Mac; each runs the effect by its id.
/// </summary>
public static class EffectCatalog
{
    /// <summary>The menu's section for the effects used last (not a category).</summary>
    public const string Recent = "Recent";

    /// <summary>How many effects Recent keeps.</summary>
    public const int RecentCount = 8;

    public const string Level = "Level";
    public const string TimeAndPitch = "Time and pitch";
    public const string ToneAndDynamics = "Tone and dynamics";
    public const string Repair = "Repair";
    public const string Channels = "Channels";
    public const string Edit = "Edit";
    public const string Stems = "Stems";
    public const string Analysis = "Analysis";

    /// <summary>Every category, in the order the menu lists them.</summary>
    public static IReadOnlyList<string> Categories { get; } = new[] { Level, TimeAndPitch, ToneAndDynamics, Repair, Channels, Edit, Stems, Analysis };

    public static IReadOnlyList<EffectItem> All { get; } = new EffectItem[]
    {
        new("normalize-peak-0.1", Level, "Normalize to −0.1 dB peak", "As loud as it can be without distorting", "loud louder maximize maximise peak"),
        new("normalize-peak-1", Level, "Normalize to −1 dB peak", "Recommended: a little headroom, so it stays clean after converting to MP3", "loud louder peak headroom"),
        new("normalize-peak-3", Level, "Normalize to −3 dB peak", "More headroom, for mixing it with other sounds", "loud quieter peak headroom"),
        new("normalize-lufs-14", Level, "Normalize to −14 LUFS", "Spotify and YouTube loudness, with peaks kept below −1 dB", "loudness streaming spotify youtube tidal lufs bs1770"),
        new("normalize-lufs-16", Level, "Normalize to −16 LUFS", "Apple Music and podcast loudness, with peaks kept below −1 dB", "loudness streaming apple music podcast lufs"),
        new("normalize-lufs-23", Level, "Normalize to −23 LUFS", "TV and radio loudness (EBU R128), with peaks kept below −1 dB", "loudness broadcast tv radio ebu r128 lufs"),
        new("volume", Level, "Volume…", "Make it louder or quieter by a number of decibels", "gain amplify amplitude louder quieter boost cut db"),
        new("fade-in", Level, "Fade in", "Fade up from silence over the selection (or the first 3 seconds)", "fade in ramp"),
        new("fade-out", Level, "Fade out", "Fade down to silence over the selection (or the last 3 seconds)", "fade out ramp"),

        new("pitch", TimeAndPitch, "Pitch…", "Move the pitch up or down by up to an octave, keeping the tempo or not", "pitch shift transpose semitones key tune"),
        new("tempo", TimeAndPitch, "Tempo…", "Make it faster or slower, keeping the pitch (or not)", "tempo speed stretch time bpm faster slower"),
        new("reverse", TimeAndPitch, "Reverse", "Play the selection backwards", "reverse backwards backward"),

        new("eq", ToneAndDynamics, "EQ…", "A parametric equalizer drawn over the spectrum and heard live while you shape it", "eq equalizer equaliser filter bass treble tone frequency"),
        new("dynamics", ToneAndDynamics, "Dynamics…", "A compressor to even out the level, or a true-peak limiter to make it louder without clipping", "compressor compression limiter limit dynamics loudness"),

        new("hum-auto", Repair, "Remove hum", "Finds 50 or 60 Hz mains hum and notches it out, with its overtones", "hum buzz mains noise"),
        new("hum-50", Repair, "Remove 50 Hz hum", "Mains hum in Europe, Asia, and Africa", "hum buzz mains noise"),
        new("hum-60", Repair, "Remove 60 Hz hum", "Mains hum in the Americas", "hum buzz mains noise"),
        new("clicks", Repair, "Remove clicks and crackle…", "Finds short clicks, like vinyl crackle, and fills them in from the music around them", "declick decrackle vinyl pops crackle restore"),
        new("noise", Repair, "Reduce background noise…", "Learns the hiss or hum from a quiet part, then takes it out everywhere", "denoise noise reduction hiss restore"),
        new("dc-offset", Repair, "Remove DC offset", "Centres a waveform that sits above or below the middle line", "dc offset centre center"),

        new("swap-channels", Channels, "Swap left and right", "The left channel plays on the right, and the right on the left", "swap channels stereo", EffectNeeds.Stereo),
        new("mono-mix", Channels, "Mix to mono", "Both sides play the same mix (still a stereo file)", "mono mix sum", EffectNeeds.Stereo),
        new("width", Channels, "Stereo width…", "Make it narrower, towards mono, or wider", "stereo width wide narrow image imager", EffectNeeds.Stereo),
        new("invert", Channels, "Invert polarity", "Flips the waveform upside down, to fix a channel that cancels out the other", "invert polarity phase flip"),
        new("to-mono", Channels, "Convert to a mono file", "Mixes the channels into one; saving writes a one-channel file, half the size", "mono convert channels", EffectNeeds.Stereo),
        new("to-stereo", Channels, "Convert to a stereo file", "Copies the one channel to both sides", "stereo convert channels", EffectNeeds.Mono),
        new("scope-both", Channels, "Edit both channels", "Edits and effects change both channels", "channel scope both", EffectNeeds.Stereo),
        new("scope-left", Channels, "Edit only the left channel", "Edits and effects change only the left channel (Alt+drag does this too)", "channel scope left", EffectNeeds.Stereo),
        new("scope-right", Channels, "Edit only the right channel", "Edits and effects change only the right channel", "channel scope right", EffectNeeds.Stereo),

        new("silence", Edit, "Silence", "Replace the selection with silence", "silence mute zero quiet"),

        new("stems", Stems, "Split into stems…", "Separate the vocals, drums, bass, and other instruments with an AI model: remove the vocals, keep only them, or save each as a file",
            "stems vocals karaoke instrumental acapella a cappella drums bass demucs separate ai"),

        new("analyze", Analysis, "Analyze…", "Peak, true peak, loudness, loudness range, DC offset, clipping, and the spectrum",
            "analyze analyse analysis meter loudness lufs peak true spectrum clipping stats"),
    };

    /// <summary>The effect with an id, if there is one.</summary>
    public static EffectItem? Find(string id) => All.FirstOrDefault(item => item.Id == id);

    /// <summary>Whether an effect belongs in a section of the menu: null for all of them, <see cref="Recent"/>, or a category.</summary>
    public static bool InSection(EffectItem item, string? section, IReadOnlyList<string> recent) => section switch
    {
        null => true,
        Recent => recent.Contains(item.Id),
        _ => item.Category == section,
    };

    /// <summary>Whether every word typed is in the effect's name, the line under it, its category, or its keywords.</summary>
    public static bool Matches(EffectItem item, string search)
    {
        var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.All(word => Plain(item.Title).Contains(Plain(word), StringComparison.CurrentCultureIgnoreCase)
                                 || item.Hint.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                                 || item.Category.Contains(word, StringComparison.CurrentCultureIgnoreCase)
                                 || item.Keywords.Contains(word, StringComparison.CurrentCultureIgnoreCase));
    }

    /// <summary>
    /// The effects in a section that match a search, under their headings: Recent in the order they were used,
    /// everything else under its category.
    /// </summary>
    public static IReadOnlyList<(string Heading, IReadOnlyList<EffectItem> Items)> Shown(string? section, string search, IReadOnlyList<string> recent)
    {
        if (section == Recent)
        {
            var used = recent.Select(Find).OfType<EffectItem>().Where(item => Matches(item, search)).ToList();
            return used.Count == 0 ? Array.Empty<(string, IReadOnlyList<EffectItem>)>() : new[] { (Recent, (IReadOnlyList<EffectItem>)used) };
        }

        return All.Where(item => InSection(item, section, recent) && Matches(item, search))
            .GroupBy(item => item.Category)
            .Select(group => (group.Key, (IReadOnlyList<EffectItem>)group.ToList()))
            .ToList();
    }

    /// <summary>Puts an effect at the top of Recent.</summary>
    public static void Used(EditorSettings settings, string id)
    {
        settings.RecentEffects.Remove(id);
        settings.RecentEffects.Insert(0, id);
        settings.RecentEffects.RemoveAll(other => Find(other) is null);
        if (settings.RecentEffects.Count > RecentCount)
        {
            settings.RecentEffects.RemoveRange(RecentCount, settings.RecentEffects.Count - RecentCount);
        }
    }

    /// <summary>Whether a sound with this many channels can have the effect.</summary>
    public static bool Fits(EffectItem item, int channels) => item.Needs switch
    {
        EffectNeeds.Stereo => channels > 1,
        EffectNeeds.Mono => channels == 1,
        _ => true,
    };

    /// <summary>Text with its minus signs as hyphens, so typing "-14" finds "−14 LUFS".</summary>
    private static string Plain(string text) => text.Replace('−', '-');
}
