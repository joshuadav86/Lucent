namespace Lucent.Editor.Plugins;

/// <summary>One plugin in a chain: the effect, its helper, and whether it's switched off for now.</summary>
public sealed class ChainSlot
{
    private volatile bool _bypassed;

    public ChainSlot(PluginInfo plugin, PluginHost host)
    {
        Plugin = plugin;
        Host = host;
    }

    public PluginInfo Plugin { get; }

    public PluginHost Host { get; }

    /// <summary>Switched off: the audio goes past it untouched. A preview playing hears the change at once.</summary>
    public bool IsBypassed
    {
        get => _bypassed;
        set => _bypassed = value;
    }

    /// <summary>The name to show: the plugin's own, with its maker.</summary>
    public string Title => string.IsNullOrWhiteSpace(Plugin.Vendor) ? Host.Name : $"{Host.Name}  ·  {Plugin.Vendor}";
}

/// <summary>
/// The plugins open in the editor, in the order the audio goes through them. Apply runs the ones that are
/// switched on as one step; a chain of one is a single plugin. The list is changed on the UI thread only.
/// </summary>
public sealed class PluginChain
{
    private readonly List<ChainSlot> _slots = new();

    public IReadOnlyList<ChainSlot> Slots => _slots;

    public int Count => _slots.Count;

    /// <summary>The plugins that are switched on and still running, in order.</summary>
    public IReadOnlyList<PluginHost> ActiveHosts => _slots.Where(s => !s.IsBypassed && !s.Host.IsGone).Select(s => s.Host).ToList();

    public void Add(ChainSlot slot) => _slots.Add(slot);

    /// <summary>Takes a plugin out of the chain (closing it is up to the caller).</summary>
    public ChainSlot RemoveAt(int index)
    {
        var slot = _slots[index];
        _slots.RemoveAt(index);
        return slot;
    }

    /// <summary>Moves a plugin so it's number <paramref name="to"/> in the chain.</summary>
    public void Move(int from, int to)
    {
        to = Math.Clamp(to, 0, _slots.Count - 1);
        if (from == to || from < 0 || from >= _slots.Count)
        {
            return;
        }

        var slot = _slots[from];
        _slots.RemoveAt(from);
        _slots.Insert(to, slot);
    }

    /// <summary>Takes every plugin out, returning them so they can be closed.</summary>
    public IReadOnlyList<ChainSlot> Clear()
    {
        var slots = _slots.ToList();
        _slots.Clear();
        return slots;
    }

    /// <summary>Takes out the plugins whose helpers have crashed or been stopped, returning them.</summary>
    public IReadOnlyList<ChainSlot> RemoveGone()
    {
        var gone = _slots.Where(s => s.Host.IsGone).ToList();
        _slots.RemoveAll(s => s.Host.IsGone);
        return gone;
    }

    /// <summary>
    /// Writes plugins down as a chain preset, with each one's settings as they are now. Waits for every
    /// plugin, so call it off the UI thread, with a copy of <see cref="Slots"/> taken on it.
    /// </summary>
    public static ChainPreset ToPreset(string name, IReadOnlyList<ChainSlot> slots)
    {
        var preset = new ChainPreset { Name = name };
        foreach (var slot in slots)
        {
            byte[]? settings = null;
            if (!slot.Host.IsGone)
            {
                try
                {
                    settings = slot.Host.GetSettings();
                }
                catch (PluginException)
                {
                    // Saved without its settings; it starts with its own defaults when the chain is opened.
                }
            }

            preset.Plugins.Add(new ChainPresetPlugin
            {
                Name = slot.Plugin.Name,
                Vendor = slot.Plugin.Vendor,
                ClassId = slot.Plugin.ClassId,
                FileName = Path.GetFileName(Path.TrimEndingDirectorySeparator(slot.Plugin.Path)),
                Bypassed = slot.IsBypassed,
                Settings = settings is null ? null : Convert.ToBase64String(settings),
            });
        }

        return preset;
    }
}
