using System.Globalization;
using System.Text;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>What a click in the classic player asks the app to do.</summary>
public enum ClassicCommand
{
    Previous,
    Play,
    Pause,
    Stop,
    Next,
    Open,
    Shuffle,
    Repeat,
    Equalizer,
    Playlist,
    Menu,
    Minimize,
    Shade,
    Close,
    ToggleTimeMode,
    CycleVisualizer,
    AlwaysOnTop,
    TrackInfo,
    DoubleSize,
    Visuals,
}

public enum ClassicPlayState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// The player drawn by a classic Winamp skin: Winamp 2's main window, 275 × 116 skin pixels, or its 14-pixel
/// "window shade" strip. Both apps hand it the player's state, show <see cref="Pixels"/> (scaled up with
/// hard edges), and pass the mouse in, in skin pixels. Everything else (drawing, the little visualizer, the
/// scrolling title, buttons and sliders) happens here, so the two apps behave the same.
/// </summary>
public sealed class ClassicPlayer
{
    private const int Width = ClassicSprites.WindowWidth;
    private const int BarCount = 19;
    private const double ScrollStep = 0.15;

    private readonly AudioAnalyzer _analyzer;
    private readonly float[] _bars = new float[BarCount];
    private readonly float[] _peaks = new float[BarCount];
    private readonly float[] _peakHold = new float[BarCount];
    private ClassicSkin _skin;
    private double _clock;
    private double _scrollTimer;
    private int _scroll;
    private string _shownTitle = string.Empty;

    private Control _pressed = Control.None;
    private bool _pressedInside;
    private double _dragValue;

    public ClassicPlayer(ClassicSkin skin, AudioCapture capture)
    {
        _skin = skin;
        _analyzer = new AudioAnalyzer(capture);
    }

    public ClassicSkin Skin
    {
        get => _skin;
        set => _skin = value;
    }

    /// <summary>The picture: <see cref="ClassicSprites.WindowWidth"/> wide, of which the top <see cref="Height"/> rows are showing.</summary>
    public uint[] Pixels { get; } = new uint[Width * ClassicSprites.WindowHeight];

    /// <summary>116 normally, 14 in window shade mode.</summary>
    public int Height => IsShaded ? 14 : ClassicSprites.WindowHeight;

    // ---- What the app tells it ----

    public ClassicPlayState PlayState { get; set; }

    public TimeSpan Position { get; set; }

    public TimeSpan Duration { get; set; }

    public bool ShowRemaining { get; set; }

    /// <summary>The scrolling title: "3. Artist - Title (4:20)", say.</summary>
    public string Title { get; set; } = string.Empty;

    public int Kbps { get; set; }

    public int SampleRate { get; set; }

    public int Channels { get; set; }

    /// <summary>0 to 1.</summary>
    public double Volume { get; set; }

    /// <summary>-1 (left only) to 1 (right only).</summary>
    public double Balance { get; set; }

    public bool CanSeek { get; set; }

    public bool Shuffle { get; set; }

    public bool Repeat { get; set; }

    public bool EqualizerOpen { get; set; }

    public bool PlaylistOpen { get; set; }

    public bool AlwaysOnTop { get; set; }

    public bool DoubleSize { get; set; }

    public bool IsShaded { get; set; }

    /// <summary>Whether the window has focus, which lights up the title bar.</summary>
    public bool IsActive { get; set; } = true;

    public MeterMode Visualizer { get; set; } = MeterMode.Bars;

    // ---- What it tells the app ----

    public event Action<ClassicCommand>? Command;

    /// <summary>The seek bar was let go of here: 0 (the start) to 1 (the end).</summary>
    public event Action<double>? SeekRequested;

    /// <summary>The volume slider moved (0 to 1). <see cref="Volume"/> has already changed.</summary>
    public event Action<double>? VolumeChanged;

    /// <summary>The balance slider moved (-1 to 1). <see cref="Balance"/> has already changed.</summary>
    public event Action<double>? BalanceChanged;

    /// <summary>A still of a skin's player, stopped, with the skin's name as the title: for the skin picker.</summary>
    public static uint[] Preview(ClassicSkin skin)
    {
        var player = new ClassicPlayer(skin, new AudioCapture())
        {
            Visualizer = MeterMode.Off,
            Volume = 0.8,
            Title = skin.Name,
        };
        player.Render();
        return player.Pixels;
    }

