namespace Lucent.ClassicSkins;

/// <summary>
/// The frame a classic skin draws round any other window (gen.bmp), as Winamp did round its media library and
/// visualizers: a title bar with the window's name in the skin's letters and a close button, edges, and a bottom
/// bar. Lucent puts it round its own windows (the visuals, the library, the artwork and the rest) while a classic
/// skin is on; the window's own content goes in <see cref="ContentArea"/>, in the skin's colours. Unlike the
/// playlist, it takes any size.
/// </summary>
public sealed class ClassicWindowFrame : IClassicSurface
{
    public const int MinWidth = 100;
    public const int MinHeight = 60;
    public const int TopHeight = 20;
    public const int BottomHeight = 14;
    public const int LeftWidth = 11;
    public const int RightWidth = 8;

    // ---- Where everything is (gen.bmp) ----

    private static readonly Sprite TopLeft = new("gen", 0, 21, 25, 20);
    private static readonly Sprite TopLeftActive = new("gen", 0, 0, 25, 20);
    private static readonly Sprite TopLeftEnd = new("gen", 26, 21, 25, 20);
    private static readonly Sprite TopLeftEndActive = new("gen", 26, 0, 25, 20);
    private static readonly Sprite TopCenter = new("gen", 52, 21, 25, 20);
    private static readonly Sprite TopCenterActive = new("gen", 52, 0, 25, 20);
    private static readonly Sprite TopRightEnd = new("gen", 78, 21, 25, 20);
    private static readonly Sprite TopRightEndActive = new("gen", 78, 0, 25, 20);
    private static readonly Sprite TopFill = new("gen", 104, 21, 25, 20);
    private static readonly Sprite TopFillActive = new("gen", 104, 0, 25, 20);
    private static readonly Sprite TopRight = new("gen", 130, 21, 25, 20);
    private static readonly Sprite TopRightActive = new("gen", 130, 0, 25, 20);
    private static readonly Sprite BottomLeft = new("gen", 0, 42, 125, 14);
    private static readonly Sprite BottomRight = new("gen", 0, 57, 125, 14);
    private static readonly Sprite BottomFill = new("gen", 127, 72, 25, 14);
    private static readonly Sprite MiddleLeft = new("gen", 127, 42, 11, 29);
    private static readonly Sprite MiddleLeftBottom = new("gen", 158, 42, 11, 24);
    private static readonly Sprite MiddleRight = new("gen", 139, 42, 8, 29);
    private static readonly Sprite MiddleRightBottom = new("gen", 170, 42, 8, 24);
    private static readonly Sprite CloseDown = new("gen", 148, 42, 9, 9);

    // The title's letters: a row of A to Z, 7 pixels high, each as wide as it is, with a column of the colour at x 0 between them.
    private const int ActiveLettersTop = 88;
    private const int LettersTop = 96;
    private const int LetterHeight = 7;
    private const int SpaceWidth = 5;
    private const int TitleTop = 4;

    private readonly SkinCanvas _canvas = new(MinWidth, MinHeight);
    private bool _closePressed;
    private bool _closeInside;

    public ClassicWindowFrame(ClassicSkin skin) => Skin = skin;

    public ClassicSkin Skin { get; set; }

