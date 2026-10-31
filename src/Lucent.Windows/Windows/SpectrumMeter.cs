using System.Windows;
using System.Windows.Media;
using Lucent.Audio;
using Lucent.Settings;
using Lucent.Visualizer;

namespace Lucent.Windows;

/// <summary>
/// The little meter in the player window, like Winamp's: spectrum bars with falling peak
/// caps, or an oscilloscope line. It analyzes the music itself, every frame.
/// </summary>
public sealed class SpectrumMeter : FrameworkElement
{
    private const int BarCount = 20;
    private const double Gap = 2;
    private const float StillLevel = 0.002f;

    private readonly AudioAnalyzer _analyzer;
    private readonly float[] _bars = new float[BarCount];
    private readonly float[] _peaks = new float[BarCount];
    private readonly float[] _peakHold = new float[BarCount];
    private DateTime _lastFrame = DateTime.UtcNow;
    private MeterMode _mode = MeterMode.Bars;
    private bool _drawnStill;

    public SpectrumMeter(AudioCapture capture)
    {
        _analyzer = new AudioAnalyzer(capture);
        SnapsToDevicePixels = true;
    }

    public MeterMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _drawnStill = false;
            InvalidateVisual();
        }
    }

    /// <summary>Reads the newest audio and redraws. Called by the player window once per screen frame.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        var deltaTime = (float)Math.Clamp((now - _lastFrame).TotalSeconds, 0.0, 0.1);
        _lastFrame = now;
        if (_mode == MeterMode.Off)
        {
            return;
        }

        _analyzer.Update(deltaTime);
        var spectrum = _analyzer.TextureData;
        var perBar = AudioAnalyzer.TextureWidth / BarCount;
        for (var bar = 0; bar < BarCount; bar++)
        {
            var level = 0f;
            for (var i = 0; i < perBar; i++)
            {
                level = Math.Max(level, spectrum[bar * perBar + i]);
            }

            level = Math.Clamp(level, 0f, 1f);
            // Bars jump up at once and sink smoothly, like a hardware meter.
            _bars[bar] = level > _bars[bar] ? level : Math.Max(level, _bars[bar] - deltaTime * 2.2f);
            if (_bars[bar] >= _peaks[bar])
            {
                _peaks[bar] = _bars[bar];
                _peakHold[bar] = 0.35f;
            }
            else if ((_peakHold[bar] -= deltaTime) < 0)
            {
                _peaks[bar] = Math.Max(_bars[bar], _peaks[bar] - deltaTime * 0.9f);
            }
        }

        // Nothing playing and the meter already drawn flat: no need to draw it again every frame,
        // which kept the player window (and the system's compositor) busy while idle.
        var still = IsStill();
        if (still && _drawnStill)
        {
            return;
        }

        _drawnStill = still;
        InvalidateVisual();
    }

    /// <summary>Whether the bars, their peaks and the waveform are all flat (less than a pixel).</summary>
    private bool IsStill()
    {
        for (var bar = 0; bar < BarCount; bar++)
        {
            if (_bars[bar] > StillLevel || _peaks[bar] > StillLevel)
            {
                return false;
            }
        }

        var wave = _analyzer.TextureData.AsSpan(AudioAnalyzer.TextureWidth, AudioAnalyzer.TextureWidth);
        foreach (var sample in wave)
        {
            if (Math.Abs(sample) > StillLevel)
            {
                return false;
            }
        }

        return true;
    }

    protected override void OnRender(DrawingContext drawing)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        // Transparent but hit-testable, so the meter can be clicked anywhere to change mode.
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (width < 4 || height < 4)
        {
            return;
        }

        var fill = TryFindResource("AccentGradientBrush") as Brush ?? Brushes.White;
        var faint = TryFindResource("FaintTextBrush") as Brush ?? Brushes.Gray;
        var text = TryFindResource("TextBrush") as Brush ?? Brushes.White;

        switch (_mode)
        {
            case MeterMode.Bars:
            {
                var barWidth = (width - Gap * (BarCount - 1)) / BarCount;
                for (var bar = 0; bar < BarCount; bar++)
                {
                    var x = bar * (barWidth + Gap);
                    var barHeight = Math.Max(1, _bars[bar] * height);
                    drawing.DrawRectangle(fill, null, new Rect(x, height - barHeight, barWidth, barHeight));
                    var peakY = height - Math.Max(1, _peaks[bar] * height) - 2;
                    drawing.DrawRectangle(text, null, new Rect(x, Math.Max(0, peakY), barWidth, 1.5));
                }

                break;
            }
            case MeterMode.Scope:
            {
                var wave = _analyzer.TextureData.AsSpan(AudioAnalyzer.TextureWidth);
                var geometry = new StreamGeometry();
                using (var context = geometry.Open())
                {
                    for (var i = 0; i < wave.Length; i++)
                    {
                        // Row 1 of the texture holds the waveform, -1 to 1.
                        var point = new Point(i * width / (wave.Length - 1), height * (0.5 - Math.Clamp(wave[i], -1f, 1f) * 0.45));
                        if (i == 0)
                        {
                            context.BeginFigure(point, false, false);
                        }
                        else
                        {
                            context.LineTo(point, true, true);
                        }
                    }
                }

                geometry.Freeze();
                drawing.DrawGeometry(null, new Pen(fill, 1.5) { LineJoin = PenLineJoin.Round }, geometry);
                break;
            }
            default:
                drawing.DrawRectangle(faint, null, new Rect(0, height - 1, width, 1));
                break;
        }
    }
}