    /// <summary>Moves the visualizer and scrolling title on. Call once a frame.</summary>
    public void Tick(double seconds)
    {
        seconds = Math.Clamp(seconds, 0, 0.1);
        _clock += seconds;
        _scrollTimer += seconds;
        while (_scrollTimer >= ScrollStep)
        {
            _scrollTimer -= ScrollStep;
            _scroll += ClassicSprites.GlyphWidth;
        }

        if (Visualizer == MeterMode.Off)
        {
            return;
        }

        _analyzer.Update((float)seconds);
        var spectrum = _analyzer.TextureData;
        var perBar = AudioAnalyzer.TextureWidth / BarCount;
        var dt = (float)seconds;
        for (var bar = 0; bar < BarCount; bar++)
        {
            var level = 0f;
            for (var i = 0; i < perBar; i++)
            {
                level = Math.Max(level, spectrum[bar * perBar + i]);
            }

            level = Math.Clamp(level, 0f, 1f);
            _bars[bar] = level > _bars[bar] ? level : Math.Max(level, _bars[bar] - dt * 2.2f);
            if (_bars[bar] >= _peaks[bar])
            {
                _peaks[bar] = _bars[bar];
                _peakHold[bar] = 0.35f;
            }
            else if ((_peakHold[bar] -= dt) < 0)
            {
                _peaks[bar] = Math.Max(_bars[bar], _peaks[bar] - dt * 0.9f);
            }
        }
    }

    // ---- Drawing ----

    /// <summary>Draws the whole window into <see cref="Pixels"/>.</summary>
    public void Render()
    {
        if (IsShaded)
        {
            RenderShade();
            return;
        }

        Draw(ClassicSprites.Main, 0, 0);
        Draw(IsActive ? ClassicSprites.TitleBarActive : ClassicSprites.TitleBar, 0, 0);
        DrawButton(ClassicSprites.OptionsButton, Control.Options);
        DrawButton(ClassicSprites.MinimizeButton, Control.Minimize);
        DrawButton(ClassicSprites.ShadeButton, Control.Shade);
        DrawButton(ClassicSprites.CloseButton, Control.Close);

        Draw(ClassicSprites.ClutterBar, ClassicSprites.ClutterBarX, ClassicSprites.ClutterBarY);
        for (var i = 0; i < ClassicSprites.ClutterButtons.Length; i++)
        {
            var on = IsDown(Control.ClutterO + i)
                || (Control.ClutterO + i == Control.ClutterA && AlwaysOnTop)
                || (Control.ClutterO + i == Control.ClutterD && DoubleSize);
            if (on)
            {
                var (top, _, down) = ClassicSprites.ClutterButtons[i];
                Draw(down, ClassicSprites.ClutterBarX, ClassicSprites.ClutterBarY + top);
            }
        }

        DrawPlayState();
        DrawTime();
        DrawMonoStereo();
        DrawText(TitleText(), ClassicSprites.TitleX, ClassicSprites.TitleY, ClassicSprites.TitleWidth, scroll: true);
        if (PlayState != ClassicPlayState.Stopped || Kbps > 0)
        {
            DrawText(KbpsText(), ClassicSprites.KbpsX, ClassicSprites.RateY, 15, scroll: false, rightAlign: true);
            DrawText(KhzText(), ClassicSprites.KhzX, ClassicSprites.RateY, 10, scroll: false, rightAlign: true);
        }

        DrawVisualizer();
        DrawSliders();

        for (var i = 0; i < ClassicSprites.Transport.Length; i++)
        {
            DrawButton(ClassicSprites.Transport[i], Control.Previous + i);
        }

        DrawToggle(ClassicSprites.Shuffle, Shuffle, Control.Shuffle);
        DrawToggle(ClassicSprites.Repeat, Repeat, Control.Repeat);
        DrawToggle(ClassicSprites.Equalizer, EqualizerOpen, Control.Equalizer);
        DrawToggle(ClassicSprites.Playlist, PlaylistOpen, Control.Playlist);
    }

