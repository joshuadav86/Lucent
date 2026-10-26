namespace Lucent.Visualizer;

/// <summary>What Previous visual (B) goes to.</summary>
public enum PreviousVisualMode
{
    /// <summary>The visual that was on screen before this one, however it got there (Auto, a click, Next).</summary>
    LastShown,

    /// <summary>The one above it in the picker's list.</summary>
    UpInList,
}

/// <summary>
/// The visuals that have been on screen, by name, so Previous visual can step back through what was actually
/// shown rather than up the list. Names, not indexes, so it still holds after the visuals load again.
/// </summary>
public sealed class VisualHistory
{
    /// <summary>How many visuals it remembers; the oldest are forgotten after that.</summary>
    public const int Limit = 200;

    private readonly List<string> _shown = new();

    public int Count => _shown.Count;

    /// <summary>Notes that a visual is on screen now. Telling it the same one again changes nothing.</summary>
    public void Shown(string name)
    {
        if (_shown.Count > 0 && string.Equals(_shown[^1], name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _shown.Add(name);
        if (_shown.Count > Limit)
        {
            _shown.RemoveRange(0, _shown.Count - Limit);
        }
    }

    /// <summary>
    /// Forgets the visual on screen and gives the one shown before it (skipping any <paramref name="exists"/>
    /// turns down, a visual that's gone since, say), or null when there's nothing further back.
    /// </summary>
    public string? Back(Func<string, bool> exists)
    {
        while (_shown.Count > 1)
        {
            _shown.RemoveAt(_shown.Count - 1);
            if (exists(_shown[^1]))
            {
                return _shown[^1];
            }
        }

        return null;
    }
}
