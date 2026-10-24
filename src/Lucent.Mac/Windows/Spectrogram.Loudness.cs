using System.Globalization;
using Avalonia;
using Avalonia.Media;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac.Windows;

// The loudness readouts along the bottom, as a loudness meter shows them (EBU R 128): momentary, short-term
// and integrated loudness in LUFS, the loudness range in LU, and the true peak in dBTP. The capture's
// LoudnessMeter does the measuring; this only shows it. Click the strip to measure from nothing again.
public sealed partial class Spectrogram
{
    private const double LoudnessHeight = 50;
    // The bars under the loudness readouts run from this up to full scale.
    private const double LoudnessFloor = -60;
    // A true peak above this risks clipping once the music is made into an MP3 or AAC, so it shows hot.
    private const double TruePeakLimit = -1;

    private Rect _loudness;
    private LoudnessReading _reading = new(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity, 0,
        double.NegativeInfinity);

    /// <summary>True when a point is on the loudness readouts.</summary>
    public bool IsOnLoudness(Point point) => Usable(_loudness) && _loudness.Contains(point);

    /// <summary>Starts the loudness readings again from nothing, for measuring from here on.</summary>
    public void RestartLoudness()
    {
        _capture.Loudness.Restart();
        _reading = _capture.Loudness.Read();
        InvalidateVisual();
    }

    // Takes the loudness strip off the bottom when it's shown with other parts; alone it fills the space.
    // Returns the height left for the rest.
    private double ArrangeLoudness(double width, double height)
    {
        if (!Shows(AnalyzerModules.Loudness))
        {
            _loudness = default;
            return height;
        }

        if ((_modules & ~AnalyzerModules.Loudness & AnalyzerModules.All) == 0)
        {
            _loudness = new Rect(0, 0, width, height);
            return 0;
        }

        var strip = Math.Min(LoudnessHeight, height);
        _loudness = new Rect(0, height - strip, width, strip);
        return Math.Max(0, height - strip - Gap);
    }

    private void UpdateLoudness()
    {
        if (Shows(AnalyzerModules.Loudness))
        {
            _reading = _capture.Loudness.Read();
        }
    }

    /// <summary>Five readouts side by side, each a small label over its number, the loudness ones with a bar underneath.</summary>
    private void DrawLoudness(DrawingContext dc, IPen grid)
    {
        var area = _loudness;
        dc.DrawRectangle(AxisBrush, null, area);
        var readouts = new (string Label, double Value, string Unit, bool Bar)[]
        {
            ("MOMENTARY", _reading.Momentary, "LUFS", true),
            ("SHORT-TERM", _reading.ShortTerm, "LUFS", true),
            ("INTEGRATED", _reading.Integrated, "LUFS", true),
            ("RANGE", _reading.Integrated > double.NegativeInfinity ? _reading.Range : double.NegativeInfinity, "LU", false),
            ("TRUE PEAK", _reading.TruePeak, "dBTP", false),
        };

        var width = area.Width / readouts.Length;
        var valueSize = Math.Clamp(area.Height * 0.36, 15, 34);
        const double labelSize = 10;
        const double barHeight = 3;
        for (var i = 0; i < readouts.Length; i++)
        {
            var (label, value, unit, bar) = readouts[i];
            var tile = new Rect(area.Left + i * width, area.Top, width, area.Height);
            if (i > 0)
            {
                var x = Math.Round(tile.Left) + 0.5;
                dc.DrawLine(grid, new Point(x, tile.Top + 6), new Point(x, tile.Bottom - 6));
            }

            var peak = label == "TRUE PEAK";
            var hot = peak && value > TruePeakLimit;
            var name = Text(label, labelSize, GridBrush);
            var number = Text(Format(value, peak), valueSize, hot ? _paletteBrushes[255] : LabelBrush);
            var units = Text(unit, labelSize, GridBrush);
            // Laid out as if every readout had a bar, so the labels and numbers line up across the strip.
            var height = name.Height + number.Height - 2 + barHeight + 3;
            var top = tile.Top + Math.Max(0, (tile.Height - height) / 2);
            var left = tile.Left + 10;
            dc.DrawText(name, new Point(left, top));
            dc.DrawText(number, new Point(left, top + name.Height - 2));
            // The unit beside the number, when there's room for it.
            if (left + number.Width + 4 + units.Width <= tile.Right - 6)
            {
                dc.DrawText(units, new Point(left + number.Width + 4, top + name.Height - 2 + number.Height - units.Height - 2));
            }

            if (bar)
            {
                var track = new Rect(left, top + name.Height + number.Height + 1, Math.Max(0, tile.Width - 20), barHeight);
                dc.DrawRectangle(_paletteBrushes[40], null, track);
                var level = double.IsNegativeInfinity(value) ? 0 : Math.Clamp((value - LoudnessFloor) / -LoudnessFloor, 0, 1);
                if (level > 0)
                {
                    dc.DrawRectangle(_paletteBrushes[(int)(level * 255)], null, new Rect(track.Left, track.Top, track.Width * level, track.Height));
                }
            }
        }
    }

    // One decimal place, with a true minus sign (and a plus on a peak above full scale); a dash until there's something to show.
    private static string Format(double value, bool signed) => double.IsNegativeInfinity(value) || double.IsNaN(value)
        ? "—"
        : value.ToString(signed && value >= 0.05 ? "+0.0" : "0.0", CultureInfo.InvariantCulture).Replace('-', '−');
}
