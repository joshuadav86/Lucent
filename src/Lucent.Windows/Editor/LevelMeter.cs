using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Lucent.Editor.Dsp;

namespace Lucent.Editor;

/// <summary>
/// The editor's level meter: a bar per channel from -60 to 0 dBFS with a falling peak hold, and below it
/// the true peak and the momentary, short-term and integrated loudness of what's playing.
/// </summary>
public sealed class LevelMeter : FrameworkElement
{
    private const double FloorDb = -60;
    private const double BarHeight = 5;
    private static readonly Brush HotBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4F)));
    private static readonly Brush WarmBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x41)));

    public static readonly DependencyProperty BarBrushProperty = Register(nameof(BarBrush));
    public static readonly DependencyProperty TrackBrushProperty = Register(nameof(TrackBrush));
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush));

    private double[] _levels = Array.Empty<double>();
    private double[] _holds = Array.Empty<double>();
    private DateTime[] _holdSince = Array.Empty<DateTime>();
    private MeterReading? _reading;

    public LevelMeter()
    {
        Height = 30;
        ToolTip = "Levels of what's playing. TP is the true peak (between samples) so far; M, S, and I are the momentary (0.4 s), " +
                  "short-term (3 s), and integrated loudness in LUFS measured as streaming services do.";
    }

    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    /// <summary>Shows a new reading, or null when playback stops (the bars drop, the numbers stay).</summary>
    public void Show(MeterReading? reading)
    {
        var now = DateTime.UtcNow;
        var channels = reading?.Peaks.Length ?? _levels.Length;
        if (_levels.Length != channels)
        {
            _levels = Enumerable.Repeat(FloorDb, channels).ToArray();
            _holds = Enumerable.Repeat(FloorDb, channels).ToArray();
            _holdSince = Enumerable.Repeat(now, channels).ToArray();
        }

        for (var c = 0; c < channels; c++)
        {
            var db = reading is null ? FloorDb : Math.Max(FloorDb, AudioAnalysis.ToDb(reading.Peaks[c]));
            // Up at once, down at about 24 dB a second, like a hardware peak meter.
            _levels[c] = db >= _levels[c] || reading is null ? db : Math.Max(db, _levels[c] - 0.8);
            if (db >= _holds[c])
            {
                _holds[c] = db;
                _holdSince[c] = now;
            }
            else if (reading is null)
            {
                _holds[c] = FloorDb;
            }
            else if ((now - _holdSince[c]).TotalSeconds > 1.5)
            {
                _holds[c] = Math.Max(_levels[c], _holds[c] - 1.2);
            }
        }

        if (reading is not null)
        {
            _reading = reading;
        }

        InvalidateVisual();
    }

    /// <summary>Clears the numbers, for a new playback.</summary>
    public void Reset()
    {
        _reading = null;
        _levels = Array.Empty<double>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, ActualHeight));
        var channels = Math.Max(1, _levels.Length);
        for (var c = 0; c < channels; c++)
        {
            var y = c * (BarHeight + 2);
            dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, width, BarHeight), 2, 2);
            if (c >= _levels.Length)
            {
                continue;
            }

            var level = Fraction(_levels[c]) * width;
            // The top 6 dB turn amber, and anything at full scale red.
            var warm = Fraction(-6) * width;
            dc.DrawRectangle(BarBrush, null, new Rect(0, y, Math.Min(level, warm), BarHeight));
            if (level > warm)
            {
                dc.DrawRectangle(WarmBrush, null, new Rect(warm, y, level - warm, BarHeight));
            }

            var hold = Fraction(_holds[c]) * width;
            dc.DrawRectangle(_holds[c] >= -0.1 ? HotBrush : LabelBrush, null, new Rect(Math.Max(0, hold - 2), y, 2, BarHeight));
        }

        string Lufs(double? value) => value is { } v && !double.IsInfinity(v) ? v.ToString("0.0", CultureInfo.CurrentCulture) : "–";
        var r = _reading;
        var truePeak = r is null ? "–" : Lufs(AudioAnalysis.ToDb(r.TruePeak));
        var text = $"TP {truePeak}   M {Lufs(r?.Momentary)}   S {Lufs(r?.ShortTerm)}   I {Lufs(r?.Integrated)} LUFS";
        var hot = r is not null && r.TruePeak >= 1;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Consolas, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            10.5, hot ? HotBrush : LabelBrush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        formatted.MaxTextWidth = Math.Max(1, width);
        formatted.Trimming = TextTrimming.CharacterEllipsis;
        dc.DrawText(formatted, new Point(0, channels * (BarHeight + 2) + 2));
    }

    private static double Fraction(double db) => Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(LevelMeter),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
