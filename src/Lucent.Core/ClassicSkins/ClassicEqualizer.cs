namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>What a click in the classic equalizer asks the app to do.</summary>
public enum ClassicEqualizerCommand
{
    ToggleOn,
    Presets,
    Close,
}

/// <summary>
/// The equalizer drawn by a classic Winamp skin (eqmain.bmp): Winamp 2's equalizer window, 275 × 116 skin
/// pixels, with a preamp and ten band sliders, ON, PRESETS and the little response graph. Like
/// <see cref="ClassicPlayer"/>, both apps hand it the settings, show <see cref="Pixels"/>, and pass the
/// mouse in, in skin pixels; it does the drawing and the sliders.
/// </summary>
public sealed class ClassicEqualizer : IClassicSurface
{
    /// <summary>Ten bands, as on Winamp's: <see cref="Audio.EqualizerBands.Classic"/>'s.</summary>
    public const int BandCount = 10;

    private const double MaxDb = Audio.Equalizer.MaxGainDb;

    // ---- Where everything is (eqmain.bmp), as Winamp 2 laid it out ----

    private static readonly Sprite Background = new("eqmain", 0, 0, 275, 116);
    private static readonly Sprite TitleBar = new("eqmain", 0, 149, 275, 14);
    private static readonly Sprite TitleBarActive = new("eqmain", 0, 134, 275, 14);
    private static readonly Sprite Close = new("eqmain", 0, 116, 9, 9);
    private static readonly Sprite CloseDown = new("eqmain", 0, 125, 9, 9);
    private const int CloseX = 264;
    private const int CloseY = 3;

    // ON's and AUTO's looks: off, off pressed, on, on pressed.
    private static readonly Sprite[] On =
    {
        new("eqmain", 10, 119, 26, 12), new("eqmain", 128, 119, 26, 12),
        new("eqmain", 69, 119, 26, 12), new("eqmain", 187, 119, 26, 12),
    };

    private static readonly Sprite Auto = new("eqmain", 36, 119, 32, 12);
    private const int OnX = 14;
    private const int AutoX = 40;
    private const int ButtonY = 18;

    private static readonly Sprite Presets = new("eqmain", 224, 164, 44, 12);
    private static readonly Sprite PresetsDown = new("eqmain", 224, 176, 44, 12);
    private const int PresetsX = 217;

    private static readonly Sprite Graph = new("eqmain", 0, 294, 113, 19);
    private static readonly Sprite GraphColours = new("eqmain", 115, 294, 1, 19);
    private static readonly Sprite PreampLine = new("eqmain", 0, 314, 113, 1);
    private const int GraphX = 86;
    private const int GraphY = 17;

    private static readonly Sprite Thumb = new("eqmain", 0, 164, 11, 11);
    private static readonly Sprite ThumbDown = new("eqmain", 0, 176, 11, 11);
    private const int PreampX = 21;
    private const int FirstBandX = 78;
    private const int BandSpacing = 18;
    private const int SliderY = 38;
    private const int SliderWidth = 14;
    private const int SliderHeight = 63;
    private const int ThumbTravel = 51;

    /// <summary>One of a slider's 28 backgrounds, 0 at the bottom (−12 dB) to 27 at the top (+12 dB).</summary>
    private static Sprite SliderFrame(int frame) => new("eqmain", 13 + frame % 14 * 15, 164 + frame / 14 * 65, SliderWidth, SliderHeight);

    private readonly SkinCanvas _canvas = new(ClassicSprites.WindowWidth, ClassicSprites.WindowHeight);

    // -1 is the preamp; 0 to 9 the bands.
    private const int PreampSlider = -1;
    private const int NoSlider = -2;
    private int _dragging = NoSlider;
    private Button _pressed = Button.None;
    private bool _pressedInside;

    public ClassicEqualizer(ClassicSkin skin) => Skin = skin;

    public ClassicSkin Skin { get; set; }

    public uint[] Pixels => _canvas.Pixels;

    public int Width => _canvas.Width;

    public int Height => _canvas.Height;

    // ---- What the app tells it ----

    public bool Enabled { get; set; }

    public double PreampDb { get; set; }

    /// <summary>The ten bands' gains in dB, lowest first.</summary>
    public double[] Gains { get; } = new double[BandCount];

    /// <summary>Whether the window has focus, which lights up the title bar.</summary>
    public bool IsActive { get; set; } = true;

    // ---- What it tells the app ----

