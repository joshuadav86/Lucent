using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// The editor's main view: a time ruler, a bar with a tab for each clip, then one lane per channel with
/// its waveform. Click to place the cursor, drag to select (Shift extends), the wheel (or Ctrl+wheel, or a
/// pinch on a touchpad or touch screen) zooms around the mouse; Shift+wheel, a sideways swipe, two fingers
/// dragged on a touch screen or a middle-button drag pans, and Alt+wheel makes the waves taller or shorter. Drag a clip's tab to slide the clip along, leaving
/// silence behind it (its edges snap to the other clips' edges, the cursor and the playhead; Ctrl slides it
/// freely); Alt+drag a tab to move the clip in among the others instead. Click a tab to select the clip.
/// The playhead is a separate layer, so moving it never redraws the waveform.
/// </summary>
/// <remarks>
/// Also shows markers (flags in the ruler, which can be clicked or dragged), highlighted problem spots
/// (clipping), and, instead of the waveform, a spectrogram. Alt+drag selects within one channel only.
/// With <see cref="Snap"/> set, the cursor and selection edges snap to it; holding Ctrl places them freely.
/// </remarks>
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
    private double _amplitudeZoom = 1;
    private HwndSource? _hwndSource;

    // Fingers on a touch screen, and what the selection was before the first one came down (two fingers pinch).
    private readonly Dictionary<int, Point> _touches = new();
    private (FrameRange Selection, int Cursor)? _beforeTouch;
    private bool _pinching;
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
    // Where along the audio the tab was pressed; whether the drag puts the clip in among the others (Alt)
    // rather than sliding it; and, while sliding, how far it has gone, the edge it snapped to and how far left it can go.
    private double _pressedFrame;
    private bool _reordering;
    private int _slideOffset;
    private int _slideEarliest;
    private int? _slideSnap;
    private int? _playheadFrame;
    private IReadOnlyList<Marker> _markers = Array.Empty<Marker>();
    private IReadOnlyList<FrameRange> _highlights = Array.Empty<FrameRange>();
    private int? _channel;
    // The marker whose flag was pressed, and where the mouse is while it's dragged.
    private int? _pressedMarker;
    private bool _draggingMarker;
    private double _markerX;
    private EditorView _view = EditorView.Waveform;
    private SpectrogramRenderer.Image?[] _spectrogram = new SpectrogramRenderer.Image?[2];
    private CancellationTokenSource? _spectrogramWork;
    private readonly DispatcherTimer _spectrogramDelay;
    private static readonly Brush HighlightBrush = Frozen(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x4D, 0x4F)));
    private static readonly Brush HighlightEdgeBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4F)));

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

        // WPF has no event for a sideways wheel or two-finger swipe, so it's read from the window's messages.
        Loaded += (_, _) =>
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(OnWindowMessage);
        };
        Unloaded += (_, _) =>
        {
            _hwndSource?.RemoveHook(OnWindowMessage);
            _hwndSource = null;
        };

        // The spectrogram is worked out once the view settles for a moment, not on every step of a scroll.
        _spectrogramDelay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _spectrogramDelay.Tick += (_, _) =>
        {
            _spectrogramDelay.Stop();
            StartSpectrogram();
        };
    }

    /// <summary>A marker's flag was clicked (without dragging).</summary>
    public event EventHandler<int>? MarkerClicked;

    /// <summary>The waves were made taller or shorter.</summary>
    public event EventHandler? AmplitudeZoomChanged;

    /// <summary>A marker's flag was dragged to a new frame.</summary>
    public event EventHandler<(int Marker, int Frame)>? MarkerMoved;

    /// <summary>The view moved or zoomed.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The selection or cursor changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user clicked (without dragging) to put the cursor somewhere.</summary>
    public event EventHandler? CursorPlaced;

    /// <summary>A clip's tab was Alt+dragged and dropped: move clip <c>Clip</c> so it becomes clip number <c>ToIndex</c>.</summary>
    public event EventHandler<(int Clip, int ToIndex)>? ClipMoveRequested;

    /// <summary>A clip's tab was dragged and dropped: slide clip <c>Clip</c> along by <c>Offset</c> frames.</summary>
    public event EventHandler<(int Clip, int Offset)>? ClipSlideRequested;

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

    /// <summary>Moves a frame the user picked to a better one nearby (a zero crossing), or null to leave frames as picked.</summary>
    public Func<int, int>? Snap { get; set; }

    /// <summary>How much silence is just before a clip, so how far left a dragged clip can slide.</summary>
    public Func<int, int>? SilenceBeforeClip { get; set; }

    /// <summary>
    /// The hand tool: when on, dragging anywhere on a clip's waveform slides the clip, as dragging its tab does,
    /// instead of selecting.
    /// </summary>
    public bool HandTool { get; set; }

    /// <summary>The markers to show, in order.</summary>
    public IReadOnlyList<Marker> Markers
    {
        get => _markers;
        set
        {
            _markers = value;
            InvalidateVisual();
        }
    }

    /// <summary>Stretches to point out, such as where the audio clips.</summary>
    public IReadOnlyList<FrameRange> Highlights
    {
        get => _highlights;
        set
        {
            _highlights = value;
            InvalidateVisual();
        }
    }

    /// <summary>The only channel the selection covers (0 is left), or null for all of them.</summary>
    public int? SelectedChannel
    {
        get => _peaks is { } peaks && _channel < peaks.Audio.ChannelCount && peaks.Audio.ChannelCount > 1 ? _channel : null;
        set
        {
            _channel = value;
            InvalidateVisual();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The waveform, or a spectrogram of each channel.</summary>
    public EditorView View
    {
        get => _view;
        set
        {
            _view = value;
            InvalidateVisual();
        }
    }

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

    /// <summary>The highest <see cref="AmplitudeZoom"/>: quiet sounds 36 dB down still fill the lane.</summary>
    public const double MaxAmplitudeZoom = 64;

    /// <summary>How much taller than full scale the waves are drawn (1 = a full-scale peak just fills its lane).</summary>
    public double AmplitudeZoom
    {
        get => _amplitudeZoom;
        set
        {
            var zoom = double.IsFinite(value) ? Math.Clamp(value, 1, MaxAmplitudeZoom) : 1;
            if (Math.Abs(zoom - _amplitudeZoom) < 1e-9)
            {
                return;
            }

            _amplitudeZoom = zoom;
            InvalidateVisual();
            AmplitudeZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

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
            _playheadFrame = value;
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

    /// <summary>Scrolls along by a share of the visible width (positive is later in the sound).</summary>
    public void ScrollBy(double widths)
    {
        _viewStart += widths * ViewLength;
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
        var laneHeight = LaneHeight(height, channels);
        var linePen = new Pen(LineBrush ?? Brushes.Gray, 1);
        linePen.Freeze();
        var spectrogram = _view == EditorView.Spectrogram;

        for (var c = 0; c < channels; c++)
        {
            var top = LaneTop(c, laneHeight);
            dc.DrawRoundedRectangle(LaneBrush, null, new Rect(0, top, width, laneHeight), 6, 6);
        }

        // Selection behind the waveform, across the ruler and every lane it covers (one, with Alt+drag).
        var selected = Rect.Empty;
        var selectedChannel = SelectedChannel;
        var selectionRects = new List<Rect>();
        if (!_selection.IsEmpty)
        {
            var x0 = Math.Max(0, FrameToX(_selection.Start));
            var x1 = Math.Min(width, FrameToX(_selection.End));
            if (x1 > x0 - 1)
            {
                selected = new Rect(x0, 0, Math.Max(1, x1 - x0), height);
                if (selectedChannel is { } only)
                {
                    selectionRects.Add(new Rect(selected.X, 0, selected.Width, lanesTop));
                    selectionRects.Add(new Rect(selected.X, LaneTop(only, laneHeight) - LaneGap / 2, selected.Width, laneHeight + LaneGap));
                }
                else
                {
                    selectionRects.Add(selected);
                }

                if (!spectrogram)
                {
                    foreach (var rect in selectionRects)
                    {
                        dc.DrawRectangle(SelectionBrush, null, rect);
                    }
                }
            }
        }

        DrawRuler(dc, width, linePen);
        DrawClips(dc, width, height, lanesTop);

        for (var c = 0; c < channels; c++)
        {
            var top = LaneTop(c, laneHeight);
            var lane = new Rect(0, top, width, laneHeight);
            if (spectrogram)
            {
                DrawSpectrogram(dc, c, lane);
            }
            else
            {
                var middle = top + laneHeight / 2;
                dc.DrawLine(linePen, new Point(0, Math.Round(middle) + 0.5), new Point(width, Math.Round(middle) + 0.5));
                dc.PushClip(new RectangleGeometry(lane));
                DrawWave(dc, c, top, laneHeight, width, WaveBrush);
                dc.Pop();

                // The selected part is drawn again over it in its own colour.
                var selectedLane = Rect.Intersect(lane, selected);
                if (!selectedLane.IsEmpty && SelectedWaveBrush is not null && (selectedChannel is null || selectedChannel == c))
                {
                    dc.PushClip(new RectangleGeometry(selectedLane));
                    DrawWave(dc, c, top, laneHeight, width, SelectedWaveBrush);
                    dc.Pop();
                }
            }

            var label = channels == 1 ? "Mono" : c == 0 ? "L" : "R";
            if (!spectrogram && _amplitudeZoom > 1.001)
            {
                label += "   " + AmplitudeZoomText(_amplitudeZoom);
            }

            dc.DrawText(Text(label, 10, spectrogram ? SelectedWaveBrush : LabelBrush), new Point(8, top + 4));
        }

        if (spectrogram)
        {
            // Over the picture, which would hide it underneath.
            foreach (var rect in selectionRects)
            {
                dc.DrawRectangle(SelectionBrush, null, rect);
            }
        }

        DrawHighlights(dc, width, lanesTop, height);
        DrawMarkers(dc, width, height);

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

    private static double LaneHeight(double height, int channels) =>
        Math.Max(1, (height - RulerHeight - ClipBarHeight - LaneGap * channels) / channels);

    private static double LaneTop(int channel, double laneHeight) =>
        RulerHeight + ClipBarHeight + LaneGap + channel * (laneHeight + LaneGap);

    /// <summary>The channel whose lane is at a height, or null above the lanes.</summary>
    private int? LaneAt(double y)
    {
        if (_peaks is null || y < RulerHeight + ClipBarHeight)
        {
            return null;
        }

        var channels = _peaks.Audio.ChannelCount;
        var laneHeight = LaneHeight(ActualHeight, channels);
        return Math.Clamp((int)((y - RulerHeight - ClipBarHeight) / (laneHeight + LaneGap)), 0, channels - 1);
    }

    /// <summary>One channel's spectrogram, from the latest picture worked out (stretched to fit until a new one is ready).</summary>
    private void DrawSpectrogram(DrawingContext dc, int channel, Rect lane)
    {
        var image = channel < _spectrogram.Length ? _spectrogram[channel] : null;
        var current = image is not null && ReferenceEquals(image.Audio, _peaks?.Audio) && Math.Abs(image.ViewStart - _viewStart) < 0.5 &&
                      Math.Abs(image.SamplesPerPixel - _samplesPerPixel) < 1e-9 && image.Width == (int)Math.Ceiling(lane.Width) &&
                      image.Height == (int)Math.Ceiling(lane.Height);
        if (!current)
        {
            _spectrogramDelay.Stop();
            _spectrogramDelay.Start();
        }

        dc.PushClip(new RectangleGeometry(lane, 6, 6));
        dc.DrawRectangle(Brushes.Black, null, lane);
        if (image is not null && ReferenceEquals(image.Audio, _peaks?.Audio))
        {
            var x = FrameToX(image.ViewStart);
            var w = image.Width * image.SamplesPerPixel / _samplesPerPixel;
            dc.DrawImage(image.Bitmap, new Rect(x, lane.Top, w, lane.Height));
        }

        // A few frequencies up the side, so the picture can be read.
        var highest = (_peaks?.Audio.SampleRate ?? 44100) / 2.0;
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
        pen.Freeze();
        foreach (var hz in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000, 20000 })
        {
            if (hz >= highest)
            {
                continue;
            }

            var y = Math.Round(lane.Bottom - SpectrogramRenderer.Height(hz, lane.Height, highest)) + 0.5;
            if (y < lane.Top + 16 || y > lane.Bottom - 4)
            {
                continue;
            }

            dc.DrawLine(pen, new Point(0, y), new Point(lane.Width, y));
            var text = hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}";
            dc.DrawText(Text(text, 9, SelectedWaveBrush), new Point(lane.Width - 26, y - 12));
        }

        dc.Pop();
    }

    /// <summary>Works out the spectrogram for the view as it is now, in the background, then redraws.</summary>
    private void StartSpectrogram()
    {
        _spectrogramWork?.Cancel();
        if (_peaks is null || _view != EditorView.Spectrogram || ActualWidth < 1)
        {
            return;
        }

        var audio = _peaks.Audio;
        var channels = audio.ChannelCount;
        var laneHeight = LaneHeight(ActualHeight, channels);
        var width = (int)Math.Ceiling(ActualWidth);
        var height = (int)Math.Ceiling(laneHeight);
        var viewStart = _viewStart;
        var samplesPerPixel = _samplesPerPixel;
        var work = new CancellationTokenSource();
        _spectrogramWork = work;
        _ = Task.Run(() =>
        {
            try
            {
                var images = Enumerable.Range(0, channels)
                    .Select(c => SpectrogramRenderer.Render(audio, c, viewStart, samplesPerPixel, width, height, work.Token)).ToArray();
                Dispatcher.BeginInvoke(() =>
                {
                    if (!work.IsCancellationRequested)
                    {
                        _spectrogram = images;
                        InvalidateVisual();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // A newer view is on its way.
            }
        });
    }

    /// <summary>The problem spots, as red bands over the lanes.</summary>
    private void DrawHighlights(DrawingContext dc, double width, double lanesTop, double height)
    {
        var edge = new Pen(HighlightEdgeBrush, 1);
        edge.Freeze();
        foreach (var range in _highlights)
        {
            var x0 = FrameToX(range.Start);
            var x1 = FrameToX(range.End);
            if (x1 < 0 || x0 > width)
            {
                continue;
            }

            // At least a few pixels wide, so a clip of a few samples can still be seen zoomed out.
            var middle = (x0 + x1) / 2;
            var w = Math.Max(3, x1 - x0);
            dc.DrawRectangle(HighlightBrush, null, new Rect(middle - w / 2, lanesTop, w, height - lanesTop));
            dc.DrawRectangle(HighlightEdgeBrush, null, new Rect(middle - w / 2, lanesTop, w, 3));
        }
    }

    /// <summary>Each marker: a flag with its name in the ruler, and a line down through the lanes.</summary>
    private void DrawMarkers(DrawingContext dc, double width, double height)
    {
        var brush = CursorBrush ?? Brushes.White;
        var line = new Pen(brush, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
        line.Freeze();
        for (var i = 0; i < _markers.Count; i++)
        {
            var frame = _draggingMarker && _pressedMarker == i ? XToFrame(_markerX) : _markers[i].Frame;
            var x = Math.Round(FrameToX(frame)) + 0.5;
            if (x < -200 || x > width)
            {
                continue;
            }

            dc.DrawLine(line, new Point(x, RulerHeight), new Point(x, height));
            var flag = new StreamGeometry();
            using (var g = flag.Open())
            {
                g.BeginFigure(new Point(x, 2), true, true);
                g.LineTo(new Point(x + 9, 2), true, false);
                g.LineTo(new Point(x + 9, RulerHeight - 6), true, false);
                g.LineTo(new Point(x, RulerHeight - 2), true, false);
            }

            flag.Freeze();
            dc.DrawGeometry(brush, null, flag);
            var name = _markers[i].Name.Length > 24 ? _markers[i].Name[..23] + "…" : _markers[i].Name;
            var text = Text(name, 10, SelectedWaveBrush ?? Brushes.White);
            // On a backing, so the ruler's times don't run through it.
            dc.DrawRoundedRectangle(LaneBrush, null, new Rect(x + 11, 3, text.Width + 6, 15), 3, 3);
            dc.DrawText(text, new Point(x + 14, 4));
        }
    }

    /// <summary>The marker whose flag is at a point in the ruler, if any.</summary>
    public int? MarkerAt(Point position)
    {
        if (position.Y > RulerHeight)
        {
            return null;
        }

        for (var i = _markers.Count - 1; i >= 0; i--)
        {
            var x = FrameToX(_markers[i].Frame);
            if (position.X >= x - 3 && position.X <= x + 12)
            {
                return i;
            }
        }

        return null;
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
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

        var clip = clips[dragged];
        var ghostWidth = Math.Max(4, clip.Length / _samplesPerPixel);
        if (!_reordering)
        {
            // The clip where it will land, and a bright line on the edge it snapped to.
            dc.PushOpacity(0.8);
            dc.DrawRoundedRectangle(SelectionBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1),
                new Rect(FrameToX(clip.Start + _slideOffset), tabTop, ghostWidth, height - tabTop), 4, 4);
            dc.Pop();
            if (_slideSnap is { } snapped)
            {
                var snapX = Math.Round(FrameToX(snapped)) + 0.5;
                dc.DrawLine(new Pen(CursorBrush ?? Brushes.White, 2), new Point(snapX, RulerHeight), new Point(snapX, height));
            }

            return;
        }

        // The dragged clip follows the mouse, and a bright line shows where it will land.
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

    /// <summary>
    /// How far a clip slides when its tab is dragged to an x position: as far as the mouse moved, snapped (unless
    /// Ctrl is held) so one of its edges lines up with another clip's edge, the cursor or the playhead when it's
    /// close, and no further left than the silence before it. Also says which edge it snapped to.
    /// </summary>
    private (int Offset, int? Snap) SlideOffset(int dragged, double x)
    {
        var clip = Clips[dragged];
        var moved = XToFrame(x) - _pressedFrame;
        var offset = moved;
        int? snap = null;
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var best = 8 * _samplesPerPixel;
            var targets = _splits.Prepend(0).Append(Frames).Append(_cursor)
                .Where(t => t != clip.Start && t != clip.End);
            if (_playheadFrame is { } playhead)
            {
                targets = targets.Append(playhead);
            }

            foreach (var target in targets)
            {
                foreach (var edge in new[] { clip.Start, clip.End })
                {
                    var distance = Math.Abs(target - edge - moved);
                    if (distance < best)
                    {
                        best = distance;
                        snap = target;
                        offset = target - edge;
                    }
                }
            }
        }

        var frames = (int)Math.Round(offset);
        if (frames < _slideEarliest)
        {
            return (_slideEarliest, null);
        }

        return (frames, snap);
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
                    var yHigh = middle - Math.Clamp(max * _amplitudeZoom, -1, 1) * half;
                    var yLow = middle - Math.Clamp(min * _amplitudeZoom, -1, 1) * half;
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

                g.BeginFigure(new Point(FrameToX(first), middle - SampleHeight(samples[first]) * half), false, false);
                for (var i = first + 1; i <= last; i++)
                {
                    g.LineTo(new Point(FrameToX(i), middle - SampleHeight(samples[i]) * half), true, false);
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
                    dc.DrawEllipse(brush, null, new Point(FrameToX(i), middle - SampleHeight(samples[i]) * half), 2, 2);
                }
            }
        }
    }

    /// <summary>A sample's height in the lane (1 is the lane's edge), a little past it for overs, the rest clipped off.</summary>
    private double SampleHeight(float sample) => Math.Clamp(sample * _amplitudeZoom, -1.5, 1.5);

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

        if (MarkerAt(position) is { } marker)
        {
            // A marker's flag: dragging moves the marker, a click goes to it.
            _pressedMarker = marker;
            _draggingMarker = false;
            _markerX = position.X;
            _anchor = null;
            e.Handled = true;
            return;
        }

        if (IsOnClipBar(position) || (HandTool && position.Y >= RulerHeight))
        {
            // A clip's tab (or, with the hand tool, its waveform): dragging slides the clip (Alt+dragging moves it in
            // among the others), a click selects it.
            _draggedClip = ClipIndexAt(frame);
            _draggingClip = false;
            _dragX = position.X;
            _pressedFrame = XToFrame(position.X);
            _reordering = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
            _slideOffset = 0;
            _slideSnap = null;
            _slideEarliest = -(SilenceBeforeClip?.Invoke(_draggedClip.Value) ?? 0);
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
            // Alt+drag in a lane selects in that channel only.
            _channel = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) ? LaneAt(position.Y) : null;
            _anchor = Snapped(frame);
        }

        e.Handled = true;
    }

    /// <summary>A frame moved to the snap point near it, unless Ctrl is held.</summary>
    private int Snapped(int frame) =>
        Snap is { } snap && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) ? Math.Clamp(snap(frame), 0, Frames) : frame;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (!IsMouseCaptured && _panFrom is null)
        {
            Cursor = MarkerAt(position) is not null ? Cursors.SizeWE
                : (IsOnClipBar(position) || (HandTool && position.Y >= RulerHeight)) && _peaks is not null ? Cursors.Hand
                : Cursors.IBeam;
        }

        if (_pressedMarker is not null && IsMouseCaptured)
        {
            if (!_draggingMarker && Math.Abs(position.X - _pressedAt.X) < DragThreshold)
            {
                return;
            }

            _draggingMarker = true;
            _markerX = Math.Clamp(position.X, FrameToX(0), FrameToX(Frames));
            InvalidateVisual();
            return;
        }

        if (_draggedClip is not null && IsMouseCaptured)
        {
            if (!_draggingClip && Math.Abs(position.X - _pressedAt.X) < DragThreshold)
            {
                return;
            }

            // A song in one piece has nowhere to move to among the others, but can still slide along.
            _draggingClip = !_reordering || Clips.Count > 1;
            _dragX = position.X;
            if (position.X < 0 || position.X > ActualWidth)
            {
                var overshoot = position.X < 0 ? position.X : position.X - ActualWidth;
                _viewStart += overshoot * _samplesPerPixel * 0.5;
                ClampView();
                RaiseViewChanged();
            }

            if (!_reordering && _draggedClip is { } slid && slid < Clips.Count)
            {
                (_slideOffset, _slideSnap) = SlideOffset(slid, position.X);
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
        var pressedMarker = _pressedMarker;
        var draggedMarker = _draggingMarker;
        ReleaseMouseCapture();
        if (pressedMarker is { } marker)
        {
            if (draggedMarker)
            {
                MarkerMoved?.Invoke(this, (marker, Snapped(FrameAt(e.GetPosition(this).X))));
            }
            else
            {
                MarkerClicked?.Invoke(this, marker);
            }

            e.Handled = true;
            return;
        }

        if (pressedClip is { } clip)
        {
            var clips = Clips;
            if (!dragged && clip < clips.Count)
            {
                Selection = clips[clip];
            }
            else if (dragged && !_reordering && clip < clips.Count)
            {
                var (offset, _) = SlideOffset(clip, e.GetPosition(this).X);
                if (offset != 0)
                {
                    ClipSlideRequested?.Invoke(this, (clip, offset));
                }
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

    /// <summary>How much the waves are magnified, as a gain in decibels ("+12 dB").</summary>
    public static string AmplitudeZoomText(double zoom) =>
        "+" + (20 * Math.Log10(zoom)).ToString("0.#", CultureInfo.CurrentCulture) + " dB";

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        // A touchpad scrolls in small steps, a mouse wheel in notches of 120. A touchpad pinch comes as Ctrl+wheel.
        var notches = e.Delta / 120.0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            AmplitudeZoom *= Math.Pow(1.25, notches);
        }
        else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            ScrollBy(-notches * 0.15);
        }
        else
        {
            Zoom(Math.Pow(0.8, notches), e.GetPosition(this).X);
        }

        e.Handled = true;
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_MOUSEHWHEEL = 0x020E;
        if (msg == WM_MOUSEHWHEEL && IsMouseOver && _peaks is not null)
        {
            // Tilting the wheel or swiping right moves on through the sound.
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            ScrollBy(delta / 120.0 * 0.15);
            handled = true;
        }

        return IntPtr.Zero;
    }

    // ---- Touch: one finger acts as the mouse; a second one pinches to zoom and drags to scroll ----

    protected override void OnTouchDown(TouchEventArgs e)
    {
        base.OnTouchDown(e);
        if (_touches.Count == 0)
        {
            _beforeTouch = (_selection, _cursor);
        }

        _touches[e.TouchDevice.Id] = e.GetTouchPoint(this).Position;
        if (_touches.Count < 2)
        {
            // Left unhandled, so the first finger clicks and drags like the mouse.
            return;
        }

        CaptureTouch(e.TouchDevice);
        if (!_pinching)
        {
            // The first finger had started a click or a drag; it's a pinch instead, so put things back.
            _pinching = true;
            if (IsMouseCaptured)
            {
                ReleaseMouseCapture();
            }

            _anchor = null;
            _selecting = false;
            if (_beforeTouch is { } before && (before.Selection != _selection || before.Cursor != _cursor))
            {
                _selection = before.Selection;
                _cursor = before.Cursor;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        base.OnTouchMove(e);
        if (!_touches.ContainsKey(e.TouchDevice.Id))
        {
            return;
        }

        if (!_pinching || _touches.Count < 2)
        {
            _touches[e.TouchDevice.Id] = e.GetTouchPoint(this).Position;
            return;
        }

        var (oldA, oldB) = FirstTwoTouches();
        _touches[e.TouchDevice.Id] = e.GetTouchPoint(this).Position;
        var (newA, newB) = FirstTwoTouches();
        var oldSpread = Math.Abs(oldA.X - oldB.X);
        var newSpread = Math.Abs(newA.X - newB.X);
        var oldMiddle = (oldA.X + oldB.X) / 2;
        var newMiddle = (newA.X + newB.X) / 2;

        // Keep the sound under the fingers: zoom by how far apart they moved, around their midpoint, then follow it.
        if (oldSpread > 20 && newSpread > 20)
        {
            Zoom(oldSpread / newSpread, oldMiddle);
        }

        ScrollBy(-(newMiddle - oldMiddle) / Math.Max(1, ActualWidth));
        e.Handled = true;
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        base.OnTouchUp(e);
        _touches.Remove(e.TouchDevice.Id);
        if (_pinching)
        {
            ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        if (_touches.Count == 0)
        {
            _pinching = false;
            _beforeTouch = null;
        }
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        base.OnLostTouchCapture(e);
        if (_pinching)
        {
            _touches.Remove(e.TouchDevice.Id);
        }
    }

    private (Point A, Point B) FirstTwoTouches()
    {
        using var touches = _touches.Values.GetEnumerator();
        touches.MoveNext();
        var a = touches.Current;
        touches.MoveNext();
        return (a, touches.Current);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        // Esc, or another window taking the mouse, drops a clip or marker back where it was.
        if (_draggedClip is not null || _pressedMarker is not null)
        {
            _draggedClip = null;
            _draggingClip = false;
            _pressedMarker = null;
            _draggingMarker = false;
            InvalidateVisual();
        }
    }

    /// <summary>True while a clip's tab or a marker is being dragged.</summary>
    public bool IsDraggingClip => _draggingClip || _draggingMarker;

    /// <summary>Stops dragging a clip or marker, leaving it where it was.</summary>
    public void CancelClipDrag()
    {
        if ((_draggedClip is not null || _pressedMarker is not null) && IsMouseCaptured)
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
        frame = Snapped(frame);
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
