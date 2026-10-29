using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Dsp;
using Lucent.Audio;
using Lucent.Settings;

namespace Lucent.Windows;

/// <summary>
/// The analyzer's picture, like the Vision plugin's: a heatmap of the music scrolling right to left,
/// low notes at the bottom and high ones at the top, coloured by how loud each frequency was at each
/// moment, with the waveform scrolling underneath in step. Beside them sit the spectrum right now, on
/// the same frequency scale as the heatmap, and a stereo spread scope (see Spectrogram.Panels.cs), with
/// loudness readouts along the bottom (see Spectrogram.Loudness.cs). Each of the five can be shown or
/// hidden; the rest fill the space. It reads the music the same way
/// as the equalizer's live spectrum, but keeps the last few seconds so you can see them go by.
/// </summary>
public sealed partial class Spectrogram : FrameworkElement
{
    private const int FftExponent = 12;
    private const int FftSize = 1 << FftExponent;

    // The frequencies shown, bottom to top. Below 30 Hz there's little but rumble.
    private const double LowestHz = 30;
    private const double HighestHz = 20000;

    // The loudness range shown, darkest to brightest, after the tilt below.
    private const float FloorDb = -92;
    private const float CeilingDb = -14;

    // Music has far more energy in the bass, so each octave up is lifted (as studio analyzers do)
    // to keep the treble from being lost in the dark.
    private const float TiltDbPerOctave = 3f;

    // More rows than this only makes the picture blurrier, not finer: the analysis can't resolve more.
    private const int MaxRows = 400;

    private const double WaveformHeight = 56;
    // The strip down the left edge that holds the frequency scale, clear of the heatmap so it stays readable.
    private const double AxisWidth = 42;
    private const double LabelSize = 11.5;
    private const double LabelSpacing = 16;
    private const double Gap = 6;
    // The spectrum and stereo panels down the right: this share of the width, within limits.
    private const double SideShare = 0.3;
    private const double SideMin = 150;
    private const double SideMax = 280;
    // With the stereo scope shown the bottom row grows to give it room.
    private const double StereoRowShare = 0.36;
    private const double StereoRowMin = 110;
    private const double StereoRowMax = 200;
    private const double MarkSeconds = 5;

    public static readonly DependencyProperty ThemeLowBrushProperty = PaletteBrush(nameof(ThemeLowBrush));
    public static readonly DependencyProperty ThemeMidBrushProperty = PaletteBrush(nameof(ThemeMidBrush));
    public static readonly DependencyProperty ThemeHighBrushProperty = PaletteBrush(nameof(ThemeHighBrush));
    public static readonly DependencyProperty ThemePeakBrushProperty = PaletteBrush(nameof(ThemePeakBrush));