    private void RenderShade()
    {
        Draw(IsActive ? ShadeBackgroundActive : ShadeBackground, 0, 0);
        DrawButton(ClassicSprites.OptionsButton, Control.Options);
        DrawButton(ClassicSprites.MinimizeButton, Control.Minimize);
        DrawButton((Unshade, UnshadeDown, ClassicSprites.ShadeButton.X, ClassicSprites.ShadeButton.Y), Control.Shade);
        DrawButton(ClassicSprites.CloseButton, Control.Close);
        if (PlayState != ClassicPlayState.Stopped)
        {
            DrawText(ShadeTime(), 125, 4, 30, scroll: false);
        }
    }

    // The window shade strip's own sprites, in titlebar.bmp.
    private static readonly Sprite ShadeBackground = new("titlebar", 27, 42, 275, 14);
    private static readonly Sprite ShadeBackgroundActive = new("titlebar", 27, 29, 275, 14);
    private static readonly Sprite Unshade = new("titlebar", 0, 27, 9, 9);
    private static readonly Sprite UnshadeDown = new("titlebar", 9, 27, 9, 9);

    private void DrawPlayState()
    {
        switch (PlayState)
        {
            case ClassicPlayState.Playing:
                Draw(ClassicSprites.Working, ClassicSprites.WorkingX, ClassicSprites.PlayStateY);
                Draw(ClassicSprites.Playing, ClassicSprites.PlayStateX, ClassicSprites.PlayStateY);
                break;
            case ClassicPlayState.Paused:
                Draw(ClassicSprites.Paused, ClassicSprites.PlayStateX, ClassicSprites.PlayStateY);
                break;
            default:
                Draw(ClassicSprites.Stopped, ClassicSprites.PlayStateX, ClassicSprites.PlayStateY);
                break;
        }
    }

    private void DrawTime()
    {
        // Nothing while stopped, and blinking while paused, like Winamp.
        if (PlayState == ClassicPlayState.Stopped || (PlayState == ClassicPlayState.Paused && _clock % 1 >= 0.5))
        {
            return;
        }

        var extended = _skin.UsesExtendedNumbers;
        var (negative, minutes, seconds) = TimeParts(_pressed == Control.Seek ? DragPosition : Position);
        if (negative)
        {
            if (extended)
            {
                Draw(ClassicSprites.MinusExtended, ClassicSprites.MinusExtendedX, ClassicSprites.DigitY);
            }
            else
            {
                Draw(ClassicSprites.Minus, ClassicSprites.MinusX, ClassicSprites.MinusY);
            }
        }

        int[] digits = { minutes / 10, minutes % 10, seconds / 10, seconds % 10 };
        for (var i = 0; i < 4; i++)
        {
            Draw(ClassicSprites.Digit(digits[i], extended), ClassicSprites.DigitX[i], ClassicSprites.DigitY);
        }
    }

    private (bool Negative, int Minutes, int Seconds) TimeParts(TimeSpan position)
    {
        var remaining = ShowRemaining && Duration > TimeSpan.Zero;
        var shown = remaining ? Duration - position : position;
        var total = Math.Max(0, (int)shown.TotalSeconds);
        return (remaining, Math.Min(99, total / 60), total % 60);
    }

    /// <summary>The time in the shade strip, in the skin's small letters: " 1:23", or "-1:23" counting down.</summary>
    private string ShadeTime()
    {
        var (negative, minutes, seconds) = TimeParts(Position);
        return (negative ? "-" : " ") + string.Create(CultureInfo.InvariantCulture, $"{minutes,2}:{seconds:00}");
    }

    private void DrawMonoStereo()
    {
        var showing = PlayState != ClassicPlayState.Stopped;
        Draw(showing && Channels >= 2 ? ClassicSprites.StereoOn : ClassicSprites.Stereo, ClassicSprites.StereoX, ClassicSprites.MonoStereoY);
        Draw(showing && Channels == 1 ? ClassicSprites.MonoOn : ClassicSprites.Mono, ClassicSprites.MonoX, ClassicSprites.MonoStereoY);
    }