    public event Action<ClassicEqualizerCommand>? Command;

    /// <summary>A band slider moved: its number (0 to 9) and its new gain. <see cref="Gains"/> has already changed.</summary>
    public event Action<int, double>? BandChanged;

    /// <summary>The preamp slider moved. <see cref="PreampDb"/> has already changed.</summary>
    public event Action<double>? PreampChanged;

    /// <summary>A still of a skin's equalizer, on and flat: for the skin picker and screenshots.</summary>
    public static uint[] Preview(ClassicSkin skin)
    {
        var equalizer = new ClassicEqualizer(skin) { Enabled = true };
        equalizer.Render();
        return equalizer.Pixels;
    }

    // ---- Drawing ----

    public void Render()
    {
        var skin = Skin;
        _canvas.Draw(skin, Background, 0, 0);
        _canvas.Draw(skin, IsActive ? TitleBarActive : TitleBar, 0, 0);
        _canvas.Draw(skin, IsDown(Button.Close) ? CloseDown : Close, CloseX, CloseY);
        _canvas.Draw(skin, On[(Enabled ? 2 : 0) + (IsDown(Button.On) ? 1 : 0)], OnX, ButtonY);
        // Winamp's AUTO loads a preset for each song; Lucent hasn't that, so it shows as off.
        _canvas.Draw(skin, Auto, AutoX, ButtonY);
        _canvas.Draw(skin, IsDown(Button.Presets) ? PresetsDown : Presets, PresetsX, ButtonY);

        DrawSlider(PreampX, PreampDb, _dragging == PreampSlider);
        for (var band = 0; band < BandCount; band++)
        {
            DrawSlider(BandX(band), Gains[band], _dragging == band);
        }

        DrawGraph();
    }

    private void DrawSlider(int left, double db, bool held)
    {
        var level = Level(db);
        _canvas.Draw(Skin, SliderFrame((int)Math.Round(level * 27)), left, SliderY);
        _canvas.Draw(Skin, held ? ThumbDown : Thumb, left + 1, SliderY + (int)Math.Round((1 - level) * ThumbTravel));
    }

    /// <summary>
    /// The response graph: a smooth line through the ten sliders, coloured by height from the skin's strip
    /// of line colours, over a line for the preamp, as Winamp draws it.
    /// </summary>
    private void DrawGraph()
    {
        var skin = Skin;
        _canvas.Draw(skin, Graph, GraphX, GraphY);
        var bottom = Graph.Height - 1;
        _canvas.Draw(skin, PreampLine, GraphX, GraphY + (int)Math.Round(Level(PreampDb) * bottom));

        var xs = new double[BandCount];
        var ys = new double[BandCount];
        for (var band = 0; band < BandCount; band++)
        {
            xs[band] = band * 12;
            ys[band] = (1 - Level(Gains[band])) * bottom;
        }

        var curve = Spline(xs, ys);
        var last = (int)Math.Round(ys[0]);
        for (var x = 0; x < curve.Length; x++)
        {
            var y = Math.Clamp((int)Math.Round(curve[x]), 0, bottom);
            for (var row = Math.Min(y, last); row <= Math.Max(y, last); row++)
            {
                if (SkinCanvas.PixelOf(skin, GraphColours, 0, row) is { } colour)
                {
                    _canvas.Plot(GraphX + 2 + x, GraphY + row, colour);
                }
            }

            last = y;
        }
    }

