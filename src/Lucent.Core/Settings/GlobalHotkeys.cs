namespace Lucent.Settings;

/// <summary>What a global shortcut does (Options > Sound "Shortcuts in every app").</summary>
public enum HotkeyAction
{
    PlayPause,
    Stop,
    Next,
    Previous,
    VolumeUp,
    VolumeDown,
    Favorite,
    ShowLucent,
}

/// <summary>The keys held with a shortcut's key. Command is the Windows key on Windows and ⌘ on a Mac.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Control = 1,
    Alt = 2,
    Shift = 4,
    Command = 8,
}

/// <summary>
/// A key with the keys held with it, such as Ctrl+Alt+P: a global shortcut, which works while another app is in
/// front. Kept in the settings as text ("Control+Alt+P"), by the key's place on the keyboard rather than the letter
/// it types, so one shortcut means the same keys on Windows and a Mac.
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, string Key)
{
    /// <summary>A key a shortcut can use: its name in the settings, its Windows virtual-key code, its Mac key code, and Avalonia's name for it.</summary>
    public readonly record struct KeyInfo(string Name, int WindowsCode, int MacCode, string Physical, string Label);

    /// <summary>The keys a shortcut can use: letters, digits, F1 to F19, and the keys around them.</summary>
    public static readonly IReadOnlyList<KeyInfo> Keys = MakeKeys();

    private static readonly Dictionary<string, KeyInfo> ByName = Keys.ToDictionary(key => key.Name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether it's a shortcut people can set: a known key, with Control, Alt or Command (Shift alone types), or an F key on its own.</summary>
    public bool IsUsable => ByName.ContainsKey(Key)
        && ((Modifiers & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Command)) != 0 || Key.StartsWith('F') && Key.Length > 1 && char.IsDigit(Key[1]));

    public KeyInfo? Info => ByName.TryGetValue(Key, out var info) ? info : null;

    /// <summary>The shortcut as the settings keep it.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Control");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Command))
        {
            parts.Add("Command");
        }

        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>How the shortcut is shown: "Ctrl+Alt+P" on Windows, "⌃⌥P" on a Mac.</summary>
    public string Label(bool mac)
    {
        var key = Info?.Label ?? Key;
        if (mac)
        {
            var symbols = (Modifiers.HasFlag(HotkeyModifiers.Control) ? "⌃" : "") + (Modifiers.HasFlag(HotkeyModifiers.Alt) ? "⌥" : "")
                + (Modifiers.HasFlag(HotkeyModifiers.Shift) ? "⇧" : "") + (Modifiers.HasFlag(HotkeyModifiers.Command) ? "⌘" : "");
            return symbols + key;
        }

        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(HotkeyModifiers.Command))
        {
            parts.Add("Win");
        }

        parts.Add(key);
        return string.Join('+', parts);
    }

    /// <summary>Reads a shortcut as the settings keep it, or null if it isn't one.</summary>
    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var modifiers = HotkeyModifiers.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        foreach (var part in parts[..^1])
        {
            if (!Enum.TryParse<HotkeyModifiers>(part, ignoreCase: true, out var modifier))
            {
                return null;
            }

            modifiers |= modifier;
        }

        return ByName.TryGetValue(parts[^1], out var key) ? new Hotkey(modifiers, key.Name) : null;
    }

    /// <summary>The key with this Windows virtual-key code, if a shortcut can use it.</summary>
    public static string? KeyForWindowsCode(int code) => Keys.FirstOrDefault(key => key.WindowsCode == code).Name;

    /// <summary>The key Avalonia calls this physical key (its <c>PhysicalKey</c> name), if a shortcut can use it.</summary>
    public static string? KeyForPhysical(string physical) => Keys.FirstOrDefault(key => key.Physical == physical).Name;

    /// <summary>What each action is called in the options.</summary>
    public static string ActionLabel(HotkeyAction action) => action switch
    {
        HotkeyAction.PlayPause => "Play or pause",
        HotkeyAction.Stop => "Stop",
        HotkeyAction.Next => "Next",
        HotkeyAction.Previous => "Previous",
        HotkeyAction.VolumeUp => "Volume up",
        HotkeyAction.VolumeDown => "Volume down",
        HotkeyAction.Favorite => "Favorite the song",
        HotkeyAction.ShowLucent => "Show Lucent",
        _ => action.ToString(),
    };

    /// <summary>The shortcuts set in <paramref name="saved"/> (the settings' <c>GlobalHotkeys</c>), each action's.</summary>
    public static IReadOnlyDictionary<HotkeyAction, Hotkey> Read(IReadOnlyDictionary<string, string> saved)
    {
        var shortcuts = new Dictionary<HotkeyAction, Hotkey>();
        foreach (var (name, text) in saved)
        {
            if (Enum.TryParse<HotkeyAction>(name, out var action) && Parse(text) is { IsUsable: true } hotkey)
            {
                shortcuts[action] = hotkey;
            }
        }

        return shortcuts;
    }

    private static List<KeyInfo> MakeKeys()
    {
        var keys = new List<KeyInfo>();
        // Mac key codes for the letters and digits, in the order of the ANSI keyboard (Carbon's kVK_ANSI_ codes).
        int[] macLetters = { 0x00, 0x0B, 0x08, 0x02, 0x0E, 0x03, 0x05, 0x04, 0x22, 0x26, 0x28, 0x25, 0x2E, 0x2D, 0x1F, 0x23, 0x0C, 0x0F, 0x01, 0x11, 0x20, 0x09, 0x0D, 0x07, 0x10, 0x06 };
        for (var i = 0; i < 26; i++)
        {
            var letter = ((char)('A' + i)).ToString();
            keys.Add(new KeyInfo(letter, 'A' + i, macLetters[i], letter, letter));
        }

        int[] macDigits = { 0x1D, 0x12, 0x13, 0x14, 0x15, 0x17, 0x16, 0x1A, 0x1C, 0x19 };
        for (var i = 0; i < 10; i++)
        {
            keys.Add(new KeyInfo(i.ToString(), '0' + i, macDigits[i], "Digit" + i, i.ToString()));
        }

        int[] macF = { 0x7A, 0x78, 0x63, 0x76, 0x60, 0x61, 0x62, 0x64, 0x65, 0x6D, 0x67, 0x6F, 0x69, 0x6B, 0x71, 0x6A, 0x40, 0x4F, 0x50 };
        for (var i = 0; i < macF.Length; i++)
        {
            var name = "F" + (i + 1);
            keys.Add(new KeyInfo(name, 0x70 + i, macF[i], name, name));
        }

        keys.Add(new KeyInfo("Space", 0x20, 0x31, "Space", "Space"));
        keys.Add(new KeyInfo("Home", 0x24, 0x73, "Home", "Home"));
        keys.Add(new KeyInfo("End", 0x23, 0x77, "End", "End"));
        keys.Add(new KeyInfo("PageUp", 0x21, 0x74, "PageUp", "Page Up"));
        keys.Add(new KeyInfo("PageDown", 0x22, 0x79, "PageDown", "Page Down"));
        keys.Add(new KeyInfo("Insert", 0x2D, 0x72, "Insert", "Insert"));
        keys.Add(new KeyInfo("Delete", 0x2E, 0x75, "Delete", "Delete"));
        keys.Add(new KeyInfo("Left", 0x25, 0x7B, "ArrowLeft", "←"));
        keys.Add(new KeyInfo("Right", 0x27, 0x7C, "ArrowRight", "→"));
        keys.Add(new KeyInfo("Up", 0x26, 0x7E, "ArrowUp", "↑"));
        keys.Add(new KeyInfo("Down", 0x28, 0x7D, "ArrowDown", "↓"));
        keys.Add(new KeyInfo("Comma", 0xBC, 0x2B, "Comma", ","));
        keys.Add(new KeyInfo("Period", 0xBE, 0x2F, "Period", "."));
        keys.Add(new KeyInfo("Minus", 0xBD, 0x1B, "Minus", "-"));
        keys.Add(new KeyInfo("Equals", 0xBB, 0x18, "Equal", "="));
        keys.Add(new KeyInfo("Slash", 0xBF, 0x2C, "Slash", "/"));
        keys.Add(new KeyInfo("Semicolon", 0xBA, 0x29, "Semicolon", ";"));
        keys.Add(new KeyInfo("Quote", 0xDE, 0x27, "Quote", "'"));
        keys.Add(new KeyInfo("LeftBracket", 0xDB, 0x21, "BracketLeft", "["));
        keys.Add(new KeyInfo("RightBracket", 0xDD, 0x1E, "BracketRight", "]"));
        keys.Add(new KeyInfo("Backslash", 0xDC, 0x2A, "Backslash", "\\"));
        keys.Add(new KeyInfo("Grave", 0xC0, 0x32, "Backquote", "`"));
        return keys;
    }
}
