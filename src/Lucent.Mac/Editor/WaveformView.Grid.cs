using Avalonia;
using Avalonia.Media;
using Lucent.Editor.Dsp;

namespace Lucent.Mac.Editor;

/// <summary>
/// The beat grid: a faint line on every beat and a stronger one, numbered, at the start of every bar, drawn
/// when <see cref="ShowBeatGrid"/> is on. Whenever the beats are known, a selection shows its length in bars and
/// beats in a tag at its top, so a loop can be stretched to a length while watching it. Clicking the tag switches it
/// to minutes and seconds and back (<see cref="LengthInTime"/>).
/// </summary>
public sealed partial class WaveformView
{
    // Lines closer together than this, in pixels, are thinned out: beats first, then bars two or four at a time.
    private const double MinGridSpacing = 7;
    private const double MinBarLabelSpacing = 30;

    private BeatGrid? _grid;
    private bool _showBeatGrid;
    private bool _lengthInTime;

    // Where the length tag was last drawn, so a click on it can switch its units; null while it isn't shown.
    private Rect? _lengthTag;

    /// <summary>Raised when a click on the length tag switches it between bars and beats and minutes and seconds.</summary>
    public event EventHandler? LengthUnitsChanged;

    /// <summary>The selection's length tag shows minutes and seconds instead of bars and beats.</summary>
    public bool LengthInTime
    {
        get => _lengthInTime;
        set
        {
            _lengthInTime = value;
            InvalidateVisual();
        }
    }

    /// <summary>The beats and bars of the sound, or null if they aren't known (yet, or there's no steady beat).</summary>
    public BeatGrid? Grid
    {
        get => _grid;
        set
        {
            _grid = value;
            InvalidateVisual();
        }
    }

    /// <summary>Draw the beats and bars over the waveform.</summary>
    public bool ShowBeatGrid
    {
        get => _showBeatGrid;
        set
        {
            _showBeatGrid = value;
            InvalidateVisual();
        }
    }

    private void DrawBeatGrid(DrawingContext dc, double width, double lanesTop, double height)
    {
        if (_grid is not { } grid || !_showBeatGrid || grid.SampleRate != (int)SampleRate)
        {
            return;
        }

        var beatPixels = grid.SamplesPerBeat / _samplesPerPixel;
        var barPixels = beatPixels * BeatGrid.BeatsPerBar;
        // Every bar, or every 2nd, 4th, 8th... when zoomed far out.
        var barStep = 1;
        while (barPixels * barStep < MinGridSpacing)
        {
            barStep *= 2;
        }

        var beatPen = new Pen(WithOpacity(LineBrush, 0.55), 1);
        var barPen = new Pen(WithOpacity(LabelBrush, 0.45), 1);
        var labelBrush = WithOpacity(LabelBrush, 0.8);
        var labelEvery = barStep;
        while (barPixels * labelEvery < MinBarLabelSpacing)
        {
            labelEvery *= 2;
        }

        var first = Math.Max(0, (int)Math.Floor(grid.BeatPosition(_viewStart)));
        var last = Math.Min(grid.Beats.Count - 1, (int)Math.Ceiling(grid.BeatPosition(_viewStart + ViewLength)));
        for (var i = first; i <= last; i++)
        {
            var x = Math.Round(FrameToX(grid.Beats[i])) + 0.5;
            if (x < 0 || x > width)
            {
                continue;
            }

            if (grid.IsDownbeat(i))
            {
                var bar = grid.BarOf(i);
                if ((bar - 1) % barStep != 0)
                {
                    continue;
                }

                dc.DrawLine(barPen, new Point(x, lanesTop), new Point(x, height));
                if ((bar - 1) % labelEvery == 0)
                {
                    dc.DrawText(Text(bar.ToString(), 9, labelBrush), new Point(x + 3, lanesTop + 1));
                }
            }
            else if (beatPixels >= MinGridSpacing)
            {
                dc.DrawLine(beatPen, new Point(x, lanesTop), new Point(x, height));
            }
        }
    }

    /// <summary>
    /// The selection's length in bars and beats (or minutes and seconds), in a tag at its top (beside it, if the
    /// selection is narrow).
    /// </summary>
    private void DrawLoopLength(DrawingContext dc, double width, double lanesTop)
    {
        _lengthTag = null;
        if (_grid is not { } grid || _selection.IsEmpty || grid.SampleRate != (int)SampleRate)
        {
            return;
        }

        var x0 = FrameToX(_selection.Start);
        var x1 = FrameToX(_selection.End);
        if (x1 < 0 || x0 > width)
        {
            return;
        }

        var length = _lengthInTime ? FormatTime(_selection.Length / (double)SampleRate, 0.01) : grid.Measure(_selection).Text;
        var text = Text(length, 11, SelectedWaveBrush ?? Brushes.White);
        var tagWidth = text.Width + 12;
        var tagHeight = text.Height + 4;
        var left = x1 - x0 >= tagWidth + 8 ? Math.Clamp((x0 + x1 - tagWidth) / 2, Math.Max(4, x0 + 4), Math.Min(width - tagWidth - 4, x1 - tagWidth - 4))
            : x1 + 6 + tagWidth <= width ? x1 + 6 : x0 - tagWidth - 6;
        var tag = new Rect(left, lanesTop + 6, tagWidth, tagHeight);
        dc.DrawRectangle(LaneBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1), tag, 6, 6);
        dc.DrawText(text, new Point(tag.X + 6, tag.Y + 2));
        _lengthTag = tag;
    }

    private bool IsOnLengthTag(Point position) => _lengthTag is { } tag && tag.Contains(position);

    /// <summary>Switches the length tag between bars and beats and minutes and seconds, for a click on it.</summary>
    private void ToggleLengthUnits()
    {
        LengthInTime = !_lengthInTime;
        LengthUnitsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IBrush WithOpacity(IBrush? brush, double opacity) =>
        brush is ISolidColorBrush solid
            ? new SolidColorBrush(solid.Color, solid.Opacity * opacity)
            : new SolidColorBrush(Colors.Gray, opacity);
}