    /// <summary>The title, or what a slider is being dragged to, as Winamp shows it.</summary>
    private string TitleText()
    {
        switch (_pressed)
        {
            case Control.Seek when Duration > TimeSpan.Zero:
                var to = DragPosition;
                return string.Create(CultureInfo.InvariantCulture,
                    $"Seek to: {Clock(to)}/{Clock(Duration)} ({_dragValue * 100:0}%)");
            case Control.Volume:
                return string.Create(CultureInfo.InvariantCulture, $"Volume: {Volume * 100:0}%");
            case Control.Balance:
                return Math.Abs(Balance) < 0.01
                    ? "Balance: Center"
                    : string.Create(CultureInfo.InvariantCulture, $"Balance: {Math.Abs(Balance) * 100:0}% {(Balance < 0 ? "Left" : "Right")}");
            default:
                return Title;
        }
    }

    private static string Clock(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}:{time.Seconds:00}");

    private string KbpsText() => Kbps switch
    {
        <= 0 => string.Empty,
        < 1000 => Kbps.ToString(CultureInfo.InvariantCulture),
        // Lossless rates don't fit in three letters: 1411 shows as 1k4.
        _ => string.Create(CultureInfo.InvariantCulture, $"{Math.Min(9, Kbps / 1000)}k{Kbps % 1000 / 100}"),
    };

    private string KhzText()
    {
        var khz = (int)Math.Round(SampleRate / 1000.0);
        // Two letters: 44 and 48 fit; 88.2 kHz and up are hi-res, shown as "HI".
        return khz switch
        {
            <= 0 => string.Empty,
            < 100 => khz.ToString(CultureInfo.InvariantCulture),
            _ => "HI",
        };
    }

    private void DrawVisualizer()
    {
        var colours = _skin.VisColors;
        int left = ClassicSprites.VisX, top = ClassicSprites.VisY;
        for (var y = 0; y < ClassicSprites.VisHeight; y++)
        {
            for (var x = 0; x < ClassicSprites.VisWidth; x++)
            {
                // Winamp's background has a faint dot on every other pixel of every other row.
                Plot(left + x, top + y, colours[(x & 1) == 1 && (y & 1) == 1 ? 1 : 0]);
            }
        }

        switch (Visualizer)
        {
            case MeterMode.Bars:
                for (var bar = 0; bar < BarCount; bar++)
                {
                    var height = (int)Math.Round(_bars[bar] * ClassicSprites.VisHeight);
                    for (var y = ClassicSprites.VisHeight - height; y < ClassicSprites.VisHeight; y++)
                    {
                        for (var x = 0; x < 3; x++)
                        {
                            // Each row has its own colour, top to bottom, however tall the bar.
                            Plot(left + bar * 4 + x, top + y, colours[2 + y]);
                        }
                    }

                    var peak = (int)Math.Round(_peaks[bar] * ClassicSprites.VisHeight);
                    if (peak > 0)
                    {
                        var y = Math.Clamp(ClassicSprites.VisHeight - peak - 1, 0, ClassicSprites.VisHeight - 1);
                        for (var x = 0; x < 3; x++)
                        {
                            Plot(left + bar * 4 + x, top + y, colours[23]);
                        }
                    }
                }

                break;

            case MeterMode.Scope:
                var wave = _analyzer.TextureData.AsSpan(AudioAnalyzer.TextureWidth);
                var previous = -1;
                for (var x = 0; x < ClassicSprites.VisWidth; x++)
                {
                    var sample = wave[x * (wave.Length - 1) / (ClassicSprites.VisWidth - 1)];
                    var y = Math.Clamp((int)Math.Round(7.5 - sample * 8), 0, ClassicSprites.VisHeight - 1);
                    var from = previous < 0 ? y : Math.Min(previous, y);
                    var to = previous < 0 ? y : Math.Max(previous, y);
                    for (var row = from; row <= to; row++)
                    {
                        // Brightest at the edges, dimmest through the middle.
                        var shade = Math.Min(4, (int)(Math.Abs(row - 7.5) / 2));
                        Plot(left + x, top + row, colours[22 - shade]);
                    }

                    previous = y;
                }

                break;
        }
    }

