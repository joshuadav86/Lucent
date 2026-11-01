using Lucent.Settings;

namespace Lucent.Layout;

/// <summary>
/// A window layout saved under a name (right-click › Layouts › Save current layout…): which windows are
/// open, where each one is and how big, which are locked, whether they're linked to the player, the
/// player's size, and whether Lucent was full screen or a single window. Picking it from the Layouts menu, or pressing its
/// number (1 to 9), puts the windows back that way.
/// </summary>
public sealed class SavedLayout
{
    public string Name { get; set; } = "";

    /// <summary>
    /// Where each open window was, by the names its place is kept under in <see cref="AppSettings.Windows"/>
    /// ("Player", "Visuals", "Queue" …). One of <see cref="SavedLayouts.Openable"/> that isn't here was closed.
    /// Saved full screen, it's where they were before Lucent went full screen, to go back to.
    /// </summary>
    public Dictionary<string, WindowBounds> Windows { get; set; } = new();

    /// <summary>The windows locked in place, by the same names.</summary>
    public List<string> Locked { get; set; } = new();

    /// <summary>Every window moved with the player (J).</summary>
    public bool Linked { get; set; }

    /// <summary>Lucent was full screen (Shift+F11), laid out with <see cref="FullScreenStyle"/> and <see cref="FullScreenSwaps"/>.</summary>
    public bool FullScreen { get; set; }

    public TileStyle FullScreenStyle { get; set; } = TileStyle.AsTheyAre;

    public List<List<string>> FullScreenSwaps { get; set; } = new();

    /// <summary>
    /// Lucent was a single window (U), at <see cref="SingleWindowPlace"/>, with <see cref="SingleWindowStyle"/>,
    /// <see cref="SingleWindowSwaps"/>, <see cref="SingleWindowDividers"/> and <see cref="SingleWindowSizes"/>. <see cref="Windows"/> is then
    /// where the windows go back to when it's switched off.
    /// </summary>
    public bool SingleWindow { get; set; }

    public WindowBounds? SingleWindowPlace { get; set; }

    public TileStyle SingleWindowStyle { get; set; } = TileStyle.AsTheyAre;

    public List<List<string>> SingleWindowSwaps { get; set; } = new();

    public List<PanelDivider> SingleWindowDividers { get; set; } = new();

    public Dictionary<string, WindowBounds> SingleWindowSizes { get; set; } = new();

    /// <summary>The player's size (<see cref="AppSettings.PlayerScale"/>).</summary>
    public double PlayerScale { get; set; } = 1;
}

/// <summary>The layouts saved by name, kept in <see cref="AppSettings.Layouts"/> in the order they were first saved.</summary>
public static class SavedLayouts
{
    /// <summary>
    /// The windows a layout opens and closes. The video, the radio and editors are left open or closed as they
    /// are (closing them would stop what's in them), but go to their places in the layout if they're open.
    /// </summary>
    public static readonly IReadOnlyList<string> Openable = new[] { "Visuals", "Queue", "Library", "Equalizer", "Analyzer", "Artwork" };

    /// <summary>How many layouts have a key: 1 to 9.</summary>
    public const int Keys = 9;

    public const int MaxNameLength = 40;

    /// <summary>The name trimmed, with runs of spaces made one, and cut to <see cref="MaxNameLength"/>.</summary>
    public static string CleanName(string name)
    {
        var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return clean.Length <= MaxNameLength ? clean : clean[..MaxNameLength].TrimEnd();
    }

    /// <summary>The layout with that name, whatever its capitals, if there is one.</summary>
    public static SavedLayout? Find(IReadOnlyList<SavedLayout> layouts, string name) =>
        layouts.FirstOrDefault(layout => string.Equals(layout.Name, CleanName(name), StringComparison.OrdinalIgnoreCase));

    /// <summary>The key that opens a layout ("1" to "9"), or null past the ninth.</summary>
    public static string? KeyOf(IReadOnlyList<SavedLayout> layouts, SavedLayout layout)
    {
        var index = layouts.ToList().IndexOf(layout);
        return index is >= 0 and < Keys ? (index + 1).ToString() : null;
    }

    /// <summary>The layout a number key opens (1 is the first), if there's one there.</summary>
    public static SavedLayout? ForKey(IReadOnlyList<SavedLayout> layouts, int number) =>
        number is >= 1 and <= Keys && number <= layouts.Count ? layouts[number - 1] : null;

    /// <summary>
    /// Keeps a layout under its (cleaned) name. One already saved with that name is replaced where it is, so it
    /// keeps its key; a new one goes on the end.
    /// </summary>
    public static void Save(List<SavedLayout> layouts, SavedLayout layout)
    {
        layout.Name = CleanName(layout.Name);
        if (Find(layouts, layout.Name) is { } existing)
        {
            layouts[layouts.IndexOf(existing)] = layout;
        }
        else
        {
            layouts.Add(layout);
        }
    }

    /// <summary>Renames a layout. False when there's no layout called <paramref name="from"/>, or another one's already called <paramref name="to"/>.</summary>
    public static bool Rename(List<SavedLayout> layouts, string from, string to)
    {
        var name = CleanName(to);
        if (name.Length == 0 || Find(layouts, from) is not { } layout || Find(layouts, name) is { } other && other != layout)
        {
            return false;
        }

        layout.Name = name;
        return true;
    }

    public static bool Delete(List<SavedLayout> layouts, string name) => Find(layouts, name) is { } layout && layouts.Remove(layout);

    /// <summary>A copy to keep, so changing the windows later doesn't change the saved layout.</summary>
    public static WindowBounds Copy(WindowBounds bounds) => new()
    {
        Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height, Docked = bounds.Docked,
    };

    /// <summary>
    /// The layout's places moved together onto <paramref name="area"/>, for when it was saved with the player on a
    /// screen that isn't there now: the whole group keeps its shape, centred on the area, with its top left on it
    /// (so the title bars can be reached) when it's bigger than the area.
    /// </summary>
    public static Dictionary<string, WindowBounds> MovedOnto(IReadOnlyDictionary<string, WindowBounds> places, TileRect area)
    {
        if (places.Count == 0)
        {
            return new Dictionary<string, WindowBounds>();
        }

        var left = places.Values.Min(place => place.Left);
        var top = places.Values.Min(place => place.Top);
        var right = places.Values.Max(place => place.Left + place.Width);
        var bottom = places.Values.Max(place => place.Top + place.Height);
        var newLeft = Math.Max(area.Left, area.Left + (area.Width - (right - left)) / 2);
        var newTop = Math.Max(area.Top, area.Top + (area.Height - (bottom - top)) / 2);
        return places.ToDictionary(entry => entry.Key, entry =>
        {
            var moved = Copy(entry.Value);
            moved.Left += newLeft - left;
            moved.Top += newTop - top;
            return moved;
        });
    }
}