    /// <summary>A natural cubic spline through the points, sampled at every whole x from the first to the last.</summary>
    private static double[] Spline(double[] xs, double[] ys)
    {
        var n = xs.Length;
        var second = new double[n];
        var work = new double[n];
        for (var i = 1; i < n - 1; i++)
        {
            var sigma = (xs[i] - xs[i - 1]) / (xs[i + 1] - xs[i - 1]);
            var p = sigma * second[i - 1] + 2;
            second[i] = (sigma - 1) / p;
            work[i] = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]) - (ys[i] - ys[i - 1]) / (xs[i] - xs[i - 1]);
            work[i] = (6 * work[i] / (xs[i + 1] - xs[i - 1]) - sigma * work[i - 1]) / p;
        }

        second[n - 1] = 0;
        for (var i = n - 2; i >= 0; i--)
        {
            second[i] = second[i] * second[i + 1] + work[i];
        }

        var samples = new double[(int)xs[n - 1] + 1];
        var segment = 0;
        for (var x = 0; x < samples.Length; x++)
        {
            while (segment < n - 2 && x > xs[segment + 1])
            {
                segment++;
            }

            var h = xs[segment + 1] - xs[segment];
            var a = (xs[segment + 1] - x) / h;
            var b = (x - xs[segment]) / h;
            samples[x] = a * ys[segment] + b * ys[segment + 1]
                + ((a * a * a - a) * second[segment] + (b * b * b - b) * second[segment + 1]) * h * h / 6;
        }

        return samples;
    }

    /// <summary>A gain as 0 (−12 dB, the slider's bottom) to 1 (+12 dB, its top).</summary>
    private static double Level(double db) => Math.Clamp((db + MaxDb) / (2 * MaxDb), 0, 1);

    private static int BandX(int band) => FirstBandX + band * BandSpacing;

    // ---- The mouse ----

    private enum Button
    {
        None,
        Close,
        On,
        Presets,
    }

    private bool IsDown(Button button) => _pressed == button && _pressedInside;

    /// <summary>A mouse button went down at (x, y) in skin pixels.</summary>
    /// <param name="clicks">2 for a double click, which puts a slider back to 0 dB.</param>
    /// <returns>True when it's not on a control, so the app should start moving the window.</returns>
    public bool PointerDown(double x, double y, int clicks)
    {
        if (SliderAt(x, y) is { } slider)
        {
            _dragging = slider;
            if (clicks == 2)
            {
                SetSlider(slider, 0);
                return false;
            }

            PointerMove(x, y);
            return false;
        }

        _pressed = ButtonAt(x, y);
        _pressedInside = true;
        return _pressed == Button.None;
    }

    public void PointerMove(double x, double y)
    {
        if (_dragging != NoSlider)
        {
            // The thumb's middle follows the pointer.
            var level = 1 - (y - SliderY - Thumb.Height / 2.0) / ThumbTravel;
            var db = Math.Round((Math.Clamp(level, 0, 1) * 2 - 1) * MaxDb * 2) / 2;
            SetSlider(_dragging, db);
            return;
        }

        if (_pressed != Button.None)
        {
            _pressedInside = ButtonAt(x, y) == _pressed;
        }
    }

    public void PointerUp(double x, double y)
    {
        if (_dragging != NoSlider)
        {
            _dragging = NoSlider;
            return;
        }

        var released = _pressed;
        PointerMove(x, y);
        var inside = _pressedInside;
        _pressed = Button.None;
        if (!inside)
        {
            return;
        }

        switch (released)
        {
            case Button.Close:
                Command?.Invoke(ClassicEqualizerCommand.Close);
                break;
            case Button.On:
                Command?.Invoke(ClassicEqualizerCommand.ToggleOn);
                break;
            case Button.Presets:
                Command?.Invoke(ClassicEqualizerCommand.Presets);
                break;
        }
    }

    /// <summary>Where the PRESETS button is, in skin pixels, for the app to open its menu beside.</summary>
    public static (int X, int Y, int Width, int Height) PresetsButton => (PresetsX, ButtonY, Presets.Width, Presets.Height);

    private void SetSlider(int slider, double db)
    {
        if (slider == PreampSlider)
        {
            if (PreampDb != db)
            {
                PreampDb = db;
                PreampChanged?.Invoke(db);
            }

            return;
        }

        if (Gains[slider] != db)
        {
            Gains[slider] = db;
            BandChanged?.Invoke(slider, db);
        }
    }

    private static int? SliderAt(double x, double y)
    {
        if (y < SliderY || y >= SliderY + SliderHeight)
        {
            return null;
        }

        if (x >= PreampX && x < PreampX + SliderWidth)
        {
            return PreampSlider;
        }

        for (var band = 0; band < BandCount; band++)
        {
            if (x >= BandX(band) && x < BandX(band) + SliderWidth)
            {
                return band;
            }
        }

        return null;
    }

    private static Button ButtonAt(double x, double y)
    {
        if (Inside(x, y, CloseX, CloseY, Close.Width, Close.Height))
        {
            return Button.Close;
        }

        if (Inside(x, y, OnX, ButtonY, On[0].Width, On[0].Height))
        {
            return Button.On;
        }

        if (Inside(x, y, PresetsX, ButtonY, Presets.Width, Presets.Height))
        {
            return Button.Presets;
        }

        return Button.None;
    }

    private static bool Inside(double x, double y, int left, int top, int width, int height) =>
        x >= left && x < left + width && y >= top && y < top + height;
}
