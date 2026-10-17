using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Windows;

// The two panels beside the heatmap and waveform, as in Vision: the spectrum right now, as bars on
// the heatmap's frequency scale with peak marks that hold and fall, and a stereo spread scope.
public sealed partial class Spectrogram
{
    // How many of the newest stereo samples the scope looks at each frame, at most.
    private const int StereoSamples = 2048;
    // How quickly the scope's traces fade, and how bright each sample makes its dot.
    private const float StereoFadePerSecond = 9f;
    private const float StereoDot = 0.28f;
    private const double CorrelationHeight = 24;

    // The spectrum panel's bars: how tall each band is, and how its peak mark behaves.
    private const double BandHeight = 6;
    private const float PeakHoldSeconds = 0.6f;
    private const float PeakFallPerSecond = 0.35f;

    private readonly Brush[] _paletteBrushes = new Brush[256];
    private float[] _spectrumLevels = Array.Empty<float>();
    private float[] _spectrumPeaks = Array.Empty<float>();
    private float[] _spectrumHold = Array.Empty<float>();

    private readonly float[] _left = new float[StereoSamples];
    private readonly float[] _right = new float[StereoSamples];
    private float[] _dots = Array.Empty<float>();
    private uint[] _stereoPixels = Array.Empty<uint>();
    private WriteableBitmap? _stereoBitmap;
    private int _stereoSize;
    private float _correlation;