    /// <summary>The window's name, drawn in the skin's capital letters ("VISUALS").</summary>
    public string Title { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public uint[] Pixels => _canvas.Pixels;

    public int Width => _canvas.Width;

    public int Height => _canvas.Height;

    /// <summary>Where the window's own content goes, in skin pixels: inside the edges, between the title and bottom bars.</summary>
    public (int X, int Y, int Width, int Height) ContentArea =>
        (LeftWidth, TopHeight, Width - LeftWidth - RightWidth, Height - TopHeight - BottomHeight);

    /// <summary>The close button was clicked.</summary>
    public event Action? CloseRequested;

    /// <summary>Makes the frame this size in skin pixels, no smaller than the smallest.</summary>
    public void SetSize(int width, int height) => _canvas.Resize(Math.Max(MinWidth, width), Math.Max(MinHeight, height));

    // ---- Drawing ----

    public void Render()
    {
        var skin = Skin;
        int width = Width, height = Height;
        var active = IsActive;

        // The title bar: a corner each end, then a cap either side of the title, with tiles filling between.
        var letters = Letters(skin, active);
        var titleWidth = TitleWidth(letters);
        var centreWidth = titleWidth + 7;
        var room = Math.Max(0, width - 4 * 25 - centreWidth);
        var leftFill = room / 2;
        var x = 0;
        _canvas.Tile(skin, active ? TopFillActive : TopFill, 0, 0, width, TopHeight);
        _canvas.Draw(skin, active ? TopLeftActive : TopLeft, x, 0);
        x += 25 + leftFill;
        _canvas.Draw(skin, active ? TopLeftEndActive : TopLeftEnd, x, 0);
        x += 25;
        _canvas.Tile(skin, active ? TopCenterActive : TopCenter, x, 0, centreWidth, TopHeight);
        DrawTitle(skin, letters, x + 4, TitleTop, centreWidth - 7);
        x += centreWidth;
        _canvas.Draw(skin, active ? TopRightEndActive : TopRightEnd, x, 0);
        _canvas.Draw(skin, active ? TopRightActive : TopRight, width - 25, 0);
        if (_closePressed && _closeInside)
        {
            var (closeX, closeY, _, _) = CloseRect;
            _canvas.Draw(skin, CloseDown, closeX, closeY);
        }

        // The edges, each with its own piece at the bottom.
        var middle = height - TopHeight - BottomHeight;
        var bottomPiece = Math.Min(MiddleLeftBottom.Height, middle);
        _canvas.Tile(skin, MiddleLeft, 0, TopHeight, LeftWidth, middle - bottomPiece);
        _canvas.Tile(skin, MiddleRight, width - RightWidth, TopHeight, RightWidth, middle - bottomPiece);
        _canvas.Draw(skin, MiddleLeftBottom, 0, TopHeight + middle - bottomPiece, LeftWidth, bottomPiece);
        _canvas.Draw(skin, MiddleRightBottom, width - RightWidth, TopHeight + middle - bottomPiece, RightWidth, bottomPiece);

        // The middle, in the playlist's background colour, for when the content doesn't cover it.
        var background = skin.Playlist.NormalBackground;
        for (var y = TopHeight; y < height - BottomHeight; y++)
        {
            for (var column = LeftWidth; column < width - RightWidth; column++)
            {
                _canvas.Plot(column, y, background);
            }
        }

        // The bottom bar: tiles, then its two ends (the right one last, with the grip, on top).
        var bottom = height - BottomHeight;
        _canvas.Tile(skin, BottomFill, 0, bottom, width, BottomHeight);
        _canvas.Draw(skin, BottomLeft, 0, bottom);
        _canvas.Draw(skin, BottomRight, width - BottomRight.Width, bottom);
    }

    private void DrawTitle(ClassicSkin skin, IReadOnlyDictionary<char, Sprite> letters, int left, int top, int width)
    {
        var x = left;
        foreach (var c in Title.ToUpperInvariant())
        {
            if (letters.TryGetValue(c, out var letter))
            {
                if (x + letter.Width > left + width)
                {
                    break;
                }

                _canvas.Draw(skin, letter, x, top);
                x += letter.Width;
            }
            else
            {
                x += SpaceWidth;
            }
        }
    }

    private int TitleWidth(IReadOnlyDictionary<char, Sprite> letters) =>
        Title.ToUpperInvariant().Sum(c => letters.TryGetValue(c, out var letter) ? letter.Width : SpaceWidth);

    /// <summary>
    /// A skin's title letters, found as Winamp finds them: from x 1 along the row, each letter runs until the
    /// next column whose top pixel is the colour at x 0. None when the skin has no gen.bmp.
    /// </summary>
    public static IReadOnlyDictionary<char, Sprite> Letters(ClassicSkin skin, bool active)
    {
        var letters = new Dictionary<char, Sprite>();
        var top = active ? ActiveLettersTop : LettersTop;
        if (skin.Sheet("gen") is not { } sheet || sheet.Height < top + LetterHeight)
        {
            return letters;
        }

        var background = sheet[0, top];
        var x = 1;
        for (var c = 'A'; c <= 'Z' && x < sheet.Width; c++)
        {
            var end = x;
            while (end < sheet.Width && sheet[end, top] != background)
            {
                end++;
            }

            if (end > x)
            {
                letters[c] = new Sprite("gen", x, top, end - x, LetterHeight);
            }

            x = end + 1;
        }

        return letters;
    }

    // ---- The mouse ----

    private (int X, int Y, int Width, int Height) CloseRect => (Width - 11, 3, 9, 9);

    private bool OnClose(double x, double y)
    {
        var (left, top, width, height) = CloseRect;
        return x >= left && x < left + width && y >= top && y < top + height;
    }

    /// <summary>A mouse button went down at (x, y) in skin pixels. True when it's not on the close button, so the window should be dragged.</summary>
    public bool PointerDown(double x, double y, int clicks)
    {
        _closePressed = OnClose(x, y);
        _closeInside = _closePressed;
        return !_closePressed;
    }

    public void PointerMove(double x, double y)
    {
        if (_closePressed)
        {
            _closeInside = OnClose(x, y);
        }
    }

    public void PointerUp(double x, double y)
    {
        var clicked = _closePressed && OnClose(x, y);
        _closePressed = false;
        _closeInside = false;
        if (clicked)
        {
            CloseRequested?.Invoke();
        }
    }
}