    private void DrawSliders()
    {
        if (CanSeek)
        {
            Draw(ClassicSprites.SeekBar, ClassicSprites.SeekX, ClassicSprites.SeekY);
            var fraction = _pressed == Control.Seek ? _dragValue
                : Duration > TimeSpan.Zero ? Math.Clamp(Position / Duration, 0, 1) : 0;
            var travel = ClassicSprites.SeekBar.Width - ClassicSprites.SeekThumb.Width;
            Draw(IsDown(Control.Seek) ? ClassicSprites.SeekThumbDown : ClassicSprites.SeekThumb,
                ClassicSprites.SeekX + (int)Math.Round(fraction * travel), ClassicSprites.SeekY);
        }

        var volume = Math.Clamp(Volume, 0, 1);
        Draw(ClassicSprites.VolumeFrame((int)Math.Round(volume * 27)), ClassicSprites.VolumeX, ClassicSprites.VolumeY);
        Draw(IsDown(Control.Volume) ? ClassicSprites.VolumeThumbDown : ClassicSprites.VolumeThumb,
            ClassicSprites.VolumeX + (int)Math.Round(volume * (ClassicSprites.VolumeWidth - ClassicSprites.ThumbWidth)),
            ClassicSprites.VolumeY + 1);

        var balance = Math.Clamp(Balance, -1, 1);
        Draw(ClassicSprites.BalanceFrame((int)Math.Round(Math.Abs(balance) * 27)), ClassicSprites.BalanceX, ClassicSprites.BalanceY);
        Draw(IsDown(Control.Balance) ? ClassicSprites.BalanceThumbDown : ClassicSprites.BalanceThumb,
            ClassicSprites.BalanceX + (int)Math.Round((balance + 1) / 2 * (ClassicSprites.BalanceWidth - ClassicSprites.ThumbWidth)),
            ClassicSprites.BalanceY + 1);
    }

    private void DrawButton((Sprite Up, Sprite Down, int X, int Y) button, Control control) =>
        Draw(IsDown(control) ? button.Down : button.Up, button.X, button.Y);

    private void DrawToggle(ClassicSprites.Toggle toggle, bool on, Control control)
    {
        var down = IsDown(control);
        Draw(on ? down ? toggle.OnDown : toggle.On : down ? toggle.OffDown : toggle.Off, toggle.X, toggle.Y);
    }

    /// <summary>Text in the skin's font. Too long to fit, it scrolls round, with "***" between laps.</summary>
    private void DrawText(string text, int left, int top, int width, bool scroll, bool rightAlign = false)
    {
        text = Simplify(text);
        var textWidth = text.Length * ClassicSprites.GlyphWidth;
        var offset = 0;
        if (scroll && textWidth > width)
        {
            if (!string.Equals(text, _shownTitle, StringComparison.Ordinal))
            {
                _shownTitle = text;
                _scroll = 0;
            }

            text += "  ***  ";
            textWidth = text.Length * ClassicSprites.GlyphWidth;
            offset = _scroll % textWidth;
            text += text;
        }
        else if (rightAlign)
        {
            offset = textWidth - width;
        }

        var font = _skin.Sheet("text");
        var blank = ClassicSprites.Glyph(' ')!.Value;
        for (var x = 0; x < width; x++)
        {
            var at = x + offset;
            var c = at >= 0 && at / ClassicSprites.GlyphWidth < text.Length ? text[at / ClassicSprites.GlyphWidth] : ' ';
            var (row, column) = ClassicSprites.Glyph(c) ?? blank;
            var sourceX = column * ClassicSprites.GlyphWidth + ((at % ClassicSprites.GlyphWidth) + ClassicSprites.GlyphWidth) % ClassicSprites.GlyphWidth;
            for (var y = 0; y < ClassicSprites.GlyphHeight; y++)
            {
                if (font is not null && sourceX < font.Width && row * ClassicSprites.GlyphHeight + y < font.Height)
                {
                    Plot(left + x, top + y, font[sourceX, row * ClassicSprites.GlyphHeight + y]);
                }
            }
        }
    }

    /// <summary>Takes accents off letters the skin's font doesn't have (é becomes e), keeping the few it does.</summary>
    private static string Simplify(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (ClassicSprites.Glyph(c) is not null)
            {
                builder.Append(c);
                continue;
            }

            var plain = c.ToString().Normalize(NormalizationForm.FormD)
                .FirstOrDefault(part => ClassicSprites.Glyph(part) is not null);
            builder.Append(plain == default ? ' ' : plain);
        }

