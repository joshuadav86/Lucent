using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// The editor's main view: a time ruler, a bar with a tab for each clip, then one lane per channel with
/// its waveform. Click to place the cursor, drag to select (Shift extends), the wheel zooms around the
/// mouse (Shift+wheel or a middle-button drag pans). Drag a clip's tab to move the clip; click it to
/// select the clip. The playhead is a separate layer, so moving it never redraws the waveform.
/// </summary>
public sealed class WaveformView : FrameworkElement
{
    public const double RulerHeight = 22;
    private const double ClipBarHeight = 20;
    private const double LaneGap = 6;
    private const double DragThreshold = 3;

    public static readonly DependencyProperty WaveBrushProperty = Register(nameof(WaveBrush));
    public static readonly DependencyProperty SelectedWaveBrushProperty = Register(nameof(SelectedWaveBrush));
    public static readonly DependencyProperty LaneBrushProperty = Register(nameof(LaneBrush));
    public static readonly DependencyProperty SelectionBrushProperty = Register(nameof(SelectionBrush));
    public static readonly DependencyProperty LineBrushProperty = Register(nameof(LineBrush));
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush));
    public static readonly DependencyProperty CursorBrushProperty = Register(nameof(CursorBrush));
    public static readonly DependencyProperty PlayheadBrushProperty = DependencyProperty.Register(
        nameof(PlayheadBrush), typeof(Brush), typeof(WaveformView),
        new FrameworkPropertyMetadata(Brushes.White, (d, _) => ((WaveformView)d).DrawPlayhead()));

    private readonly DrawingVisual _playhead = new();
    private readonly TranslateTransform _playheadOffset = new();
    private PeakCache? _peaks;
    private double _viewStart;
    private double _samplesPerPixel = 1;
    private FrameRange _selection;
    private int _cursor;
    private int? _anchor;
    private Point _pressedAt;
    private bool _selecting;
    private Point? _panFrom;
    private double _panStartView;
    private IReadOnlyList<int> _splits = Array.Empty<int>();
    // The clip whose tab was pressed, and where the mouse is while it's dragged.
    private int? _draggedClip;
    private double _dragX;
    private bool _draggingClip;

    public WaveformView()
    {
        _playhead.Transform = _playheadOffset;
        AddVisualChild(_playhead);
        Cursor = Cursors.IBeam;
        ClipToBounds = true;
        Focusable = false;
        SizeChanged += (_, _) =>
        {
            ClampView();
            DrawPlayhead();
            RaiseViewChanged();
        };
    }

    /// <summary>The view moved or zoomed.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The selection or cursor changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user clicked (without dragging) to put the cursor somewhere.</summary>
    public event EventHandler? CursorPlaced;

    /// <summary>A clip's tab was dragged and dropped: move clip <c>Clip</c> so it becomes clip number <c>ToIndex</c>.</summary>
    public event EventHandler<(int Clip, int ToIndex)>? ClipMoveRequested;

    public Brush? WaveBrush { get => (Brush?)GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    /// <summary>The waveform inside the selection, so what's selected stands out from the rest.</summary>
    public Brush? SelectedWaveBrush { get => (Brush?)GetValue(SelectedWaveBrushProperty); set => SetValue(SelectedWaveBrushProperty, value); }

    public Brush? LaneBrush { get => (Brush?)GetValue(LaneBrushProperty); set => SetValue(LaneBrushProperty, value); }

    public Brush? SelectionBrush { get => (Brush?)GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }

    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public Brush? CursorBrush { get => (Brush?)GetValue(CursorBrushProperty); set => SetValue(CursorBrushProperty, value); }

    public Brush? PlayheadBrush { get => (Brush?)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

    /// <summary>The audio to show, through its peaks. Setting it keeps the view and selection where they can.</summary>
    public PeakCache? Peaks
    {
        get => _peaks;
        set
        {
            var first = _peaks is null;
            _peaks = value;
            if (first)
            {
                ZoomToFit();
            }

            Selection = _selection;
            ClampView();
            InvalidateVisual();
            RaiseViewChanged();
        }
    }

    public int Frames => _peaks?.Audio.Frames ?? 0;

    /// <summary>Where the audio is split into clips, in order.</summary>
    public IReadOnlyList<int> Splits
    {
        get => _splits;
        set
        {
            _splits = value;
            InvalidateVisual();
        }
    }

    /// <summary>The clips between the splits, in order.</summary>
    public IReadOnlyList<FrameRange> Clips
    {
        get
        {
            var clips = new List<FrameRange>(_splits.Count + 1);
            var start = 0;
            foreach (var split in _splits.Where(s => s > 0 && s < Frames).Append(Frames))
            {
                clips.Add(new FrameRange(start, split - start));
                start = split;
            }

            return clips;
        }
    }

    public int SampleRate => _peaks?.Audio.SampleRate ?? 44100;

    /// <summary>The frame at the left edge.</summary>
    public double ViewStart => _viewStart;

    /// <summary>How many frames are visible.</summary>
    public double ViewLength => _samplesPerPixel * Math.Max(1, ActualWidth);

    public double SamplesPerPixel => _samplesPerPixel;

    /// <summary>The selected range; empty when nothing is selected.</summary>
    public FrameRange Selection
    {
        get => _selection;
        set
        {
            var start = Math.Clamp(value.Start, 0, Frames);
            var end = Math.Clamp(value.End, start, Frames);
            _anchor = null;
            _selection = new FrameRange(start, end - start);
            if (!_selection.IsEmpty)
            {
                _cursor = _selection.Start;
            }

            _cursor = Math.Clamp(_cursor, 0, Frames);
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Where playback starts when nothing is selected.</summary>
    public int CursorFrame
    {
        get => _cursor;
        set
        {
            _cursor = Math.Clamp(value, 0, Frames);
            _anchor = null;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Where the playhead is drawn, or null to hide it.</summary>
    public int? PlayheadFrame
    {
        set
        {
            _playhead.Opacity = value is null ? 0 : 1;
            if (value is { } frame)
            {
                _playheadOffset.X = Math.Round(FrameToX(frame)) + 0.5;
            }
        }
    }

    public void SelectAll() => Selection = new FrameRange(0, Frames);

    public void ClearSelection()
    {
        _anchor = null;
        Selection = new FrameRange(_cursor, 0);
    }

    public double FrameToX(double frame) => (frame - _viewStart) / _samplesPerPixel;

    public double XToFrame(double x) => _viewStart + x * _samplesPerPixel;

    /// <summary>Shows the whole song.</summary>
    public void ZoomToFit()
    {
        _samplesPerPixel = MaxSamplesPerPixel;
        _viewStart = 0;
        ClampView();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Zooms in (factor below 1) or out (above 1), keeping the frame under <paramref name="x"/> still.</summary>
    public void Zoom(double factor, double? x = null)
    {
        var anchorX = x ?? ActualWidth / 2;
        var anchorFrame = XToFrame(anchorX);
        _samplesPerPixel = Math.Clamp(_samplesPerPixel * factor, MinSamplesPerPixel, MaxSamplesPerPixel);
        _viewStart = anchorFrame - anchorX * _samplesPerPixel;
        ClampView();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Scrolls so <paramref name="frame"/> is at the given fraction of the width (0 = left edge).</summary>
    public void ScrollTo(double frame, double atFraction = 0.5)
    {
        _viewStart = frame - ViewLength * atFraction;
        ClampView();
        InvalidateVisual();
        RaiseViewChanged();
    }

    /// <summary>Scrolls just enough to bring a frame into view, paging forward like a DAW during playback.</summary>
    public void KeepInView(int frame)
    {
        if (frame < _viewStart || frame > _viewStart + ViewLength * 0.98)
        {
            ScrollTo(frame, 0.05);
        }
    }

    private double MinSamplesPerPixel => 1.0 / 32;

    private double MaxSamplesPerPixel => Math.Max(MinSamplesPerPixel, Frames / Math.Max(1, ActualWidth));

    private void ClampView()
    {
        _samplesPerPixel = Math.Clamp(_samplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, Frames - ViewLength));
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    // ---- Drawing ----

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _playhead;

    private void DrawPlayhead()
    {
        using var dc = _playhead.RenderOpen();
        var pen = new Pen(PlayheadBrush ?? Brushes.White, 1.5);
        dc.DrawLine(pen, new Point(0, 0), new Point(0, Math.Max(0, ActualHeight)));
        // A small triangle in the ruler marks the playhead clearly.
        var marker = new StreamGeometry();
        using (var g = marker.Open())
        {
            g.BeginFigure(new Point(-5, 0), true, true);
            g.LineTo(new Point(5, 0), true, false);
            g.LineTo(new Point(0, 7), true, false);
        }

        marker.Freeze();
        dc.DrawGeometry(PlayheadBrush ?? Brushes.White, null, marker);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (_peaks is null || width <= 0 || height <= RulerHeight)
        {
            return;
        }

        var channels = _peaks.Audio.ChannelCount;
        var lanesTop = RulerHeight + ClipBarHeight;
        var laneHeight = (height - lanesTop - LaneGap * channels) / channels;
        var linePen = new Pen(LineBrush ?? Brushes.Gray, 1);
        linePen.Freeze();

        for (var c = 0; c < channels; c++)
        {
            var top = lanesTop + LaneGap + c * (laneHeight + LaneGap);
            dc.DrawRoundedRectangle(LaneBrush, null, new Rect(0, top, width, laneHeight), 6, 6);
        }

        // Selection behind the waveform, across the ruler and every lane.
        var selected = Rect.Empty;
        if (!_selection.IsEmpty)
        {
            var x0 = Math.Max(0, FrameToX(_selection.Start));
            var x1 = Math.Min(width, FrameToX(_selection.End));
            if (x1 > x0 - 1)
            {
                selected = new Rect(x0, 0, Math.Max(1, x1 - x0), height);
                dc.DrawRectangle(SelectionBrush, null, selected);
            }
        }

        DrawRuler(dc, width, linePen);
        DrawClips(dc, width, height, lanesTop);

        for (var c = 0; c < channels; c++)
        {
            var top = lanesTop + LaneGap + c * (laneHeight + LaneGap);
            var lane = new Rect(0, top, width, laneHeight);
            var middle = top + laneHeight / 2;
            dc.DrawLine(linePen, new Point(0, Math.Round(middle) + 0.5), new Point(width, Math.Round(middle) + 0.5));
            dc.PushClip(new RectangleGeometry(lane));
            DrawWave(dc, c, top, laneHeight, width, WaveBrush);
            dc.Pop();

            // The selected part is drawn again over it in its own colour.
            var selectedLane = Rect.Intersect(lane, selected);
            if (!selectedLane.IsEmpty && SelectedWaveBrush is not null)
            {
                dc.PushClip(new RectangleGeometry(selectedLane));
                DrawWave(dc, c, top, laneHeight, width, SelectedWaveBrush);
                dc.Pop();
            }

            var label = channels == 1 ? "Mono" : c == 0 ? "L" : "R";
            dc.DrawText(Text(label, 10, LabelBrush), new Point(8, top + 4));
        }

        // The cursor, when nothing is selected.
        if (_selection.IsEmpty)
        {
            var x = Math.Round(FrameToX(_cursor)) + 0.5;
            if (x >= 0 && x <= width)
            {
                var pen = new Pen(CursorBrush ?? Brushes.White, 1);
                dc.DrawLine(pen, new Point(x, lanesTop), new Point(x, height));
            }
        }
        else
        {
            var pen = new Pen(SelectedWaveBrush ?? CursorBrush ?? Brushes.White, 1);
            foreach (var edge in new[] { _selection.Start, _selection.End })
            {
                var x = Math.Round(FrameToX(edge)) + 0.5;
                if (x >= 0 && x <= width)
                {
                    dc.DrawLine(pen, new Point(x, 0), new Point(x, height));
                }
            }
        }
    }

    /// <summary>The clip tabs under the ruler, the splits across the lanes, and the clip being dragged.</summary>
    private void DrawClips(DrawingContext dc, double width, double height, double lanesTop)
    {
        var clips = Clips;
        var tabTop = RulerHeight + 2;
        var tabHeight = ClipBarHeight - 3;
        for (var i = 0; i < clips.Count; i++)
        {
            var x0 = Math.Max(-8, FrameToX(clips[i].Start)) + 1;
            var x1 = Math.Min(width + 8, FrameToX(clips[i].End)) - 1;
            if (x1 <= 0 || x0 >= width || x1 - x0 < 2)
            {
                continue;
            }

            var tab = new Rect(x0, tabTop, x1 - x0, tabHeight);
            dc.PushOpacity(_draggingClip && _draggedClip == i ? 0.35 : 1);
            dc.DrawRoundedRectangle(LaneBrush, null, tab, 4, 4);
            var label = clips.Count == 1
                ? "Press S to split at the cursor"
                : $"Clip {i + 1}  ·  {FormatTime(clips[i].Length / (double)SampleRate, 0.1)}";
            dc.PushClip(new RectangleGeometry(tab));
            dc.DrawText(Text(label, 10, LabelBrush), new Point(Math.Max(x0, 0) + 6, tabTop + 2));
            dc.Pop();
            dc.Pop();
        }

        var splitPen = new Pen(LabelBrush ?? Brushes.Gray, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        splitPen.Freeze();
        foreach (var split in _splits)
        {
            var x = Math.Round(FrameToX(split)) + 0.5;
            if (x >= 0 && x <= width)
            {
                dc.DrawLine(splitPen, new Point(x, RulerHeight), new Point(x, height));
            }
        }

        if (!_draggingClip || _draggedClip is not { } dragged || dragged >= clips.Count)
        {
            return;
        }

        // The dragged clip follows the mouse, and a bright line shows where it will land.
        var clip = clips[dragged];
        var ghostWidth = Math.Max(4, clip.Length / _samplesPerPixel);
        var ghostX = _dragX - Math.Min(ghostWidth, Math.Max(0, _pressedAt.X - FrameToX(clip.Start)));
        dc.PushOpacity(0.8);
        dc.DrawRoundedRectangle(SelectionBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1),
            new Rect(ghostX, tabTop, ghostWidth, height - tabTop), 4, 4);
        dc.Pop();
        var toIndex = DropIndex(dragged, XToFrame(_dragX));
        if (toIndex != dragged)
        {
            var others = clips.Where((_, i) => i != dragged).ToList();
            // The gap it drops into, in today's layout: the end of the clip that will be before it.
            var frame = toIndex == 0 ? 0 : others[toIndex - 1].End;
            var x = Math.Round(FrameToX(frame)) + 0.5;
            dc.DrawLine(new Pen(CursorBrush ?? Brushes.White, 3), new Point(x, RulerHeight), new Point(x, height));
        }
    }

    /// <summary>Which clip number a dragged clip becomes when dropped at a frame: after every other clip whose middle it's past.</summary>
    private int DropIndex(int dragged, double frame)
    {
        var clips = Clips;
        var index = 0;
        for (var i = 0; i < clips.Count; i++)
        {
            if (i != dragged && frame > clips[i].Start + clips[i].Length / 2.0)
            {
                index++;
            }
        }

        return index;
    }

    private void DrawWave(DrawingContext dc, int channel, double top, double laneHeight, double width, Brush? brush)
    {
        var half = laneHeight / 2 - 2;
        var middle = top + laneHeight / 2;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            if (_samplesPerPixel >= 1)
            {
                // One column per pixel, from each column's lowest to highest sample: top edge left to right, bottom edge back.
                var columns = (int)Math.Ceiling(width);
                var lastColumn = (int)Math.Min(columns, Math.Ceiling(FrameToX(Frames)));
                if (lastColumn <= 0)
                {
                    return;
                }

                var highs = new Point[lastColumn];
                var lows = new Point[lastColumn];
                for (var x = 0; x < lastColumn; x++)
                {
                    var (min, max) = _peaks!.Range(channel, XToFrame(x), XToFrame(x + 1));
                    var yHigh = middle - Math.Clamp(max, -1, 1) * half;
                    var yLow = middle - Math.Clamp(min, -1, 1) * half;
                    // At least a pixel tall, so quiet passages still show as a line.
                    if (yLow - yHigh < 1)
                    {
                        var centre = (yLow + yHigh) / 2;
                        yHigh = centre - 0.5;
                        yLow = centre + 0.5;
                    }

                    highs[x] = new Point(x, yHigh);
                    lows[x] = new Point(x + 1, yLow);
                }

                g.BeginFigure(new Point(0, highs[0].Y), true, true);
                for (var x = 0; x < lastColumn; x++)
                {
                    g.LineTo(highs[x], false, false);
                    g.LineTo(new Point(x + 1, highs[x].Y), false, false);
                }

                for (var x = lastColumn - 1; x >= 0; x--)
                {
                    g.LineTo(lows[x], false, false);
                    g.LineTo(new Point(x, lows[x].Y), false, false);
                }
            }
            else
            {
                // Zoomed in past single samples: join the samples with lines.
                var samples = _peaks!.Audio.Channels[channel];
                var first = Math.Max(0, (int)Math.Floor(_viewStart) - 1);
                var last = Math.Min(samples.Length - 1, (int)Math.Ceiling(XToFrame(width)) + 1);
                if (last <= first)
                {
                    return;
                }

                g.BeginFigure(new Point(FrameToX(first), middle - samples[first] * half), false, false);
                for (var i = first + 1; i <= last; i++)
                {
                    g.LineTo(new Point(FrameToX(i), middle - Math.Clamp(samples[i], -1.5f, 1.5f) * half), true, false);
                }
            }
        }

        geometry.Freeze();
        if (_samplesPerPixel >= 1)
        {
            dc.DrawGeometry(brush, null, geometry);
        }
        else
        {
            dc.DrawGeometry(null, new Pen(brush, 1.5), geometry);
            if (1 / _samplesPerPixel >= 8)
            {
                // Far enough in to see individual samples as dots.
                var samples = _peaks!.Audio.Channels[channel];
                var first = Math.Max(0, (int)Math.Floor(_viewStart));
                var last = Math.Min(samples.Length - 1, (int)Math.Ceiling(XToFrame(width)));
                for (var i = first; i <= last; i++)
                {
                    dc.DrawEllipse(brush, null, new Point(FrameToX(i), middle - Math.Clamp(samples[i], -1.5f, 1.5f) * half), 2, 2);
                }
            }
        }
    }

    private void DrawRuler(DrawingContext dc, double width, Pen linePen)
    {
        dc.DrawLine(linePen, new Point(0, RulerHeight - 0.5), new Point(width, RulerHeight - 0.5));
        var secondsPerPixel = _samplesPerPixel / SampleRate;
        double[] steps = { 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600 };
        var step = steps.FirstOrDefault(s => s / secondsPerPixel >= 80, 1200);
        var startSecond = Math.Floor(_viewStart / SampleRate / step) * step;
        var endSecond = (_viewStart + ViewLength) / SampleRate;
        for (var t = startSecond; t <= endSecond + step; t += step)
        {
            var x = Math.Round(FrameToX(t * SampleRate)) + 0.5;
            if (x < -60 || x > width)
            {
                continue;
            }

            dc.DrawLine(linePen, new Point(x, RulerHeight - 8), new Point(x, RulerHeight));
            // Minor ticks between labels.
            for (var m = 1; m < 5; m++)
            {
                var minor = Math.Round(FrameToX((t + step * m / 5) * SampleRate)) + 0.5;
                dc.DrawLine(linePen, new Point(minor, RulerHeight - 4), new Point(minor, RulerHeight));
            }

            dc.DrawText(Text(FormatTime(t, step), 10, LabelBrush), new Point(x + 4, 3));
        }
    }

    /// <summary>"1:05", or "1:05.250" when zoomed in far enough for fractions to matter.</summary>
    public static string FormatTime(double seconds, double precision = 0.001)
    {
        seconds = Math.Max(0, seconds);
        var minutes = (int)(seconds / 60);
        var rest = seconds - minutes * 60;
        var format = precision >= 1 ? "00" : precision >= 0.1 ? "00.0" : precision >= 0.01 ? "00.00" : "00.000";
        var text = rest.ToString(format, CultureInfo.CurrentCulture);
        // Rounding can reach 60.0; carry it into the minutes.
        if (text.StartsWith("60", StringComparison.Ordinal))
        {
            minutes++;
            text = 0.0.ToString(format, CultureInfo.CurrentCulture);
        }

        return $"{minutes}:{text}";
    }

    private FormattedText Text(string text, double size, Brush? brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)(TryFindResource("UiFont") ?? new FontFamily("Segoe UI")), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            size, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    // ---- Mouse ----

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_peaks is null)
        {
            return;
        }

        var position = e.GetPosition(this);
        var frame = FrameAt(position.X);
        CaptureMouse();
        _pressedAt = position;
        _selecting = false;

        if (IsOnClipBar(position))
        {
            // A clip's tab: dragging moves the clip, a click selects it.
            _draggedClip = ClipIndexAt(frame);
            _draggingClip = false;
            _dragX = position.X;
            _anchor = null;
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Extend from the end of the selection (or the cursor) furthest from the click.
            _anchor ??= _selection.IsEmpty
                ? _cursor
                : Math.Abs(frame - _selection.Start) > Math.Abs(frame - _selection.End) ? _selection.Start : _selection.End;
            _selecting = true;
            SetSelectionTo(frame);
        }
        else
        {
            _anchor = frame;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (!IsMouseCaptured && _panFrom is null)
        {
            Cursor = IsOnClipBar(position) && Clips.Count > 1 ? Cursors.SizeWE : Cursors.IBeam;
        }

        if (_draggedClip is not null && IsMouseCaptured)
        {
            if (!_draggingClip && Math.Abs(position.X - _pressedAt.X) < DragThreshold)
            {
                return;
            }

            // A song in one piece has nowhere to move to.
            _draggingClip = Clips.Count > 1;
            _dragX = position.X;
            if (position.X < 0 || position.X > ActualWidth)
            {
                var overshoot = position.X < 0 ? position.X : position.X - ActualWidth;
                _viewStart += overshoot * _samplesPerPixel * 0.5;
                ClampView();
                RaiseViewChanged();
            }

            InvalidateVisual();
            return;
        }

        if (_panFrom is { } from)
        {
            _viewStart = _panStartView - (position.X - from.X) * _samplesPerPixel;
            ClampView();
            InvalidateVisual();
            RaiseViewChanged();
            return;
        }

        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed || _anchor is null)
        {
            return;
        }

        if (!_selecting && Math.Abs(position.X - _pressedAt.X) < DragThreshold)
        {
            return;
        }

        _selecting = true;
        // Dragging past either edge scrolls the view along.
        if (position.X < 0 || position.X > ActualWidth)
        {
            var overshoot = position.X < 0 ? position.X : position.X - ActualWidth;
            _viewStart += overshoot * _samplesPerPixel * 0.5;
            ClampView();
            RaiseViewChanged();
        }

        SetSelectionTo(FrameAt(position.X));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
        {
            return;
        }

        // Letting go of the mouse forgets the drag, so read it first.
        var pressedClip = _draggedClip;
        var dragged = _draggingClip;
        ReleaseMouseCapture();
        if (pressedClip is { } clip)
        {
            var clips = Clips;
            if (!dragged && clip < clips.Count)
            {
                Selection = clips[clip];
            }
            else if (dragged && DropIndex(clip, XToFrame(e.GetPosition(this).X)) is var toIndex && toIndex != clip)
            {
                ClipMoveRequested?.Invoke(this, (clip, toIndex));
            }

            e.Handled = true;
            return;
        }

        if (!_selecting && _anchor is { } frame)
        {
            // A plain click: drop the selection and put the cursor here.
            _selection = new FrameRange(frame, 0);
            _cursor = frame;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            CursorPlaced?.Invoke(this, EventArgs.Empty);
        }

        _selecting = false;
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton == MouseButton.Middle)
        {
            _panFrom = e.GetPosition(this);
            _panStartView = _viewStart;
            CaptureMouse();
            Cursor = Cursors.ScrollWE;
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton == MouseButton.Middle && _panFrom is not null)
        {
            _panFrom = null;
            ReleaseMouseCapture();
            Cursor = Cursors.IBeam;
            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var notches = e.Delta / 120.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _viewStart -= notches * ViewLength * 0.15;
            ClampView();
            InvalidateVisual();
            RaiseViewChanged();
        }
        else
        {
            Zoom(Math.Pow(0.8, notches), e.GetPosition(this).X);
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Esc, or another window taking the mouse, drops a clip back where it was.
        if (_draggedClip is not null)
        {
            _draggedClip = null;
            _draggingClip = false;
            InvalidateVisual();
        }
    }

    /// <summary>True while a clip's tab is being dragged.</summary>
    public bool IsDraggingClip => _draggingClip;

    /// <summary>Stops dragging a clip, leaving it where it was.</summary>
    public void CancelClipDrag()
    {
        if (_draggedClip is not null && IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }
    }

    /// <summary>The frame under an x position, inside the audio.</summary>
    public int FrameAt(double x) => (int)Math.Clamp(Math.Round(XToFrame(x)), 0, Frames);

    /// <summary>The number of the clip a frame is in (a frame on a split belongs to the clip after it).</summary>
    public int ClipIndexAt(int frame) => _splits.Count(split => split <= frame);

    private static bool IsOnClipBar(Point position) =>
        position.Y >= RulerHeight && position.Y < RulerHeight + ClipBarHeight;

    private void SetSelectionTo(int frame)
    {
        var anchor = _anchor ?? frame;
        _selection = new FrameRange(Math.Min(anchor, frame), Math.Abs(frame - anchor));
        _cursor = _selection.Start;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(WaveformView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
