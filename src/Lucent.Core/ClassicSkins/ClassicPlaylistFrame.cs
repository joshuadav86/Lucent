using System.Globalization;

namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>What a click on the classic playlist's frame asks the app to do.</summary>
public enum ClassicPlaylistCommand
{
    Close,
    Add,
    Remove,
    Select,
    Misc,
    List,
    Previous,
    Play,
    Pause,
    Stop,
    Next,
    Eject,
    ScrollUp,
    ScrollDown,
    ToggleTimeMode,
}

/// <summary>
/// The frame a classic Winamp skin draws round its playlist (pledit.bmp): the title bar, the edges with the
/// scroll bar, and the bottom bar with ADD, REM, SEL, MISC and LIST OPTS, the running time, a little
/// transport and the time. The songs themselves are the app's own list, laid over <see cref="ListArea"/> in
/// the skin's colours (<see cref="ClassicSkin.Playlist"/>), so selecting, dragging and menus work as ever.
/// Like Winamp's, the window grows in steps: 25 skin pixels across, 29 down.
/// </summary>
public sealed class ClassicPlaylistFrame : IClassicSurface
{
    public const int MinWidth = 275;
    public const int MinHeight = 116;
    public const int WidthStep = 25;
    public const int HeightStep = 29;

    // ---- Where everything is (pledit.bmp) ----

    private static readonly Sprite TopLeft = new("pledit", 0, 21, 25, 20);
    private static readonly Sprite TopLeftActive = new("pledit", 0, 0, 25, 20);
    private static readonly Sprite Title = new("pledit", 26, 21, 100, 20);
    private static readonly Sprite TitleActive = new("pledit", 26, 0, 100, 20);
    private static readonly Sprite TopTile = new("pledit", 127, 21, 25, 20);
    private static readonly Sprite TopTileActive = new("pledit", 127, 0, 25, 20);
    private static readonly Sprite TopRight = new("pledit", 153, 21, 25, 20);
    private static readonly Sprite TopRightActive = new("pledit", 153, 0, 25, 20);
    private static readonly Sprite LeftTile = new("pledit", 0, 42, 12, 29);
    private static readonly Sprite RightTile = new("pledit", 31, 42, 20, 29);
    private static readonly Sprite BottomTile = new("pledit", 179, 0, 25, 38);
    private static readonly Sprite BottomLeft = new("pledit", 0, 72, 125, 38);
    private static readonly Sprite BottomRight = new("pledit", 126, 72, 150, 38);
    private static readonly Sprite VisualizerBackground = new("pledit", 205, 0, 75, 38);
    private static readonly Sprite CloseDown = new("pledit", 52, 42, 9, 9);
    private static readonly Sprite ScrollHandle = new("pledit", 52, 53, 8, 18);
    private static readonly Sprite ScrollHandleDown = new("pledit", 61, 53, 8, 18);

    private const int TopHeight = 20;
    private const int BottomHeight = 38;
    private const int LeftWidth = 12;
    private const int RightWidth = 20;

    private readonly SkinCanvas _canvas = new(MinWidth, MinHeight);
    private Control _pressed = Control.None;
    private bool _pressedInside;
    private double _grabOffset;

    public ClassicPlaylistFrame(ClassicSkin skin) => Skin = skin;

    public ClassicSkin Skin { get; set; }

    /// <summary>The picture, <see cref="Width"/> × <see cref="Height"/>.</summary>
    public uint[] Pixels => _canvas.Pixels;

    public int Width => _canvas.Width;

    public int Height => _canvas.Height;

    /// <summary>Where the songs go, in skin pixels: inside the edges, between the title and bottom bars.</summary>
    public (int X, int Y, int Width, int Height) ListArea => (LeftWidth, TopHeight, Width - LeftWidth - RightWidth, Height - TopHeight - BottomHeight);

    // ---- What the app tells it ----

    public bool IsActive { get; set; } = true;

    /// <summary>How far down the list is scrolled, 0 to 1.</summary>
    public double ScrollFraction { get; set; }

    /// <summary>False when every song fits, so there's nothing to scroll.</summary>
    public bool CanScroll { get; set; }

    /// <summary>Winamp's running time: the selected songs' length and the whole list's, "3:45/42:10".</summary>
    public string RunningTime { get; set; } = string.Empty;

    public ClassicPlayState PlayState { get; set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; }

    public bool ShowRemaining { get; set; }

    // ---- What it tells the app ----

    public event Action<ClassicPlaylistCommand>? Command;

    /// <summary>The scroll bar was dragged: scroll to this, 0 (the top) to 1 (the bottom).</summary>
    public event Action<double>? ScrollRequested;

    /// <summary>The corner was dragged: make the window this size, in skin pixels (already in whole steps).</summary>
    public event Action<int, int>? ResizeRequested;

    /// <summary>The size in skin pixels, rounded to the steps a classic playlist grows in, and no smaller than the smallest.</summary>
    public static (int Width, int Height) Snap(double width, double height) =>
        (MinWidth + Math.Max(0, (int)Math.Round((width - MinWidth) / WidthStep)) * WidthStep,
         MinHeight + Math.Max(0, (int)Math.Round((height - MinHeight) / HeightStep)) * HeightStep);

