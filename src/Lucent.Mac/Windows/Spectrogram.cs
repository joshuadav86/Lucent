using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Lucent.Mac.Audio;
using NAudio.Dsp;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac.Windows;

/// <summary>
/// The analyzer's picture, like the Vision plugin's: a heatmap of the music scrolling right to left,
/// low notes at the bottom and high ones at the top, coloured by how loud each frequency was at each
/// moment, with the waveform scrolling underneath in step. Beside them sit the spectrum right now, on
/// the same frequency scale as the heatmap, and a stereo spread scope (see Spectrogram.Panels.cs).
/// Each of the four can be shown or hidden; the rest fill the space. It reads the music the same way
/// as the equalizer's live spectrum, but keeps the last few seconds so you can see them go by.
/// </summary>
/// <remarks>
/// The heatmap lives in a <see cref="WriteableBitmap"/> used as a ring: each frame only the new
/// columns are coloured and written, and the picture is drawn as the ring's two halves side by side.
/// </remarks>
public sealed partial class Spectrogram : Control
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

    public static readonly StyledProperty<IBrush?> ThemeLowBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush?>(nameof(ThemeLowBrush));

    public static readonly StyledProperty<IBrush?> ThemeMidBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush?>(nameof(ThemeMidBrush));

    public static readonly StyledProperty<IBrush?> ThemeHighBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush?>(nameof(ThemeHighBrush));

    public static readonly StyledProperty<IBrush?> ThemePeakBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush?>(nameof(ThemePeakBrush));

    public static readonly StyledProperty<IBrush> WaveBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush>(nameof(WaveBrush), Brushes.White);

    public static readonly StyledProperty<IBrush> LabelBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush>(nameof(LabelBrush), Brushes.White);

    public static readonly StyledProperty<IBrush> AxisBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush>(nameof(AxisBrush), Brushes.Black);

    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<Spectrogram, IBrush>(nameof(GridBrush), Brushes.Gray);

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
    private WriteableBitmap? _bitmap;
    private int _columns;
    private int _rows;
    private int _head;
    // The loudness of each row in the newest analysis, 0 to 1, for the spectrum panel.
    private float[] _latest = Array.Empty<float>();
    // The waveform's outline, reused from frame to frame.
    private Point[] _wavePoints = Array.Empty<Point>();

    // Where each part goes, worked out again when the size or the choice of parts changes.
    private AnalyzerModules _modules = AnalyzerModules.All;
    private Rect _axis;
    private Rect _heatmap;
    private Rect _spectrum;
    private Rect _wave;
    private Rect _stereo;
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
    private double _sinceMark;
    private TimeSpan _lastFrame;
    private bool _running;
    private bool _frameRequested;

    // Pens and labels, made once and again only when the theme's colours or font change.
    private IPen? _gridPen;
    private IPen? _tickPen;
    private IPen? _outlinePen;
    private readonly Dictionary<(string Text, double Size), FormattedText> _labels = new();
    private Typeface? _typeface;

    static Spectrogram()
    {
        AffectsRender<Spectrogram>(WaveBrushProperty, LabelBrushProperty, AxisBrushProperty, GridBrushProperty);
    }

    public Spectrogram(AudioCapture capture)
    {
        _capture = capture;
        ClipToBounds = true;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.LowQuality);
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        }

        // The window's frame and the theme palette follow the skin and theme as they change.
        this.SetResourceReference(ThemeLowBrushProperty, "BackgroundBrush");
        this.SetResourceReference(ThemeMidBrushProperty, "AccentSoftBrush");
        this.SetResourceReference(ThemeHighBrushProperty, "AccentBrush");
        this.SetResourceReference(ThemePeakBrushProperty, "TextBrush");
        this.SetResourceReference(WaveBrushProperty, "AccentGradientBrush");
        this.SetResourceReference(GridBrushProperty, "FaintTextBrush");
        this.SetResourceReference(LabelBrushProperty, "TextBrush");
        this.SetResourceReference(AxisBrushProperty, "SurfaceBrush");
        ResourcesChanged += (_, _) => ForgetLabels();
        BuildPalette();
    }

    public IBrush? ThemeLowBrush
    {
        get => GetValue(ThemeLowBrushProperty);
        set => SetValue(ThemeLowBrushProperty, value);
    }

    public IBrush? ThemeMidBrush
    {
        get => GetValue(ThemeMidBrushProperty);
        set => SetValue(ThemeMidBrushProperty, value);
    }

    public IBrush? ThemeHighBrush
    {
        get => GetValue(ThemeHighBrushProperty);
        set => SetValue(ThemeHighBrushProperty, value);
    }

    public IBrush? ThemePeakBrush
    {
        get => GetValue(ThemePeakBrushProperty);
        set => SetValue(ThemePeakBrushProperty, value);
    }

    public IBrush WaveBrush
    {
        get => GetValue(WaveBrushProperty);
        set => SetValue(WaveBrushProperty, value);
    }

    public IBrush LabelBrush
    {
        get => GetValue(LabelBrushProperty);
        set => SetValue(LabelBrushProperty, value);
    }

    public IBrush AxisBrush
    {
        get => GetValue(AxisBrushProperty);
        set => SetValue(AxisBrushProperty, value);
    }

    public IBrush GridBrush
    {
        get => GetValue(GridBrushProperty);
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

    // A part that isn't shown has no area (WPF's Rect.Empty).
    private static bool IsEmpty(Rect rect) => rect.Width <= 0 && rect.Height <= 0;

    /// <summary>
    /// Lays the parts out in two rows: the heatmap and spectrum on top with the frequency scale down
    /// the left, the waveform and stereo scope underneath. A hidden part's neighbour takes its place.
    /// </summary>
    private void Arrange()
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
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
            return (left ? whole : default, right ? whole : default);
        }

        _axis = top ? new Rect(0, 0, AxisWidth, height) : default;
        var left = top ? AxisWidth : 0;
        (_heatmap, _spectrum) = top
            ? Row(Shows(AnalyzerModules.Heatmap), Shows(AnalyzerModules.Spectrum), left, 0, width, _topHeight, side)
            : (default, default);
        (_wave, _stereo) = bottom
            ? Row(Shows(AnalyzerModules.Waveform), Shows(AnalyzerModules.Stereo), left, height - bottomHeight, width, bottomHeight, side)
            : (default, default);
    }

    // ---- Running ----

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
        else if (change.Property == ThemeLowBrushProperty || change.Property == ThemeMidBrushProperty
            || change.Property == ThemeHighBrushProperty || change.Property == ThemePeakBrushProperty)
        {
            BuildPalette();
        }
        else if (change.Property == GridBrushProperty || change.Property == LabelBrushProperty)
        {
            _gridPen = _tickPen = _outlinePen = null;
            ForgetLabels();
        }
    }

    private void Start()
    {
        if (!_running && IsVisible && VisualRoot is not null)
        {
            _running = true;
            _lastFrame = TimeSpan.Zero;
            RequestFrame();
        }
    }

    private void Stop() => _running = false;

    // Once a screen frame, like WPF's CompositionTarget.Rendering, for as long as it's showing.
    private void RequestFrame()
    {
        if (_running && !_frameRequested && TopLevel.GetTopLevel(this) is { } top)
        {
            _frameRequested = true;
            top.RequestAnimationFrame(OnFrame);
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        Resize();
    }

    /// <summary>One column per pixel across, one row per pixel down (up to a point), keeping what fits.</summary>
    private void Resize()
    {
        Arrange();
        static int Pixels(Rect rect) => IsEmpty(rect) ? 0 : (int)Math.Ceiling(rect.Width);
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
        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(new PixelSize(columns, rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        _mappedRate = 0;
        ResizePanels();
        Paint();
    }

    // A column's place in the ring, counting round from any whole number.
    private int Ring(int column) => _columns == 0 ? 0 : ((column % _columns) + _columns) % _columns;

    private void OnFrame(TimeSpan frameTime)
    {
        _frameRequested = false;
        if (!_running)
        {
            return;
        }

        // Timed by when the frame will be shown, not when this runs, so the scroll moves the same
        // distance every frame.
        var deltaTime = _lastFrame == TimeSpan.Zero ? 0 : Math.Clamp((frameTime - _lastFrame).TotalSeconds, 0, 0.1);
        _lastFrame = frameTime;
        Advance(deltaTime);
        RequestFrame();
    }

    /// <summary>Moves the picture on by <paramref name="deltaTime"/> seconds. (Screenshots call this directly.)</summary>
    internal void Advance(double deltaTime)
    {
        // Paused or stopped, the picture holds still so you can look at it.
        if (_capture.IsSilent || _bitmap is null)
        {
            return;
        }

        var sampleRate = _capture.SampleRate;
        var delay = sampleRate * AudioPlayer.OutputLatencyMs / 1000 * 2 / 3;
        UpdatePanels((float)deltaTime, delay);

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

            var decibels = 10f * MathF.Log10(power + 1e-12f) + _rowTilt[row];
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

    /// <summary>Colours <paramref name="count"/> columns of the ring from <paramref name="first"/> straight into the bitmap.</summary>
    private unsafe void PaintColumns(int first, int count)
    {
        if (_bitmap is null)
        {
            return;
        }

        using var frame = _bitmap.Lock();
        var stride = frame.RowBytes / 4;
        var pixels = (uint*)frame.Address;

        // At most two runs: up to the end of the ring, then on from its start.
        while (count > 0)
        {
            var run = Math.Min(count, _columns - first);
            for (var row = 0; row < _rows; row++)
            {
                var at = row * _columns + first;
                var to = pixels + row * stride + first;
                for (var i = 0; i < run; i++)
                {
                    to[i] = _palette[_levels[at + i]];
                }
            }

            count -= run;
            first = 0;
        }
    }

    public override void Render(DrawingContext dc)
    {
        if (_bitmap is null || Bounds.Width < 2 || Bounds.Height < 2)
        {
            return;
        }

        var grid = _gridPen ??= new ImmutablePen(GridBrush.ToImmutable(), 1, new ImmutableDashStyle(new[] { 1.0, 1.0 }, 0));

        // The history runs right to left from each part's right edge; a part narrower than the
        // history shows only its newest end. Everything is shifted by the same fraction of a pixel.
        if (Usable(_heatmap))
        {
            using (dc.PushClip(_heatmap))
            {
                DrawHeatmap(dc);
                DrawMarks(dc, grid, _heatmap);
            }
        }

        if (!IsEmpty(_axis))
        {
            DrawScale(dc, grid);
        }

        if (Usable(_spectrum))
        {
            DrawSpectrum(dc, grid);
        }

        if (Usable(_wave))
        {
            using (dc.PushClip(_wave))
            {
                DrawMarks(dc, grid, _wave);
                DrawWave(dc, grid);
            }
        }

        if (Usable(_stereo))
        {
            DrawStereo(dc, grid);
        }
    }

    // The ring as two pieces: the newer columns (0 to the newest) end at the right edge, nudged left
    // by however far the picture has glided, and the older ones (after the newest) sit before them.
    private void DrawHeatmap(DrawingContext dc)
    {
        var bitmap = _bitmap!;
        var newest = Ring(_head - 1);
        var right = _heatmap.Right - _carry + 1;
        var newer = newest + 1;
        dc.DrawImage(bitmap, new Rect(0, 0, newer, _rows), new Rect(right - newer, _heatmap.Top, newer, _heatmap.Height));
        var older = _columns - newer;
        if (older > 0)
        {
            dc.DrawImage(bitmap, new Rect(newer, 0, older, _rows), new Rect(right - newer - older, _heatmap.Top, older, _heatmap.Height));
        }
    }

    private static bool Usable(Rect rect) => rect.Width >= 2 && rect.Height >= 2;

    // Faint lines every few seconds, moving with the music.
    private void DrawMarks(DrawingContext dc, IPen grid, Rect area)
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
    private void DrawScale(DrawingContext dc, IPen grid)
    {
        dc.DrawRectangle(AxisBrush, null, _axis);
        var tick = _tickPen ??= new ImmutablePen(LabelBrush.ToImmutable(), 1);
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
            dc.DrawLine(grid, new Point(AxisWidth, y), new Point(Bounds.Width, y));
            dc.DrawLine(tick, new Point(AxisWidth - 5, y), new Point(AxisWidth, y));
            var formatted = Text(label, LabelSize);
            dc.DrawText(formatted, new Point(AxisWidth - 8 - formatted.Width, y - formatted.Height / 2));
        }
    }

    // The waveform: the swing of each column's audio, centred on a line.
    private void DrawWave(DrawingContext dc, IPen grid)
    {
        var middle = _wave.Top + _wave.Height / 2;
        var half = _wave.Height / 2 - 2;
        dc.DrawLine(grid, new Point(_wave.Left, middle), new Point(_wave.Right, middle));
        var count = Math.Min(_columns, (int)Math.Ceiling(_wave.Width) + 2);
        if (count < 1)
        {
            return;
        }

        if (_wavePoints.Length != count * 2)
        {
            _wavePoints = new Point[count * 2];
        }

        var points = _wavePoints;
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
            context.BeginFigure(points[0], true);
            for (var i = 1; i < points.Length; i++)
            {
                context.LineTo(points[i]);
            }

            context.EndFigure(true);
        }

        dc.DrawGeometry(WaveBrush, null, wave);
    }

    // The frequencies marked on the scale, lowest first; ones too close together on a short window are skipped.
    private static readonly (double Hertz, string Label)[] ScaleStops =
    {
        (50, "50"), (100, "100"), (200, "200"), (500, "500"), (1000, "1k"),
        (2000, "2k"), (5000, "5k"), (10000, "10k"), (20000, "20k"),
    };

    // A label in the theme's font and text colour, made once and kept.
    private FormattedText Text(string text, double size)
    {
        if (_labels.TryGetValue((text, size), out var made))
        {
            return made;
        }

        _typeface ??= new Typeface(
            this.TryFindResource("UiFont", out var font) && font is FontFamily family ? family : FontFamily.Default,
            FontStyle.Normal, FontWeight.SemiBold);
        made = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, _typeface.Value, size, LabelBrush);
        _labels[(text, size)] = made;
        return made;
    }

    private void ForgetLabels()
    {
        _labels.Clear();
        _typeface = null;
    }

    // ---- Colours ----

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
    private static Color ColorOf(IBrush? brush, Color fallback) => brush switch
    {
        ISolidColorBrush solid => solid.Color,
        IGradientBrush { GradientStops.Count: > 0 } gradient => gradient.GradientStops[0].Color,
        _ => fallback,
    };
}
