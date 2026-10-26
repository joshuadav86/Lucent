namespace Lucent.ClassicSkins;

/// <summary>A rectangle of one of a skin's sheets.</summary>
public readonly record struct Sprite(string Sheet, int X, int Y, int Width, int Height);

/// <summary>
/// Where each piece of Winamp 2's main window lives in a skin's sheets, and where it goes in the window.
/// Every classic skin is drawn to this layout, so it's fixed. The window is 275 × 116 skin pixels.
/// </summary>
public static class ClassicSprites
{
    public const int WindowWidth = 275;
    public const int WindowHeight = 116;

    public static readonly Sprite Main = new("main", 0, 0, 275, 116);

    // ---- Title bar (titlebar.bmp) ----

    public static readonly Sprite TitleBar = new("titlebar", 27, 15, 275, 14);
    public static readonly Sprite TitleBarActive = new("titlebar", 27, 0, 275, 14);

    /// <summary>The title bar's buttons: their sprites up and pressed, and where they go.</summary>
    public static readonly (Sprite Up, Sprite Down, int X, int Y) OptionsButton = (new("titlebar", 0, 0, 9, 9), new("titlebar", 0, 9, 9, 9), 6, 3);
    public static readonly (Sprite Up, Sprite Down, int X, int Y) MinimizeButton = (new("titlebar", 9, 0, 9, 9), new("titlebar", 9, 9, 9, 9), 244, 3);
    public static readonly (Sprite Up, Sprite Down, int X, int Y) ShadeButton = (new("titlebar", 0, 18, 9, 9), new("titlebar", 9, 18, 9, 9), 254, 3);
    public static readonly (Sprite Up, Sprite Down, int X, int Y) CloseButton = (new("titlebar", 18, 0, 9, 9), new("titlebar", 18, 9, 9, 9), 264, 3);

    /// <summary>The little O A I D V column down the left of the display.</summary>
    public static readonly Sprite ClutterBar = new("titlebar", 304, 0, 8, 43);
    public const int ClutterBarX = 10;
    public const int ClutterBarY = 22;

    /// <summary>The clutter bar's letters: their top and height in the bar, and the sprite of each pressed.</summary>
    public static readonly (int Top, int Height, Sprite Down)[] ClutterButtons =
    {
        (3, 8, new("titlebar", 304, 47, 8, 8)),
        (11, 7, new("titlebar", 312, 55, 8, 7)),
        (18, 7, new("titlebar", 320, 62, 8, 7)),
        (25, 8, new("titlebar", 328, 69, 8, 8)),
        (33, 7, new("titlebar", 336, 77, 8, 7)),
    };

    // ---- Transport (cbuttons.bmp) ----

    public static readonly (Sprite Up, Sprite Down, int X, int Y)[] Transport =
    {
        (new("cbuttons", 0, 0, 23, 18), new("cbuttons", 0, 18, 23, 18), 16, 88),
        (new("cbuttons", 23, 0, 23, 18), new("cbuttons", 23, 18, 23, 18), 39, 88),
        (new("cbuttons", 46, 0, 23, 18), new("cbuttons", 46, 18, 23, 18), 62, 88),
        (new("cbuttons", 69, 0, 23, 18), new("cbuttons", 69, 18, 23, 18), 85, 88),
        (new("cbuttons", 92, 0, 22, 18), new("cbuttons", 92, 18, 22, 18), 108, 88),
        (new("cbuttons", 114, 0, 22, 16), new("cbuttons", 114, 16, 22, 16), 136, 89),
    };

    // ---- Display ----

    /// <summary>Playing, paused and stopped marks (playpaus.bmp), shown at <see cref="PlayStateX"/>.</summary>
    public static readonly Sprite Playing = new("playpaus", 0, 0, 9, 9);
    public static readonly Sprite Paused = new("playpaus", 9, 0, 9, 9);
    public static readonly Sprite Stopped = new("playpaus", 18, 0, 9, 9);
    public static readonly Sprite Working = new("playpaus", 39, 0, 3, 9);
    public static readonly Sprite NotWorking = new("playpaus", 36, 0, 3, 9);
    public const int PlayStateX = 26;
    public const int PlayStateY = 28;
    public const int WorkingX = 24;

    /// <summary>The time's digits (numbers.bmp or nums_ex.bmp, 9 × 13 each) and where each goes.</summary>
    public static Sprite Digit(int digit, bool extended) => new(extended ? "nums_ex" : "numbers", digit * 9, 0, 9, 13);
    public static Sprite BlankDigit(bool extended) => new(extended ? "nums_ex" : "numbers", 90, 0, 9, 13);
    public static readonly int[] DigitX = { 48, 60, 78, 90 };
    public const int DigitY = 26;

    /// <summary>numbers.bmp has no minus sign: Winamp borrows a dash from the middle of the 2.</summary>
    public static readonly Sprite Minus = new("numbers", 20, 6, 5, 1);
    public const int MinusX = 38;
    public const int MinusY = 32;
    public static readonly Sprite MinusExtended = new("nums_ex", 99, 0, 9, 13);
    public const int MinusExtendedX = 38;