    public void SetSize(int width, int height)
    {
        var (snappedWidth, snappedHeight) = Snap(width, height);
        _canvas.Resize(snappedWidth, snappedHeight);
    }

    // ---- Drawing ----

    public void Render()
    {
        var skin = Skin;
        int width = Width, height = Height;

        // The title bar: corners, tiles, and the title in the middle.
        _canvas.Tile(skin, IsActive ? TopTileActive : TopTile, 0, 0, width, TopHeight);
        _canvas.Draw(skin, IsActive ? TopLeftActive : TopLeft, 0, 0);
        _canvas.Draw(skin, IsActive ? TitleActive : Title, (width - Title.Width) / 2, 0);
        _canvas.Draw(skin, IsActive ? TopRightActive : TopRight, width - TopRight.Width, 0);
        if (IsDown(Control.Close))
        {
            _canvas.Draw(skin, CloseDown, CloseRect.X, CloseRect.Y);
        }

        // The edges, the right one with the scroll bar's groove in it.
        var middle = height - TopHeight - BottomHeight;
        _canvas.Tile(skin, LeftTile, 0, TopHeight, LeftWidth, middle);
        _canvas.Tile(skin, RightTile, width - RightWidth, TopHeight, RightWidth, middle);
        var style = skin.Playlist;
        for (var y = TopHeight; y < height - BottomHeight; y++)
        {
            for (var x = LeftWidth; x < width - RightWidth; x++)
            {
                _canvas.Plot(x, y, style.NormalBackground);
            }
        }

        _canvas.Draw(skin, IsDown(Control.ScrollHandle) ? ScrollHandleDown : ScrollHandle, width - 15, HandleTop);

        // The bottom bar: its two ends, tiles between, and the visualizer's spot once there's room.
        var bottom = height - BottomHeight;
        _canvas.Tile(skin, BottomTile, 0, bottom, width, BottomHeight);
        _canvas.Draw(skin, BottomLeft, 0, bottom);
        if (width >= MinWidth + 3 * WidthStep)
        {
            _canvas.Draw(skin, VisualizerBackground, width - BottomRight.Width - VisualizerBackground.Width, bottom);
        }

        var right = width - BottomRight.Width;
        _canvas.Draw(skin, BottomRight, right, bottom);
        _canvas.Text(skin, RunningTime.PadRight(18), right + 7, bottom + 10, 18 * ClassicSprites.GlyphWidth);
        DrawMiniTime(right + 66, bottom + 23);
    }

    /// <summary>The time in five of the skin's letters: a minus counting down, then "mm:ss" (the colon is in the frame).</summary>
    private void DrawMiniTime(int left, int top)
    {
        // Nothing while stopped, and blinking while paused, like Winamp.
        if (PlayState == ClassicPlayState.Stopped || (PlayState == ClassicPlayState.Paused && DateTime.UtcNow.Millisecond >= 500))
        {
            return;
        }

        var remaining = ShowRemaining && Duration > TimeSpan.Zero;
        var shown = remaining ? Duration - Position : Position;
        var total = Math.Max(0, (int)shown.TotalSeconds);
        var minutes = Math.Min(99, total / 60).ToString("00", CultureInfo.InvariantCulture);
        var seconds = (total % 60).ToString("00", CultureInfo.InvariantCulture);
        int[] at = { 1, 7, 12, 20, 25 };
        var letters = new[] { remaining ? '-' : ' ', minutes[0], minutes[1], seconds[0], seconds[1] };
        for (var i = 0; i < letters.Length; i++)
        {
            _canvas.Text(Skin, letters[i].ToString(), left + at[i], top, ClassicSprites.GlyphWidth);
        }
    }

    private int HandleTravel => Math.Max(0, Height - TopHeight - BottomHeight - ScrollHandle.Height);

    private int HandleTop => TopHeight + (CanScroll ? (int)Math.Round(Math.Clamp(ScrollFraction, 0, 1) * HandleTravel) : 0);

    // ---- The mouse ----

    private enum Control
    {
        None,
        Close,
        Add,
        Remove,
        Select,
        Misc,
        List,
        Previous,
        Play,
        Pause,
        Stop,
        Next,
        Eject,
        ScrollUp,
        ScrollDown,
        Time,
        ScrollHandle,
        Resize,
    }

    private bool IsDown(Control control) => _pressed == control && (_pressedInside || control == Control.ScrollHandle);

    private (int X, int Y, int Width, int Height) CloseRect => (Width - 11, 3, 9, 9);

    /// <summary>Where a bottom-bar button is, in skin pixels, for the app to open its menu above.</summary>
    public (int X, int Y, int Width, int Height) ButtonRect(ClassicPlaylistCommand command) => command switch
    {
        ClassicPlaylistCommand.Add => (14, Height - 30, 22, 18),
        ClassicPlaylistCommand.Remove => (43, Height - 30, 22, 18),
        ClassicPlaylistCommand.Select => (72, Height - 30, 22, 18),
        ClassicPlaylistCommand.Misc => (101, Height - 30, 22, 18),
        ClassicPlaylistCommand.List => (Width - 44, Height - 30, 22, 18),
        _ => (0, 0, Width, Height),
    };