        return builder.ToString();
    }

    private void Draw(Sprite sprite, int left, int top)
    {
        if (_skin.Sheet(sprite.Sheet) is not { } sheet)
        {
            return;
        }

        // Parts of a sprite past the edge of its sheet (some skins' sheets are smaller than Winamp's) are left out.
        var width = Math.Min(sprite.Width, sheet.Width - sprite.X);
        var height = Math.Min(sprite.Height, sheet.Height - sprite.Y);
        for (var y = 0; y < height; y++)
        {
            var targetY = top + y;
            if (targetY < 0 || targetY >= ClassicSprites.WindowHeight)
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var targetX = left + x;
                if (targetX >= 0 && targetX < Width)
                {
                    Pixels[targetY * Width + targetX] = sheet.Pixels[(sprite.Y + y) * sheet.Width + sprite.X + x];
                }
            }
        }
    }

    private void Plot(int x, int y, uint colour)
    {
        if (x >= 0 && x < Width && y >= 0 && y < ClassicSprites.WindowHeight)
        {
            Pixels[y * Width + x] = 0xFF000000 | colour;
        }
    }

    // ---- The mouse ----

    private enum Control
    {
        None,
        Options,
        Minimize,
        Shade,
        Close,
        ClutterO,
        ClutterA,
        ClutterI,
        ClutterD,
        ClutterV,
        Previous,
        Play,
        Pause,
        Stop,
        Next,
        Eject,
        Shuffle,
        Repeat,
        Equalizer,
        Playlist,
        Seek,
        Volume,
        Balance,
        Time,
        Vis,
    }

    private bool IsDown(Control control) => _pressed == control && (_pressedInside || control is Control.Seek or Control.Volume or Control.Balance);

    private TimeSpan DragPosition => Duration * _dragValue;

    /// <summary>A mouse button went down at (x, y) in skin pixels.</summary>
    /// <param name="clicks">2 for a double click.</param>
    /// <returns>True when it's not on a control, so the app should start moving the window.</returns>
    public bool PointerDown(double x, double y, int clicks = 1)
    {
        _pressed = HitTest(x, y);
        _pressedInside = true;
        switch (_pressed)
        {
            case Control.None:
                // Double-clicking the title bar rolls the window up into its shade strip, and back.
                if (clicks == 2 && y < 14)
                {
                    Command?.Invoke(ClassicCommand.Shade);
                }

                return true;
            case Control.Seek or Control.Volume or Control.Balance:
                PointerMove(x, y);
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
            case Control.Seek:
                var travel = ClassicSprites.SeekBar.Width - ClassicSprites.SeekThumb.Width;
                _dragValue = Math.Clamp((x - ClassicSprites.SeekX - ClassicSprites.SeekThumb.Width / 2.0) / travel, 0, 1);
                return;
            case Control.Volume:
                Volume = Math.Clamp((x - ClassicSprites.VolumeX - ClassicSprites.ThumbWidth / 2.0)
                    / (ClassicSprites.VolumeWidth - ClassicSprites.ThumbWidth), 0, 1);
                VolumeChanged?.Invoke(Volume);
                return;
            case Control.Balance:
                var balance = Math.Clamp((x - ClassicSprites.BalanceX - ClassicSprites.ThumbWidth / 2.0)
                    / (ClassicSprites.BalanceWidth - ClassicSprites.ThumbWidth), 0, 1) * 2 - 1;
                // It catches in the middle, so it's easy to put back.
                Balance = Math.Abs(balance) < 0.12 ? 0 : balance;
                BalanceChanged?.Invoke(Balance);
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
        if (released == Control.Seek)
        {
            SeekRequested?.Invoke(_dragValue);
            return;
        }

        if (!inside || CommandFor(released) is not { } command)
        {
            return;
        }

        Command?.Invoke(command);
    }

    private static ClassicCommand? CommandFor(Control control) => control switch
    {
        Control.Options => ClassicCommand.Menu,
        Control.Minimize => ClassicCommand.Minimize,
        Control.Shade => ClassicCommand.Shade,
        Control.Close => ClassicCommand.Close,
        Control.ClutterO => ClassicCommand.Menu,
        Control.ClutterA => ClassicCommand.AlwaysOnTop,
        Control.ClutterI => ClassicCommand.TrackInfo,
        Control.ClutterD => ClassicCommand.DoubleSize,
        Control.ClutterV => ClassicCommand.Visuals,
        Control.Previous => ClassicCommand.Previous,
        Control.Play => ClassicCommand.Play,
        Control.Pause => ClassicCommand.Pause,
        Control.Stop => ClassicCommand.Stop,
        Control.Next => ClassicCommand.Next,
        Control.Eject => ClassicCommand.Open,
        Control.Shuffle => ClassicCommand.Shuffle,
        Control.Repeat => ClassicCommand.Repeat,
        Control.Equalizer => ClassicCommand.Equalizer,
        Control.Playlist => ClassicCommand.Playlist,
        Control.Time => ClassicCommand.ToggleTimeMode,
        Control.Vis => ClassicCommand.CycleVisualizer,
        _ => null,
    };

    private Control HitTest(double x, double y)
    {
        if (Inside(x, y, ClassicSprites.OptionsButton.X, ClassicSprites.OptionsButton.Y, 9, 9))
        {
            return Control.Options;
        }

        if (Inside(x, y, ClassicSprites.MinimizeButton.X, ClassicSprites.MinimizeButton.Y, 9, 9))
        {
            return Control.Minimize;
        }

        if (Inside(x, y, ClassicSprites.ShadeButton.X, ClassicSprites.ShadeButton.Y, 9, 9))
        {
            return Control.Shade;
        }

        if (Inside(x, y, ClassicSprites.CloseButton.X, ClassicSprites.CloseButton.Y, 9, 9))
        {
            return Control.Close;
        }

        if (IsShaded)
        {
            return Control.None;
        }

        for (var i = 0; i < ClassicSprites.ClutterButtons.Length; i++)
        {
            var (top, height, _) = ClassicSprites.ClutterButtons[i];
            if (Inside(x, y, ClassicSprites.ClutterBarX, ClassicSprites.ClutterBarY + top, 8, height))
            {
                return Control.ClutterO + i;
            }
        }

        for (var i = 0; i < ClassicSprites.Transport.Length; i++)
        {
            var (up, _, left, top) = ClassicSprites.Transport[i];
            if (Inside(x, y, left, top, up.Width, up.Height))
            {
                return Control.Previous + i;
            }
        }

        (ClassicSprites.Toggle Toggle, Control Control)[] toggles =
        {
            (ClassicSprites.Shuffle, Control.Shuffle), (ClassicSprites.Repeat, Control.Repeat),
            (ClassicSprites.Equalizer, Control.Equalizer), (ClassicSprites.Playlist, Control.Playlist),
        };
        foreach (var (toggle, control) in toggles)
        {
            if (Inside(x, y, toggle.X, toggle.Y, toggle.Off.Width, toggle.Off.Height))
            {
                return control;
            }
        }

        if (CanSeek && Inside(x, y, ClassicSprites.SeekX, ClassicSprites.SeekY, ClassicSprites.SeekBar.Width, ClassicSprites.SeekBar.Height))
        {
            return Control.Seek;
        }

        if (Inside(x, y, ClassicSprites.VolumeX, ClassicSprites.VolumeY, ClassicSprites.VolumeWidth, ClassicSprites.SliderHeight))
        {
            return Control.Volume;
        }

        if (Inside(x, y, ClassicSprites.BalanceX, ClassicSprites.BalanceY, ClassicSprites.BalanceWidth, ClassicSprites.SliderHeight))
        {
            return Control.Balance;
        }

        if (Inside(x, y, 36, 26, 64, 13))
        {
            return Control.Time;
        }

        if (Inside(x, y, ClassicSprites.VisX, ClassicSprites.VisY, ClassicSprites.VisWidth, ClassicSprites.VisHeight))
        {
            return Control.Vis;
        }

        return Control.None;
    }

    private static bool Inside(double x, double y, int left, int top, int width, int height) =>
        x >= left && x < left + width && y >= top && y < top + height;
}