    public static readonly Sprite Stereo = new("monoster", 0, 12, 29, 12);
    public static readonly Sprite StereoOn = new("monoster", 0, 0, 29, 12);
    public static readonly Sprite Mono = new("monoster", 29, 12, 27, 12);
    public static readonly Sprite MonoOn = new("monoster", 29, 0, 27, 12);
    public const int StereoX = 239;
    public const int MonoX = 212;
    public const int MonoStereoY = 41;

    /// <summary>The scrolling title (31 letters of text.bmp), the bitrate (3) and the sample rate (2).</summary>
    public const int TitleX = 111;
    public const int TitleY = 27;
    public const int TitleWidth = 154;
    public const int KbpsX = 111;
    public const int KhzX = 156;
    public const int RateY = 43;

    /// <summary>The little visualizer.</summary>
    public const int VisX = 24;
    public const int VisY = 43;
    public const int VisWidth = 76;
    public const int VisHeight = 16;

    // ---- Sliders ----

    public const int SeekX = 16;
    public const int SeekY = 72;
    public static readonly Sprite SeekBar = new("posbar", 0, 0, 248, 10);
    public static readonly Sprite SeekThumb = new("posbar", 248, 0, 29, 10);
    public static readonly Sprite SeekThumbDown = new("posbar", 278, 0, 29, 10);

    public const int VolumeX = 107;
    public const int VolumeY = 57;
    public const int VolumeWidth = 68;
    public const int BalanceX = 177;
    public const int BalanceY = 57;
    public const int BalanceWidth = 38;
    public const int SliderHeight = 13;
    public const int ThumbWidth = 14;

    /// <summary>One of the volume (or balance) bar's 28 backgrounds, which darken or redden as it goes up.</summary>
    public static Sprite VolumeFrame(int frame) => new("volume", 0, frame * 15, 68, 13);
    public static Sprite BalanceFrame(int frame) => new("balance", 9, frame * 15, 38, 13);
    public static readonly Sprite VolumeThumb = new("volume", 15, 422, 14, 11);
    public static readonly Sprite VolumeThumbDown = new("volume", 0, 422, 14, 11);
    public static readonly Sprite BalanceThumb = new("balance", 15, 422, 14, 11);
    public static readonly Sprite BalanceThumbDown = new("balance", 0, 422, 14, 11);

    // ---- Toggles (shufrep.bmp) ----

    /// <summary>A two-state button's four looks: off, off pressed, on, on pressed.</summary>
    public readonly record struct Toggle(Sprite Off, Sprite OffDown, Sprite On, Sprite OnDown, int X, int Y);

    public static readonly Toggle Shuffle = new(
        new("shufrep", 28, 0, 47, 15), new("shufrep", 28, 15, 47, 15),
        new("shufrep", 28, 30, 47, 15), new("shufrep", 28, 45, 47, 15), 164, 89);

    public static readonly Toggle Repeat = new(
        new("shufrep", 0, 0, 28, 15), new("shufrep", 0, 15, 28, 15),
        new("shufrep", 0, 30, 28, 15), new("shufrep", 0, 45, 28, 15), 210, 89);

    public static readonly Toggle Equalizer = new(
        new("shufrep", 0, 61, 23, 12), new("shufrep", 46, 61, 23, 12),
        new("shufrep", 0, 73, 23, 12), new("shufrep", 46, 73, 23, 12), 219, 58);

    public static readonly Toggle Playlist = new(
        new("shufrep", 23, 61, 23, 12), new("shufrep", 69, 61, 23, 12),
        new("shufrep", 23, 73, 23, 12), new("shufrep", 69, 73, 23, 12), 242, 58);

    // ---- Text (text.bmp) ----

    /// <summary>Where a character is in text.bmp's grid of 5 × 6 letters (row, column), or null for one it doesn't have.</summary>
    public static (int Row, int Column)? Glyph(char c)
    {
        c = char.ToLowerInvariant(c);
        if (c is >= 'a' and <= 'z')
        {
            return (0, c - 'a');
        }

        if (c is >= '0' and <= '9')
        {
            return (1, c - '0');
        }

        return c switch
        {
            '"' => (0, 26),
            '@' => (0, 27),
            ' ' => (0, 30),
            '…' => (1, 10),
            '.' => (1, 11),
            ':' => (1, 12),
            '(' => (1, 13),
            ')' => (1, 14),
            '-' or '–' or '—' => (1, 15),
            '\'' or '’' or '‘' or '`' => (1, 16),
            '!' => (1, 17),
            '_' => (1, 18),
            '+' => (1, 19),
            '\\' => (1, 20),
            '/' => (1, 21),
            '[' or '{' or '<' => (1, 22),
            ']' or '}' or '>' => (1, 23),
            '^' => (1, 24),
            '&' => (1, 25),
            '%' => (1, 26),
            ',' => (1, 27),
            '=' => (1, 28),
            '$' => (1, 29),
            '#' => (1, 30),
            'å' => (2, 0),
            'ö' => (2, 1),
            'ä' => (2, 2),
            '?' => (2, 3),
            '*' => (2, 4),
            _ => null,
        };
    }

    public const int GlyphWidth = 5;
    public const int GlyphHeight = 6;
}