    private void ResizePanels()
    {
        if (_spectrumLevels.Length != _latest.Length)
        {
            _spectrumLevels = new float[_latest.Length];
            _spectrumPeaks = new float[_latest.Length];
            _spectrumHold = new float[_latest.Length];
        }

        var size = Usable(_stereo) ? (int)Math.Max(0, Math.Min(_stereo.Width, _stereo.Height - CorrelationHeight - 4)) : 0;
        if (size == _stereoSize)
        {
            return;
        }

        _stereoSize = size;
        _dots = new float[size * size];
        _stereoPixels = new uint[size * size];
        _stereoBitmap = size >= 8 ? new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgr32, null) : null;
        PaintStereo();
    }

    /// <summary>Moves the spectrum bars and the stereo scope on by one screen frame.</summary>
    private void UpdatePanels(float deltaTime, int delay)
    {
        // The bars rise quickly and fall back slowly; the peak marks hold a moment, then drift down.
        var attack = 1f - MathF.Exp(-deltaTime * 30f);
        var release = 1f - MathF.Exp(-deltaTime * 6f);
        var count = Math.Min(_latest.Length, _spectrumLevels.Length);
        for (var row = 0; row < count; row++)
        {
            var target = _latest[row];
            var level = _spectrumLevels[row];
            level += (target - level) * (target > level ? attack : release);
            _spectrumLevels[row] = level;
            if (level >= _spectrumPeaks[row])
            {
                _spectrumPeaks[row] = level;
                _spectrumHold[row] = PeakHoldSeconds;
            }
            else if ((_spectrumHold[row] -= deltaTime) < 0)
            {
                _spectrumPeaks[row] = MathF.Max(level, _spectrumPeaks[row] - PeakFallPerSecond * deltaTime);
            }
        }

        if (_stereoBitmap is null && !Shows(AnalyzerModules.Stereo))
        {
            return;
        }

        _capture.ReadLatestStereo(_left, _right, delay);

        // How alike the two sides are: +1 is mono, 0 is wide and unrelated, -1 is out of phase.
        double both = 0, leftPower = 0, rightPower = 0;
        for (var i = 0; i < StereoSamples; i++)
        {
            both += _left[i] * _right[i];
            leftPower += _left[i] * _left[i];
            rightPower += _right[i] * _right[i];
        }

        var correlation = leftPower + rightPower < 1e-7 ? 1f : (float)(both / Math.Sqrt(leftPower * rightPower + 1e-12));
        _correlation += (Math.Clamp(correlation, -1f, 1f) - _correlation) * (1f - MathF.Exp(-deltaTime * 8f));

        if (_stereoBitmap is null)
        {
            return;
        }

        var fade = MathF.Exp(-deltaTime * StereoFadePerSecond);
        for (var i = 0; i < _dots.Length; i++)
        {
            _dots[i] *= fade;
        }

        // Each sample is a dot: up for what both sides share (mid), sideways for how they differ
        // (side), so mono is a vertical line and a wide mix fills the diamond.
        var size = _stereoSize;
        var half = (size - 1) / 2f;
        var fresh = Math.Clamp((int)(_capture.SampleRate * deltaTime), 64, StereoSamples);
        for (var i = StereoSamples - fresh; i < StereoSamples; i++)
        {
            var mid = (_left[i] + _right[i]) / 2;
            var side = (_right[i] - _left[i]) / 2;
            var x = (int)MathF.Round(half + Math.Clamp(side, -1f, 1f) * half);
            var y = (int)MathF.Round(half - Math.Clamp(mid, -1f, 1f) * half);
            var at = y * size + x;
            _dots[at] = MathF.Min(1f, _dots[at] + StereoDot);
        }

        PaintStereo();
    }

    private void PaintStereo()
    {
        if (_stereoBitmap is null)
        {
            return;
        }

        for (var i = 0; i < _dots.Length; i++)
        {
            _stereoPixels[i] = _palette[(int)(_dots[i] * 255)];
        }

        _stereoBitmap.WritePixels(new Int32Rect(0, 0, _stereoSize, _stereoSize), _stereoPixels, _stereoSize * 4, 0);
    }

    private void PaletteChanged()
    {
        for (var i = 0; i < _palette.Length; i++)
        {
            var colour = _palette[i];
            var brush = new SolidColorBrush(Color.FromRgb((byte)(colour >> 16), (byte)(colour >> 8), (byte)colour));
            brush.Freeze();
            _paletteBrushes[i] = brush;
        }

        PaintStereo();
    }

    /// <summary>
    /// The spectrum right now, as bars growing from the left on the heatmap's own frequency scale,
    /// each coloured like the heatmap at that loudness, with a mark at its recent peak.
    /// </summary>
    private void DrawSpectrum(DrawingContext dc, Pen grid)
    {
        var area = _spectrum;
        dc.DrawRectangle(_paletteBrushes[0], null, area);
        for (var quarter = 1; quarter < 4; quarter++)
        {
            var x = Math.Round(area.Left + area.Width * quarter / 4) + 0.5;
            dc.DrawLine(grid, new Point(x, area.Top), new Point(x, area.Bottom));
        }

        var rows = _spectrumLevels.Length;
        var bands = Math.Max(1, (int)(area.Height / BandHeight));
        if (rows == 0)
        {
            return;
        }

        var peakBrush = LabelBrush;
        var height = area.Height / bands;
        for (var band = 0; band < bands; band++)
        {
            // Rows run top to bottom, like the bands.
            var first = band * rows / bands;
            var last = Math.Max(first, (band + 1) * rows / bands - 1);
            var level = 0f;
            var peak = 0f;
            for (var row = first; row <= last && row < rows; row++)
            {
                level = MathF.Max(level, _spectrumLevels[row]);
                peak = MathF.Max(peak, _spectrumPeaks[row]);
            }

            var top = area.Top + band * height;
            var barHeight = Math.Max(1, height - 1);
            if (level > 0.005f)
            {
                dc.DrawRectangle(_paletteBrushes[(int)(level * 255)], null,
                    new Rect(area.Left, top, area.Width * level, barHeight));
            }

            if (peak > 0.01f)
            {
                dc.DrawRectangle(peakBrush, null,
                    new Rect(area.Left + Math.Max(0, area.Width * peak - 2), top, 2, barHeight));
            }
        }
    }

    /// <summary>
    /// The stereo spread scope: the music's dots inside a diamond (left channel up the left edge,
    /// right up the right), and under it a bar showing how alike the two sides are.
    /// </summary>
    private void DrawStereo(DrawingContext dc, Pen grid, Func<string, double, Brush, FormattedText> text)
    {
        var area = _stereo;
        var labels = LabelBrush;
        if (_stereoBitmap is not null)
        {
            var size = _stereoSize;
            var square = new Rect(area.Left + (area.Width - size) / 2, area.Top, size, size);
            dc.DrawImage(_stereoBitmap, square);

            var centre = new Point(square.Left + size / 2.0, square.Top + size / 2.0);
            var half = size / 2.0;
            var outline = new Pen(GridBrush, 1);
            var diamond = new StreamGeometry();
            using (var context = diamond.Open())
            {
                context.BeginFigure(new Point(centre.X, square.Top), false, true);
                context.PolyLineTo(new[]
                {
                    new Point(square.Right, centre.Y), new Point(centre.X, square.Bottom), new Point(square.Left, centre.Y),
                }, true, false);
            }

            diamond.Freeze();
            dc.DrawGeometry(null, outline, diamond);
            dc.DrawLine(grid, new Point(centre.X, square.Top), new Point(centre.X, square.Bottom));
            dc.DrawLine(grid, new Point(square.Left, centre.Y), new Point(square.Right, centre.Y));

            var left = text("L", 11, labels);
            var right = text("R", 11, labels);
            dc.DrawText(left, new Point(centre.X - half / 2 - left.Width - 4, centre.Y - half / 2 - left.Height));
            dc.DrawText(right, new Point(centre.X + half / 2 + 4, centre.Y - half / 2 - right.Height));
        }

        // The correlation bar: -1 (out of phase) on the left, +1 (mono) on the right.
        var barTop = area.Bottom - CorrelationHeight + 2;
        var track = new Rect(area.Left + 18, barTop, Math.Max(0, area.Width - 36), 6);
        dc.DrawRectangle(_paletteBrushes[40], null, track);
        var middle = track.Left + track.Width / 2;
        var at = middle + _correlation * track.Width / 2;
        var fill = _paletteBrushes[_correlation >= 0 ? 200 : 255];
        dc.DrawRectangle(fill, null, new Rect(Math.Min(middle, at), track.Top, Math.Abs(at - middle), track.Height));
        dc.DrawLine(grid, new Point(middle, track.Top - 2), new Point(middle, track.Bottom + 2));
        dc.DrawRectangle(labels, null, new Rect(at - 1, track.Top - 2, 2, track.Height + 4));
        var minus = text("−1", 10, labels);
        var plus = text("+1", 10, labels);
        dc.DrawText(minus, new Point(track.Left - minus.Width - 3, track.Top + track.Height / 2 - minus.Height / 2));
        dc.DrawText(plus, new Point(track.Right + 3, track.Top + track.Height / 2 - plus.Height / 2));
    }
}