    /// <summary>A mouse button went down at (x, y) in skin pixels.</summary>
    /// <param name="clicks">2 for a double click.</param>
    /// <returns>True when it's not on a control, so the app should start moving the window.</returns>
    public bool PointerDown(double x, double y, int clicks)
    {
        _pressed = HitTest(x, y);
        _pressedInside = true;
        switch (_pressed)
        {
            case Control.None:
                return true;
            case Control.ScrollHandle:
                // The handle moves with the pointer from wherever on it it was grabbed.
                _grabOffset = y - HandleTop;
                if (_grabOffset < 0 || _grabOffset >= ScrollHandle.Height)
                {
                    // A click in the groove jumps the handle there.
                    _grabOffset = ScrollHandle.Height / 2.0;
                    PointerMove(x, y);
                }

                break;
            case Control.Resize:
                _grabOffset = 0;
                break;
        }

        return false;
    }

    public void PointerMove(double x, double y)
    {
        switch (_pressed)
        {
            case Control.None:
                return;
            case Control.ScrollHandle:
                if (CanScroll && HandleTravel > 0)
                {
                    ScrollFraction = Math.Clamp((y - _grabOffset - TopHeight) / HandleTravel, 0, 1);
                    ScrollRequested?.Invoke(ScrollFraction);
                }

                return;
            case Control.Resize:
                var (width, height) = Snap(x + 10, y + 10);
                if (width != Width || height != Height)
                {
                    ResizeRequested?.Invoke(width, height);
                }

                return;
            default:
                _pressedInside = HitTest(x, y) == _pressed;
                return;
        }
    }

    public void PointerUp(double x, double y)
    {
        var released = _pressed;
        PointerMove(x, y);
        var inside = _pressedInside;
        _pressed = Control.None;
        if (!inside || CommandFor(released) is not { } command)
        {
            return;
        }

        Command?.Invoke(command);
    }

    private static ClassicPlaylistCommand? CommandFor(Control control) => control switch
    {
        Control.Close => ClassicPlaylistCommand.Close,
        Control.Add => ClassicPlaylistCommand.Add,
        Control.Remove => ClassicPlaylistCommand.Remove,
        Control.Select => ClassicPlaylistCommand.Select,
        Control.Misc => ClassicPlaylistCommand.Misc,
        Control.List => ClassicPlaylistCommand.List,
        Control.Previous => ClassicPlaylistCommand.Previous,
        Control.Play => ClassicPlaylistCommand.Play,
        Control.Pause => ClassicPlaylistCommand.Pause,
        Control.Stop => ClassicPlaylistCommand.Stop,
        Control.Next => ClassicPlaylistCommand.Next,
        Control.Eject => ClassicPlaylistCommand.Eject,
        Control.ScrollUp => ClassicPlaylistCommand.ScrollUp,
        Control.ScrollDown => ClassicPlaylistCommand.ScrollDown,
        Control.Time => ClassicPlaylistCommand.ToggleTimeMode,
        _ => null,
    };

    private Control HitTest(double x, double y)
    {
        var (closeX, closeY, closeWidth, closeHeight) = CloseRect;
        if (Inside(x, y, closeX, closeY, closeWidth, closeHeight))
        {
            return Control.Close;
        }

        if (y < TopHeight)
        {
            return Control.None;
        }

        var bottom = Height - BottomHeight;
        if (y < bottom)
        {
            return x >= Width - 15 && x < Width - 7 ? Control.ScrollHandle : Control.None;
        }

        (ClassicPlaylistCommand Command, Control Control)[] menus =
        {
            (ClassicPlaylistCommand.Add, Control.Add), (ClassicPlaylistCommand.Remove, Control.Remove),
            (ClassicPlaylistCommand.Select, Control.Select), (ClassicPlaylistCommand.Misc, Control.Misc),
            (ClassicPlaylistCommand.List, Control.List),
        };
        foreach (var (command, control) in menus)
        {
            var (left, top, width, height) = ButtonRect(command);
            if (Inside(x, y, left, top, width, height))
            {
                return control;
            }
        }

        var right = Width - BottomRight.Width;
        if (Inside(x, y, Width - 20, Height - 20, 20, 20))
        {
            return Control.Resize;
        }

        if (Inside(x, y, Width - 15, bottom + 2, 8, 5))
        {
            return Control.ScrollUp;
        }

        if (Inside(x, y, Width - 15, bottom + 8, 8, 5))
        {
            return Control.ScrollDown;
        }

        if (Inside(x, y, right + 3, bottom + 22, 60, 10))
        {
            return Control.Previous + (int)((x - right - 3) / 10);
        }

        if (Inside(x, y, right + 66, bottom + 23, 30, 6))
        {
            return Control.Time;
        }

        return Control.None;
    }

    private static bool Inside(double x, double y, int left, int top, int width, int height) =>
        x >= left && x < left + width && y >= top && y < top + height;
}