    public static readonly DependencyProperty WaveBrushProperty = DependencyProperty.Register(
        nameof(WaveBrush), typeof(Brush), typeof(Spectrogram),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(Spectrogram),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AxisBrushProperty = DependencyProperty.Register(
        nameof(AxisBrush), typeof(Brush), typeof(Spectrogram),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(Spectrogram),
        new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly AudioCapture _capture;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly uint[] _palette = new uint[256];

    // What's been heard: one byte of loudness per row per column (row-major), the waveform's lowest
    // and highest point in each column, and which columns get a time mark. The columns are a ring:
    // each new one overwrites the oldest at _head, so nothing has to move as the picture scrolls,
    // and only the new columns are sent to the screen.
    private byte[] _levels = Array.Empty<byte>();
    private float[] _waveLow = Array.Empty<float>();
    private float[] _waveHigh = Array.Empty<float>();
    private bool[] _marks = Array.Empty<bool>();
    private uint[] _pixels = Array.Empty<uint>();
    private WriteableBitmap? _bitmap;
    private int _columns;
    private int _rows;
    private int _head;
    // Paints the heatmap ring, tiled so the wrap-around point never shows.
    private ImageBrush? _heatBrush;
    // The loudness of each row in the newest analysis, 0 to 1, for the spectrum panel.
    private float[] _latest = Array.Empty<float>();

    // Where each part goes, worked out again when the size or the choice of parts changes.
    private AnalyzerModules _modules = AnalyzerModules.All;
    private Rect _axis = Rect.Empty;
    private Rect _heatmap = Rect.Empty;
    private Rect _spectrum = Rect.Empty;
    private Rect _wave = Rect.Empty;
    private Rect _stereo = Rect.Empty;
    private double _topHeight;

    // Which part of the analysis each row reads, worked out again when the sample rate changes.
    private int[] _rowFirst = Array.Empty<int>();
    private int[] _rowLast = Array.Empty<int>();
    private double[] _rowBin = Array.Empty<double>();
    private float[] _rowTilt = Array.Empty<float>();
    private int _mappedRate;

    private SpectrogramPalette _paletteChoice = SpectrogramPalette.Classic;
    private double _columnsPerSecond = 50;
    private double _carry;
    // Whole columns scrolled so far, kept only as far as the dotted lines' pattern repeats, so their
    // dots glide along with the heatmap and waveform instead of sitting still on top of them.
    private int _scrolled;
    private float _sensitivityDb;
    private double _sinceMark;
    private TimeSpan _lastFrame;
    private bool _running;

    public Spectrogram(AudioCapture capture)
    {
        _capture = capture;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.Linear);
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        }

        // The window's frame and the theme palette follow the skin and theme as they change.
        SetResourceReference(ThemeLowBrushProperty, "BackgroundBrush");
        SetResourceReference(ThemeMidBrushProperty, "AccentSoftBrush");
        SetResourceReference(ThemeHighBrushProperty, "AccentBrush");
        SetResourceReference(ThemePeakBrushProperty, "TextBrush");
        SetResourceReference(WaveBrushProperty, "AccentGradientBrush");
        SetResourceReference(GridBrushProperty, "FaintTextBrush");
        SetResourceReference(LabelBrushProperty, "TextBrush");
        SetResourceReference(AxisBrushProperty, "SurfaceBrush");
        BuildPalette();

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Start();
            }
            else
            {
                Stop();
            }
        };
    }

    public Brush? ThemeLowBrush
    {
        get => (Brush?)GetValue(ThemeLowBrushProperty);
        set => SetValue(ThemeLowBrushProperty, value);
    }

    public Brush? ThemeMidBrush
    {
        get => (Brush?)GetValue(ThemeMidBrushProperty);
        set => SetValue(ThemeMidBrushProperty, value);
    }

    public Brush? ThemeHighBrush
    {
        get => (Brush?)GetValue(ThemeHighBrushProperty);
        set => SetValue(ThemeHighBrushProperty, value);
    }

    public Brush? ThemePeakBrush
    {
        get => (Brush?)GetValue(ThemePeakBrushProperty);
        set => SetValue(ThemePeakBrushProperty, value);
    }

    public Brush WaveBrush
    {
        get => (Brush)GetValue(WaveBrushProperty);
        set => SetValue(WaveBrushProperty, value);
    }

    public Brush LabelBrush
    {
        get => (Brush)GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public Brush AxisBrush
    {
        get => (Brush)GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    /// <summary>The colours loudness is painted in. Changing it repaints what's already showing, too.</summary>
    public SpectrogramPalette Palette
    {
        get => _paletteChoice;
        set
        {
            _paletteChoice = value;
            BuildPalette();
        }
    }

    /// <summary>Which parts are shown. At least one always is.</summary>
    public AnalyzerModules Modules
    {
        get => _modules;
        set
        {
            _modules = (value & AnalyzerModules.All) == 0 ? AnalyzerModules.Heatmap : value & AnalyzerModules.All;
            Resize();
            InvalidateVisual();
        }
    }

    /// <summary>How fast the picture scrolls, in screen pixels a second.</summary>
    public double ColumnsPerSecond
    {
        get => _columnsPerSecond;
        set => _columnsPerSecond = Math.Clamp(value, 5, 400);
    }

    /// <summary>
    /// How much brighter the heatmap and spectrum paint the music, in dB: higher shows quieter
    /// sounds, lower keeps loud music from filling everything with the top colours.
    /// </summary>
    public double SensitivityDb
    {
        get => _sensitivityDb;
        set => _sensitivityDb = (float)Math.Clamp(value, MinSensitivityDb, MaxSensitivityDb);
    }

    public const double MinSensitivityDb = -12;
    public const double MaxSensitivityDb = 24;

    /// <summary>The frequency at a height in the heatmap or spectrum, or null outside them.</summary>
    public double? FrequencyAt(double y)
    {
        var height = _topHeight;
        if (height <= 0 || y < 0 || y > height)
        {
            return null;
        }

        return LowestHz * Math.Pow(TopHz / LowestHz, 1 - y / height);
    }

    // The top of the picture: 20 kHz, or as high as the music goes at a low sample rate.
    private double TopHz => Math.Min(HighestHz, _capture.SampleRate / 2.0);

    private bool Shows(AnalyzerModules module) => (_modules & module) != 0;

    // How far down a frequency sits in the heatmap and spectrum.
    private double HeightOf(double hertz) => _topHeight * (1 - Math.Log(hertz / LowestHz) / Math.Log(TopHz / LowestHz));

    /// <summary>
    /// Lays the parts out in two rows: the heatmap and spectrum on top with the frequency scale down
    /// the left, the waveform and stereo scope underneath. A hidden part's neighbour takes its place.
    /// </summary>
    private void Arrange()
    {
        var width = ActualWidth;
        var height = ArrangeLoudness(ActualWidth, ActualHeight);
        var top = Shows(AnalyzerModules.Heatmap) || Shows(AnalyzerModules.Spectrum);
        var bottom = Shows(AnalyzerModules.Waveform) || Shows(AnalyzerModules.Stereo);
        var bottomHeight = !bottom ? 0
            : !top ? height
            : Shows(AnalyzerModules.Stereo) ? Math.Clamp(height * StereoRowShare, StereoRowMin, StereoRowMax)
            : WaveformHeight;
        _topHeight = !top ? 0 : bottom ? Math.Max(0, height - bottomHeight - Gap) : height;
        var side = Math.Clamp(width * SideShare, SideMin, SideMax);

        // Two parts side by side split the row; one alone fills it.
        static (Rect Left, Rect Right) Row(bool left, bool right, double x, double y, double width, double height, double side)
        {
            var full = width - x;
            if (left && right)
            {
                return (new Rect(x, y, Math.Max(0, full - side - Gap), height), new Rect(width - side, y, side, height));
            }

            var whole = new Rect(x, y, Math.Max(0, full), height);
            return (left ? whole : Rect.Empty, right ? whole : Rect.Empty);
        }

        _axis = top ? new Rect(0, 0, AxisWidth, height) : Rect.Empty;
        var left = top ? AxisWidth : 0;
        (_heatmap, _spectrum) = top
            ? Row(Shows(AnalyzerModules.Heatmap), Shows(AnalyzerModules.Spectrum), left, 0, width, _topHeight, side)
            : (Rect.Empty, Rect.Empty);
        (_wave, _stereo) = bottom
            ? Row(Shows(AnalyzerModules.Waveform), Shows(AnalyzerModules.Stereo), left, height - bottomHeight, width, bottomHeight, side)
            : (Rect.Empty, Rect.Empty);
    }

    // ---- Running ----

    private void Start()
    {
        if (!_running && IsLoaded && IsVisible)
        {
            _running = true;
            _lastFrame = TimeSpan.Zero;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void Stop()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Resize();
    }

    /// <summary>One column per pixel across, one row per pixel down (up to a point), keeping what fits.</summary>
    private void Resize()
    {
        Arrange();
        static int Pixels(Rect rect) => rect.IsEmpty ? 0 : (int)Math.Ceiling(rect.Width);
        // Two spare columns: the picture scrolls by fractions of a pixel, so a sliver more than the
        // width can show.
        var columns = Math.Max(Pixels(_heatmap), Pixels(_wave)) + 2;
        var rows = Math.Clamp((int)Math.Ceiling(_topHeight), 1, MaxRows);
        if (columns == _columns && rows == _rows)
        {
            ResizePanels();
            return;
        }

        var levels = new byte[columns * rows];
        var waveLow = new float[columns];
        var waveHigh = new float[columns];
        var marks = new bool[columns];
        // Wider or narrower, the newest moments are kept (laid out oldest first from column 0). A new
        // height can't keep the old rows, so the heatmap starts again (the waveform carries on).
        var kept = Math.Min(columns, _columns);
        for (var i = 0; i < kept; i++)
        {
            var from = Ring(_head - kept + i);
            if (rows == _rows)
            {
                for (var row = 0; row < rows; row++)
                {
                    levels[row * columns + i] = _levels[row * _columns + from];
                }
            }

            waveLow[i] = _waveLow[from];
            waveHigh[i] = _waveHigh[from];
            marks[i] = _marks[from];
        }

        _levels = levels;
        _waveLow = waveLow;
        _waveHigh = waveHigh;
        _marks = marks;
        _columns = columns;
        _rows = rows;
        _head = kept % columns;
        _latest = new float[rows];
        _pixels = new uint[columns * rows];
        _bitmap = new WriteableBitmap(columns, rows, 96, 96, PixelFormats.Bgr32, null);
        _heatBrush = new ImageBrush(_bitmap)
        {
            TileMode = TileMode.Tile,
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.Absolute,
        };
        RenderOptions.SetBitmapScalingMode(_heatBrush, BitmapScalingMode.Linear);
        _mappedRate = 0;
        ResizePanels();
        Paint();
    }

    // A column's place in the ring, counting round from any whole number.
    private int Ring(int column) => _columns == 0 ? 0 : ((column % _columns) + _columns) % _columns;

    private void OnFrame(object? sender, EventArgs e)
    {
        // Timed by when WPF says the frame will be shown, not when this runs, so the scroll moves the
        // same distance every frame. WPF can call this more than once a frame; only the first counts.
        var frameTime = e is RenderingEventArgs rendering ? rendering.RenderingTime : TimeSpan.FromTicks(DateTime.UtcNow.Ticks);
        if (frameTime == _lastFrame)
        {
            return;
        }

        var deltaTime = _lastFrame == TimeSpan.Zero ? 0 : Math.Clamp((frameTime - _lastFrame).TotalSeconds, 0, 0.1);
        _lastFrame = frameTime;

        // Paused or stopped, the picture holds still so you can look at it.
        if (_capture.IsSilent || _bitmap is null)
        {
            return;
        }

        var sampleRate = _capture.SampleRate;
        var delay = _capture.DelaySamples;
        UpdatePanels((float)deltaTime, delay);
        UpdateLoudness();

        // The picture glides left continuously, by however far this frame's time takes it, and a new
        // column is added each time it has moved a whole pixel.
        _carry += deltaTime * _columnsPerSecond;
        var count = (int)_carry;
        _carry -= count;
        if (count > 0)
        {
            count = Math.Min(count, _columns);
            if (_mappedRate != sampleRate)
            {
                MapRows(sampleRate);
            }

            // Each new column is the music at its own moment: the last one now (less the part of a
            // column the picture has already glided), the ones before it a column earlier each.
            var samplesPerColumn = Math.Max(1, (int)(sampleRate / _columnsPerSecond));
            var first = _head;
            for (var i = 0; i < count; i++)
            {
                var column = _head;
                _capture.ReadLatest(_samples, delay + (int)((count - 1 - i + _carry) * samplesPerColumn));
                Analyze(column);
                ReadWave(column, Math.Min(samplesPerColumn, FftSize));
                _sinceMark += 1 / _columnsPerSecond;
                _marks[column] = _sinceMark >= MarkSeconds;
                if (_marks[column])
                {
                    _sinceMark -= MarkSeconds;
                }

                _head = (_head + 1) % _columns;
            }

            _scrolled = (_scrolled + count) % DotRepeat;

            PaintColumns(first, count);
        }

        InvalidateVisual();
    }

    // ---- Analysis ----

    /// <summary>Works out which frequencies each row covers, spaced evenly in octaves like a piano.</summary>
    private void MapRows(int sampleRate)
    {
        _mappedRate = sampleRate;
        _rowFirst = new int[_rows];
        _rowLast = new int[_rows];
        _rowBin = new double[_rows];
        _rowTilt = new float[_rows];
        var binHz = (double)sampleRate / FftSize;
        var lastBin = FftSize / 2 - 1;
        var top = TopHz;
        double Frequency(double fromBottom) => LowestHz * Math.Pow(top / LowestHz, fromBottom / _rows);

        for (var row = 0; row < _rows; row++)
        {
            // Row 0 is the top of the picture.
            var fromBottom = _rows - 1 - row;
            var low = Frequency(fromBottom);
            var high = Frequency(fromBottom + 1);
            var centre = Frequency(fromBottom + 0.5);
            _rowFirst[row] = Math.Clamp((int)Math.Floor(low / binHz), 1, lastBin);
            _rowLast[row] = Math.Clamp((int)Math.Ceiling(high / binHz), _rowFirst[row], lastBin);
            _rowBin[row] = Math.Clamp(centre / binHz, 1, lastBin - 1);
            _rowTilt[row] = TiltDbPerOctave * (float)Math.Log2(centre / 1000);
        }
    }

    private void Analyze(int column)
    {
        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = _samples[i] * _window[i];
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, _fft);

        for (var row = 0; row < _rows; row++)
        {
            // Low down a row is narrower than one bin, so it reads between the two nearest;
            // higher up it takes the loudest bin it covers, so a single note isn't lost.
            float power;
            var first = _rowFirst[row];
            var last = _rowLast[row];
            if (last - first <= 1)
            {
                var below = (int)_rowBin[row];
                var blend = (float)(_rowBin[row] - below);
                power = Power(below) * (1 - blend) + Power(below + 1) * blend;
            }
            else
            {
                power = 0;
                for (var bin = first; bin <= last; bin++)
                {
                    power = MathF.Max(power, Power(bin));
                }
            }

            var decibels = 10f * MathF.Log10(power + 1e-12f) + _rowTilt[row] + _sensitivityDb;
            var level = Math.Clamp((decibels - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
            _levels[row * _columns + column] = (byte)(level * 255 + 0.5f);
            _latest[row] = level;
        }
    }

    private float Power(int bin) => _fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y;

    /// <summary>The lowest and highest point of the newest stretch of audio, for the waveform underneath.</summary>
    private void ReadWave(int column, int length)
    {
        var low = 0f;
        var high = 0f;
        for (var i = FftSize - length; i < FftSize; i++)
        {
            low = MathF.Min(low, _samples[i]);
            high = MathF.Max(high, _samples[i]);
        }

        _waveLow[column] = Math.Clamp(low, -1f, 1f);
        _waveHigh[column] = Math.Clamp(high, -1f, 1f);
    }

    // ---- Drawing ----

    private void Paint()
    {
        PaintColumns(0, _columns);
        InvalidateVisual();
    }

    /// <summary>Colours <paramref name="count"/> columns of the ring from <paramref name="first"/> and sends just those to the screen.</summary>
    private void PaintColumns(int first, int count)
    {
        if (_bitmap is null)
        {
            return;
        }

        // At most two runs: up to the end of the ring, then on from its start.
        while (count > 0)
        {
            var run = Math.Min(count, _columns - first);
            for (var row = 0; row < _rows; row++)
            {
                var at = row * _columns + first;
                for (var i = 0; i < run; i++)
                {
                    _pixels[at + i] = _palette[_levels[at + i]];
                }
            }

            _bitmap.WritePixels(new Int32Rect(first, 0, run, _rows), _pixels, _columns * 4, first, 0);
            count -= run;
            first = 0;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_bitmap is null || ActualWidth < 2 || ActualHeight < 2)
        {
            return;
        }

        var grid = new Pen(GridBrush, 1) { DashStyle = new DashStyle(Dots, 0), DashCap = PenLineCap.Flat };
        // The same dots, shifted along by however far the picture has scrolled, for lines across it.
        var gliding = new Pen(GridBrush, 1) { DashStyle = new DashStyle(Dots, _scrolled + _carry), DashCap = PenLineCap.Flat };
        var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(TryFindResource("UiFont") as FontFamily ?? new FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        FormattedText Text(string text, double size, Brush brush) => new(text, CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, typeface, size, brush, pixelsPerDip);

        // The history runs right to left from each part's right edge; a part narrower than the
        // history shows only its newest end. Everything is shifted by the same fraction of a pixel.
        if (Usable(_heatmap))
        {
            dc.PushClip(new RectangleGeometry(_heatmap));
            if (_heatBrush is not null)
            {
                // The newest column sits at the right edge, nudged left by however far it has glided.
                var newest = Ring(_head - 1);
                _heatBrush.Viewport = new Rect(_heatmap.Right - _carry - newest, _heatmap.Top, _columns, _heatmap.Height);
                dc.DrawRectangle(_heatBrush, null, _heatmap);
            }

            DrawMarks(dc, grid, _heatmap);
            dc.Pop();
        }

        if (!_axis.IsEmpty)
        {
            DrawScale(dc, grid, gliding, Text);
        }

        if (Usable(_spectrum))
        {
            DrawSpectrum(dc, grid);
        }

        if (Usable(_wave))
        {
            dc.PushClip(new RectangleGeometry(_wave));
            DrawMarks(dc, grid, _wave);
            DrawWave(dc, gliding);
            dc.Pop();
        }

        if (Usable(_stereo))
        {
            DrawStereo(dc, grid, Text);
        }

        if (Usable(_loudness))
        {
            DrawLoudness(dc, grid, Text);
        }
    }

    // The dotted lines' pattern: a dot every fourth pixel. Spaced that far apart the dots are seen to
    // glide with the picture; a dot every other pixel would only shimmer as it scrolls.
    private const int DotRepeat = 4;
    private static readonly double[] Dots = { 1.0, DotRepeat - 1.0 };

    private static bool Usable(Rect rect) => !rect.IsEmpty && rect.Width >= 2 && rect.Height >= 2;

    // Faint lines every few seconds, moving with the music.
    private void DrawMarks(DrawingContext dc, Pen grid, Rect area)
    {
        var visible = Math.Min(_columns, (int)Math.Ceiling(area.Width) + 2);
        for (var age = 0; age < visible; age++)
        {
            if (_marks[Ring(_head - 1 - age)])
            {
                var x = area.Right - _carry - age + 0.5;
                dc.DrawLine(grid, new Point(x, area.Top), new Point(x, area.Bottom));
            }
        }
    }

    /// <summary>
    /// The frequency scale, in its own strip so the numbers never sit on top of the colours:
    /// larger, bolder type in the theme's text colour, with a tick and a faint line across.
    /// </summary>
    private void DrawScale(DrawingContext dc, Pen grid, Pen gliding, Func<string, double, Brush, FormattedText> text)
    {
        dc.DrawRectangle(AxisBrush, null, _axis);
        var tick = new Pen(LabelBrush, 1);
        var lastLabel = double.PositiveInfinity;
        foreach (var (hertz, label) in ScaleStops)
        {
            var y = Math.Round(HeightOf(hertz)) + 0.5;
            // Labels are placed from the bottom up, skipping any that would crowd the one below.
            if (hertz >= TopHz || y < LabelSize / 2 + 1 || y > _topHeight - LabelSize / 2 - 1 || lastLabel - y < LabelSpacing)
            {
                continue;
            }

            lastLabel = y;
            // Across the heatmap the dots scroll with it; across the spectrum beside it they hold still.
            var from = AxisWidth;
            if (Usable(_heatmap))
            {
                dc.DrawLine(gliding, new Point(_heatmap.Left, y), new Point(_heatmap.Right, y));
                from = _heatmap.Right;
            }

            dc.DrawLine(grid, new Point(from, y), new Point(ActualWidth, y));
            dc.DrawLine(tick, new Point(AxisWidth - 5, y), new Point(AxisWidth, y));
            var formatted = text(label, LabelSize, LabelBrush);
            dc.DrawText(formatted, new Point(AxisWidth - 8 - formatted.Width, y - formatted.Height / 2));
        }
    }

    // The waveform: the swing of each column's audio, centred on a line whose dots scroll with it.
    private void DrawWave(DrawingContext dc, Pen grid)
    {
        var middle = _wave.Top + _wave.Height / 2;
        var half = _wave.Height / 2 - 2;
        dc.DrawLine(grid, new Point(_wave.Left, middle), new Point(_wave.Right, middle));
        var count = Math.Min(_columns, (int)Math.Ceiling(_wave.Width) + 2);
        var points = new Point[count * 2];
        for (var age = 0; age < count; age++)
        {
            var column = Ring(_head - 1 - age);
            var x = _wave.Right - _carry - age + 0.5;
            // At least a hairline, so quiet passages still show.
            var high = Math.Max(_waveHigh[column], 0.01f);
            var low = Math.Min(_waveLow[column], -0.01f);
            points[age] = new Point(x, middle - high * half);
            points[points.Length - 1 - age] = new Point(x, middle - low * half);
        }

        var wave = new StreamGeometry();
        using (var context = wave.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToList(), false, false);
        }

        wave.Freeze();
        dc.DrawGeometry(WaveBrush, null, wave);
    }

    // The frequencies marked on the scale, lowest first; ones too close together on a short window are skipped.
    private static readonly (double Hertz, string Label)[] ScaleStops =
    {
        (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1k"),
        (2000, "2k"), (5000, "5k"), (10000, "10k"), (20000, "20k"),
    };

    // ---- Colours ----

    private static DependencyProperty PaletteBrush(string name) => DependencyProperty.Register(
        name, typeof(Brush), typeof(Spectrogram),
        new PropertyMetadata(null, (element, _) => ((Spectrogram)element).BuildPalette()));

    private void BuildPalette()
    {
        var stops = _paletteChoice switch
        {
            // Vision's colours: midnight blue through cyan, green and yellow to red.
            SpectrogramPalette.Classic => new[]
            {
                (0.00, Color.FromRgb(0x00, 0x00, 0x0A)),
                (0.16, Color.FromRgb(0x00, 0x0C, 0x5C)),
                (0.32, Color.FromRgb(0x00, 0x46, 0xF0)),
                (0.46, Color.FromRgb(0x00, 0xC8, 0xFF)),
                (0.60, Color.FromRgb(0x18, 0xF0, 0x60)),
                (0.74, Color.FromRgb(0xE8, 0xF8, 0x10)),
                (0.87, Color.FromRgb(0xFF, 0x70, 0x00)),
                (1.00, Color.FromRgb(0xF0, 0x08, 0x08)),
            },
            // Black-body: embers to white heat.
            SpectrogramPalette.Fire => new[]
            {
                (0.00, Color.FromRgb(0x00, 0x00, 0x00)),
                (0.25, Color.FromRgb(0x3A, 0x04, 0x4A)),
                (0.50, Color.FromRgb(0xC0, 0x18, 0x20)),
                (0.72, Color.FromRgb(0xFF, 0x80, 0x00)),
                (0.88, Color.FromRgb(0xFF, 0xDC, 0x40)),
                (1.00, Color.FromRgb(0xFF, 0xFF, 0xF0)),
            },
            // The theme's own colours, so the analyzer matches the rest of Lucent.
            _ => new[]
            {
                (0.00, ColorOf(ThemeLowBrush, Colors.Black)),
                (0.35, ColorOf(ThemeMidBrush, Color.FromRgb(0x2A, 0x25, 0x50))),
                (0.72, ColorOf(ThemeHighBrush, Color.FromRgb(0x8B, 0x7C, 0xFF))),
                (1.00, ColorOf(ThemePeakBrush, Colors.White)),
            },
        };

        for (var i = 0; i < _palette.Length; i++)
        {
            var at = i / 255.0;
            var upper = 1;
            while (upper < stops.Length - 1 && stops[upper].Item1 < at)
            {
                upper++;
            }

            var (fromAt, from) = stops[upper - 1];
            var (toAt, to) = stops[upper];
            var blend = Math.Clamp((at - fromAt) / Math.Max(1e-6, toAt - fromAt), 0, 1);
            byte Mix(byte a, byte b) => (byte)Math.Round(a + (b - a) * blend);
            _palette[i] = 0xFF000000u | (uint)Mix(from.R, to.R) << 16 | (uint)Mix(from.G, to.G) << 8 | Mix(from.B, to.B);
        }

        PaletteChanged();
        Paint();
    }

    // A theme brush's colour: a solid brush's own, or a gradient's first stop.
    private static Color ColorOf(Brush? brush, Color fallback) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        GradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
        _ => fallback,
    };
}
