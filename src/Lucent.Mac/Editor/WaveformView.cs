using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using Lucent.Editor;
using Lucent.Settings;

namespace Lucent.Mac.Editor;

/// <summary>
/// The editor's main view: a time ruler, a bar with a tab for each clip, then one lane per channel with
/// its waveform. Click to place the cursor, drag to select (Shift extends), the wheel (or ⌘+scroll, or a
/// trackpad pinch) zooms around the mouse; Shift+wheel, a sideways swipe or a middle-button drag pans, and
/// Option+scroll makes the waves taller or shorter. Drag a clip's tab to slide the clip
/// along, leaving silence behind it (its edges snap to the other clips' edges, the cursor and the playhead;
/// ⌘ slides it freely); Option+drag a tab to move the clip in among the others instead. Click a tab to
/// select the clip. A clip's edges and the handles at its top corners trim it and fade it (see WaveformView.Edges.cs).
/// Once the sound is split, the hand tool (on unless turned off) lets a clip be dragged by its waveform too,
/// and sliding a clip over the next one overlaps them with a crossfade, as in REAPER.
/// The playhead is a separate layer, so moving it never redraws the waveform.
/// </summary>
/// <remarks>
/// Also shows markers (flags in the ruler, which can be clicked or dragged), highlighted problem spots
/// (clipping), and, instead of the waveform, a spectrogram. Option+drag selects within one channel only.
/// With <see cref="Snap"/> set, the cursor and selection edges snap to it; holding ⌘ places them freely.
/// </remarks>
public sealed partial class WaveformView : Control
{
    public const double RulerHeight = 22;
    private const double ClipBarHeight = 20;
    private const double LaneGap = 6;
    private const double DragThreshold = 3;

    public static readonly StyledProperty<IBrush?> WaveBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(WaveBrush));
    public static readonly StyledProperty<IBrush?> SelectedWaveBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(SelectedWaveBrush));
    public static readonly StyledProperty<IBrush?> LaneBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(LaneBrush));
    public static readonly StyledProperty<IBrush?> SelectionBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(SelectionBrush));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(LineBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> CursorBrushProperty = AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(CursorBrush));
    public static readonly StyledProperty<IBrush?> PlayheadBrushProperty =
        AvaloniaProperty.Register<WaveformView, IBrush?>(nameof(PlayheadBrush), Brushes.White);

    private static readonly IBrush HighlightBrush = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0xFF, 0x4D, 0x4F));
    private static readonly IBrush HighlightEdgeBrush = new ImmutableSolidColorBrush(Color.FromRgb(0xFF, 0x4D, 0x4F));
    private static readonly IPen FrequencyPen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), 1);
    private static readonly Cursor IBeamCursor = new(StandardCursorType.Ibeam);
    private static readonly Cursor SideToSideCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private readonly PlayheadLayer _playhead;
    private readonly TranslateTransform _playheadOffset = new();
    private PeakCache? _peaks;
    private double _viewStart;
    private double _samplesPerPixel = 1;
    private double _amplitudeZoom = 1;
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
    // Where along the audio the tab was pressed; whether the drag puts the clip in among the others (Option)
    // rather than sliding it; and, while sliding, how far it has gone, the edge it snapped to and how far left it can go.
    private double _pressedFrame;
    private bool _reordering;
    private int _slideOffset;
    private SlidePlan? _slidePlan;
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
    // The mouse while it's held down (it's captured), and the keys held with it.
    private IPointer? _captured;
    private KeyModifiers _modifiers;
    private bool _leftDown;
    // A Control-click, which on a Mac opens the right-click menu.
    private bool _contextClick;

    static WaveformView()
    {
        AffectsRender<WaveformView>(WaveBrushProperty, SelectedWaveBrushProperty, LaneBrushProperty, SelectionBrushProperty,
            LineBrushProperty, LabelBrushProperty, CursorBrushProperty);
    }

    public WaveformView()
    {
        _playhead = new PlayheadLayer(this) { RenderTransform = _playheadOffset, Opacity = 0, IsHitTestVisible = false };
        VisualChildren.Add(_playhead);
        Cursor = IBeamCursor;
        ClipToBounds = true;
        Focusable = false;

        // The spectrogram is worked out once the view settles for a moment, not on every step of a scroll.
        _spectrogramDelay = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(40) };
        _spectrogramDelay.Tick += (_, _) =>
        {
            _spectrogramDelay.Stop();
            StartSpectrogram();
        };

        AddHandler(Gestures.PointerTouchPadGestureMagnifyEvent, OnPinch);
    }

    /// <summary>The waves were made taller or shorter.</summary>
    public event EventHandler? AmplitudeZoomChanged;

    /// <summary>A marker's flag was clicked (without dragging).</summary>
    public event EventHandler<int>? MarkerClicked;

    /// <summary>A marker's flag was dragged to a new frame.</summary>
    public event EventHandler<(int Marker, int Frame)>? MarkerMoved;

    /// <summary>The view moved or zoomed.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The selection or cursor changed.</summary>
    public event EventHandler? SelectionChanged;

    /// <summary>The user clicked (without dragging) to put the cursor somewhere.</summary>
    public event EventHandler? CursorPlaced;

    /// <summary>A clip's tab was Option+dragged and dropped: move clip <c>Clip</c> so it becomes clip number <c>ToIndex</c>.</summary>
    public event EventHandler<(int Clip, int ToIndex)>? ClipMoveRequested;

    /// <summary>A clip's tab was dragged and dropped: slide clip <c>Clip</c> along by <c>Offset</c> frames.</summary>
    public event EventHandler<(int Clip, int Offset)>? ClipSlideRequested;

    /// <summary>Right-clicked (or Control-clicked) at a point, for the editor's menu. WPF's MouseRightButtonUp.</summary>
    public event EventHandler<Point>? RightClicked;

    public IBrush? WaveBrush { get => GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    /// <summary>The waveform inside the selection, so what's selected stands out from the rest.</summary>
    public IBrush? SelectedWaveBrush { get => GetValue(SelectedWaveBrushProperty); set => SetValue(SelectedWaveBrushProperty, value); }

    public IBrush? LaneBrush { get => GetValue(LaneBrushProperty); set => SetValue(LaneBrushProperty, value); }

    public IBrush? SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }

    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }

    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public IBrush? CursorBrush { get => GetValue(CursorBrushProperty); set => SetValue(CursorBrushProperty, value); }

    public IBrush? PlayheadBrush { get => GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

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

    /// <summary>Where a clip would land if slid by an offset (see <see cref="EditSession.PlanSlide"/>), or null if it can't slide.</summary>
    public Func<int, int, SlidePlan?>? PlanSlide { get; set; }

    /// <summary>
    /// The hand tool (on unless turned off): once the sound is split, dragging a clip's waveform slides the clip, as
    /// dragging its tab does, instead of selecting; dragging in a gap of silence, in the ruler, or with Shift still
    /// selects. A click still puts the cursor there, and a double-click selects the clip.
    /// </summary>
    public bool HandTool { get; set; } = true;

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
    public double ViewLength => _samplesPerPixel * Math.Max(1, Bounds.Width);

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
        var anchorX = x ?? Bounds.Width / 2;
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

    private double MaxSamplesPerPixel => Math.Max(MinSamplesPerPixel, Frames / Math.Max(1, Bounds.Width));

    private void ClampView()
    {
        _samplesPerPixel = Math.Clamp(_samplesPerPixel, MinSamplesPerPixel, MaxSamplesPerPixel);
        _viewStart = Math.Clamp(_viewStart, 0, Math.Max(0, Frames - ViewLength));
    }

    private void RaiseViewChanged() => ViewChanged?.Invoke(this, EventArgs.Empty);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ClampView();
        _playhead.InvalidateVisual();
        RaiseViewChanged();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PlayheadBrushProperty)
        {
            _playhead.InvalidateVisual();
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _playhead.Arrange(new Rect(finalSize));
        return finalSize;
    }

    // ---- Drawing ----

    /// <summary>The playhead: a line down the view with a small triangle in the ruler, moved by a transform.</summary>
    private sealed class PlayheadLayer : Control
    {
        private readonly WaveformView _owner;

        public PlayheadLayer(WaveformView owner) => _owner = owner;

        public override void Render(DrawingContext dc)
        {
            var brush = _owner.PlayheadBrush ?? Brushes.White;
            var pen = new Pen(brush, 1.5);
            dc.DrawLine(pen, new Point(0, 0), new Point(0, Math.Max(0, Bounds.Height)));
            // A small triangle in the ruler marks the playhead clearly.
            var marker = new StreamGeometry();
            using (var g = marker.Open())
            {
                g.BeginFigure(new Point(-5, 0), true);
                g.LineTo(new Point(5, 0));
                g.LineTo(new Point(0, 7));
                g.EndFigure(true);
            }

            dc.DrawGeometry(brush, null, marker);
        }
    }

    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (_peaks is null || width <= 0 || height <= RulerHeight)
        {
            return;
        }

        var channels = _peaks.Audio.ChannelCount;
        var lanesTop = RulerHeight + ClipBarHeight;
        var laneHeight = LaneHeight(height, channels);
        var linePen = new Pen(LineBrush ?? Brushes.Gray, 1);
        var spectrogram = _view == EditorView.Spectrogram;

        for (var c = 0; c < channels; c++)
        {
            var top = LaneTop(c, laneHeight);
            dc.DrawRectangle(LaneBrush, null, new Rect(0, top, width, laneHeight), 6, 6);
        }

        // Selection behind the waveform, across the ruler and every lane it covers (one, with Option+drag).
        Rect? selected = null;
        var selectedChannel = SelectedChannel;
        var selectionRects = new List<Rect>();
        if (!_selection.IsEmpty)
        {
            var x0 = Math.Max(0, FrameToX(_selection.Start));
            var x1 = Math.Min(width, FrameToX(_selection.End));
            if (x1 > x0 - 1)
            {
                var whole = new Rect(x0, 0, Math.Max(1, x1 - x0), height);
                selected = whole;
                if (selectedChannel is { } only)
                {
                    selectionRects.Add(new Rect(whole.X, 0, whole.Width, lanesTop));
                    selectionRects.Add(new Rect(whole.X, LaneTop(only, laneHeight) - LaneGap / 2, whole.Width, laneHeight + LaneGap));
                }
                else
                {
                    selectionRects.Add(whole);
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
        DrawBeatGrid(dc, width, lanesTop, height);
        DrawClips(dc, width, height);

        for (var c = 0; c < channels; c++)
        {
            var top = LaneTop(c, laneHeight);
            var lane = new Rect(0, top, width, laneHeight);
            // A lane muted (or left out of a solo) is dimmed: it isn't heard.
            var heard = IsHeard(c);
            DrawingContext.PushedState? dimmed = heard ? null : dc.PushOpacity(0.35);
            if (spectrogram)
            {
                DrawSpectrogram(dc, c, lane);
            }
            else
            {
                var middle = top + laneHeight / 2;
                dc.DrawLine(linePen, new Point(0, Math.Round(middle) + 0.5), new Point(width, Math.Round(middle) + 0.5));
                using (dc.PushClip(lane))
                {
                    DrawWave(dc, c, top, laneHeight, width, WaveBrush);
                }

                // The selected part is drawn again over it in its own colour.
                if (selected is { } selectedRect && lane.Intersects(selectedRect) && SelectedWaveBrush is not null &&
                    (selectedChannel is null || selectedChannel == c))
                {
                    using (dc.PushClip(lane.Intersect(selectedRect)))
                    {
                        DrawWave(dc, c, top, laneHeight, width, SelectedWaveBrush);
                    }
                }
            }

            var label = channels == 1 ? "Mono" : c == 0 ? "L" : "R";
            if (!spectrogram && _amplitudeZoom > 1.001)
            {
                label += "   " + AmplitudeZoomText(_amplitudeZoom);
            }

            dimmed?.Dispose();
            dc.DrawText(Text(label, 10, spectrogram ? SelectedWaveBrush : LabelBrush), new Point(8, top + 4));
            DrawListenButtons(dc, c, top);
        }

        if (spectrogram)
        {
            // Over the picture, which would hide it underneath.
            foreach (var rect in selectionRects)
            {
                dc.DrawRectangle(SelectionBrush, null, rect);
            }
        }

        if (!spectrogram)
        {
            DrawEdges(dc, width, height, channels, laneHeight);
        }

        DrawHighlights(dc, width, lanesTop, height);
        DrawMarkers(dc, width, height);
        DrawLoopLength(dc, width, lanesTop);

        // The cursor, when nothing is selected.
        if (_selection.IsEmpty)
        {
            var x = Math.Round(FrameToX(_cursor)) + 0.5;
            if (x >= 0 && x <= width)
            {
                // Top to bottom, through the ruler with a small flag at the top like the playhead's, in its own
                // colour (not the waves'), so it's easy to find.
                var brush = CursorBrush ?? Brushes.White;
                dc.DrawLine(new Pen(brush, 1.5), new Point(x, 0), new Point(x, height));
                var flag = new StreamGeometry();
                using (var g = flag.Open())
                {
                    g.BeginFigure(new Point(x - 5, 0), true);
                    g.LineTo(new Point(x + 5, 0));
                    g.LineTo(new Point(x, 7));
                    g.EndFigure(true);
                }

                dc.DrawGeometry(brush, null, flag);
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
        var laneHeight = LaneHeight(Bounds.Height, channels);
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

        using (dc.PushClip(new RoundedRect(lane, 6)))
        {
            dc.DrawRectangle(Brushes.Black, null, lane);
            if (image is not null && ReferenceEquals(image.Audio, _peaks?.Audio))
            {
                var x = FrameToX(image.ViewStart);
                var w = image.Width * image.SamplesPerPixel / _samplesPerPixel;
                dc.DrawImage(image.Bitmap, new Rect(x, lane.Top, w, lane.Height));
            }

            // A few frequencies up the side, so the picture can be read.
            var highest = (_peaks?.Audio.SampleRate ?? 44100) / 2.0;
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

                dc.DrawLine(FrequencyPen, new Point(0, y), new Point(lane.Width, y));
                var text = hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}";
                dc.DrawText(Text(text, 9, SelectedWaveBrush), new Point(lane.Width - 26, y - 12));
            }
        }
    }

    /// <summary>Works out the spectrogram for the view as it is now, in the background, then redraws.</summary>
    private void StartSpectrogram()
    {
        _spectrogramWork?.Cancel();
        if (_peaks is null || _view != EditorView.Spectrogram || Bounds.Width < 1)
        {
            return;
        }

        var audio = _peaks.Audio;
        var channels = audio.ChannelCount;
        var laneHeight = LaneHeight(Bounds.Height, channels);
        var width = (int)Math.Ceiling(Bounds.Width);
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
                Dispatcher.UIThread.Post(() =>
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

    /// <summary>
    /// Works out the spectrogram for the view as it is now and waits for it, for the screenshots, which
    /// can't wait for the background work.
    /// </summary>
    public void RenderSpectrogramNow()
    {
        if (_peaks is null || Bounds.Width < 1)
        {
            return;
        }

        var audio = _peaks.Audio;
        var laneHeight = LaneHeight(Bounds.Height, audio.ChannelCount);
        _spectrogram = Enumerable.Range(0, audio.ChannelCount)
            .Select(c => SpectrogramRenderer.Render(audio, c, _viewStart, _samplesPerPixel, (int)Math.Ceiling(Bounds.Width),
                (int)Math.Ceiling(laneHeight), CancellationToken.None))
            .ToArray();
        InvalidateVisual();
    }

    /// <summary>The problem spots, as red bands over the lanes.</summary>
    private void DrawHighlights(DrawingContext dc, double width, double lanesTop, double height)
    {
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
        var line = new Pen(brush, 1, new DashStyle(new double[] { 2, 2 }, 0));
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
                g.BeginFigure(new Point(x, 2), true);
                g.LineTo(new Point(x + 9, 2));
                g.LineTo(new Point(x + 9, RulerHeight - 6));
                g.LineTo(new Point(x, RulerHeight - 2));
                g.EndFigure(true);
            }

            dc.DrawGeometry(brush, null, flag);
            var name = _markers[i].Name.Length > 24 ? _markers[i].Name[..23] + "…" : _markers[i].Name;
            var text = Text(name, 10, SelectedWaveBrush ?? Brushes.White);
            // On a backing, so the ruler's times don't run through it.
            dc.DrawRectangle(LaneBrush, null, new Rect(x + 11, 3, text.Width + 6, 15), 3, 3);
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

    /// <summary>The clip tabs under the ruler, the splits across the lanes, and the clip being dragged.</summary>
    private void DrawClips(DrawingContext dc, double width, double height)
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
            using (dc.PushOpacity(_draggingClip && _draggedClip == i ? 0.35 : 1))
            {
                dc.DrawRectangle(LaneBrush, null, tab, 4, 4);
                var label = clips.Count == 1
                    ? "Press S to split at the cursor"
                    : $"Clip {i + 1}  ·  {FormatTime(clips[i].Length / (double)SampleRate, 0.1)}";
                using (dc.PushClip(tab))
                {
                    dc.DrawText(Text(label, 10, LabelBrush), new Point(Math.Max(x0, 0) + 6, tabTop + 2));
                }
            }
        }

        var splitPen = new Pen(LabelBrush ?? Brushes.Gray, 1, new DashStyle(new double[] { 3, 3 }, 0));
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
            // The clip where it will land, an X where it will crossfade with a clip it overlaps, and a bright line on
            // the edge it snapped to.
            var placed = _slidePlan?.Placed ?? new FrameRange(clip.Start + _slideOffset, clip.Length);
            var ghost = new Rect(FrameToX(placed.Start), tabTop, Math.Max(4, placed.Length / _samplesPerPixel), height - tabTop);
            using (dc.PushOpacity(0.8))
            {
                dc.DrawRectangle(SelectionBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1), ghost, 4, 4);
            }

            if (_slidePlan is { } plan)
            {
                var crossPen = new Pen(CursorBrush ?? Brushes.White, 1.5);
                foreach (var (from, to) in new[] { (placed.Start, placed.Start + plan.OverlapBefore), (placed.End - plan.OverlapAfter, placed.End) })
                {
                    if (to > from)
                    {
                        var (x0, x1) = (FrameToX(from), FrameToX(to));
                        dc.DrawLine(crossPen, new Point(x0, tabTop), new Point(x1, height));
                        dc.DrawLine(crossPen, new Point(x0, height), new Point(x1, tabTop));
                    }
                }
            }

            if (_slideSnap is { } snapped)
            {
                var snapX = Math.Round(FrameToX(snapped)) + 0.5;
                dc.DrawLine(new Pen(CursorBrush ?? Brushes.White, 2), new Point(snapX, RulerHeight), new Point(snapX, height));
            }

            return;
        }

        // The dragged clip follows the mouse, and a bright line shows where it will land.
        var ghostX = _dragX - Math.Min(ghostWidth, Math.Max(0, _pressedAt.X - FrameToX(clip.Start)));
        using (dc.PushOpacity(0.8))
        {
            dc.DrawRectangle(SelectionBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1),
                new Rect(ghostX, tabTop, ghostWidth, height - tabTop), 4, 4);
        }

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
    /// How far a clip slides when it's dragged to an x position: as far as the mouse moved, snapped (unless ⌘ is
    /// held) so one of its edges lines up with another clip's edge, the cursor or the playhead when it's close, and
    /// no further than it can go (see <see cref="EditSession.PlanSlide"/>). Also says which edge it snapped to.
    /// </summary>
    private (int Offset, int? Snap) SlideOffset(int dragged, double x)
    {
        var clip = Clips[dragged];
        var moved = XToFrame(x) - _pressedFrame;
        var offset = moved;
        int? snap = null;
        if (!_modifiers.HasFlag(KeyModifiers.Meta))
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
        _slidePlan = PlanSlide?.Invoke(dragged, frames);
        if (_slidePlan is { } plan && plan.Offset != frames)
        {
            return (plan.Offset, null);
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

    private void DrawWave(DrawingContext dc, int channel, double top, double laneHeight, double width, IBrush? brush)
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

                g.BeginFigure(new Point(0, highs[0].Y), true);
                for (var x = 0; x < lastColumn; x++)
                {
                    g.LineTo(highs[x]);
                    g.LineTo(new Point(x + 1, highs[x].Y));
                }

                for (var x = lastColumn - 1; x >= 0; x--)
                {
                    g.LineTo(lows[x]);
                    g.LineTo(new Point(x, lows[x].Y));
                }

                g.EndFigure(true);
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

                g.BeginFigure(new Point(FrameToX(first), middle - SampleHeight(samples[first]) * half), false);
                for (var i = first + 1; i <= last; i++)
                {
                    g.LineTo(new Point(FrameToX(i), middle - SampleHeight(samples[i]) * half));
                }

                g.EndFigure(false);
            }
        }

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

    private void DrawRuler(DrawingContext dc, double width, IPen linePen)
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

    private FormattedText Text(string text, double size, IBrush? brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(EditorFonts.Ui(this)), size, brush ?? Brushes.Gray);

    // ---- Mouse ----

    private bool IsMouseCaptured => _captured is { } pointer && pointer.Captured == this;

    private void Capture(IPointer pointer)
    {
        pointer.Capture(this);
        _captured = pointer;
    }

    private void ReleaseMouseCapture()
    {
        var pointer = _captured;
        _captured = null;
        pointer?.Capture(null);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _modifiers = e.KeyModifiers;
        var point = e.GetCurrentPoint(this);
        var position = point.Position;
        if (point.Properties.IsMiddleButtonPressed)
        {
            _panFrom = position;
            _panStartView = _viewStart;
            Capture(e.Pointer);
            Cursor = SideToSideCursor;
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed || _peaks is null)
        {
            return;
        }

        // On a Mac, Control-click is a right-click.
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _contextClick = true;
            e.Handled = true;
            return;
        }

        if (ListenButtonAt(position) is { } button)
        {
            // A lane's mute or solo button.
            ToggleListen(button.Channel, button.Solo);
            e.Handled = true;
            return;
        }

        if (IsOnLengthTag(position))
        {
            // The selection's length tag: a click switches it between bars and beats and minutes and seconds.
            ToggleLengthUnits();
            e.Handled = true;
            return;
        }

        var frame = FrameAt(position.X);
        Capture(e.Pointer);
        _leftDown = true;
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

        if (PressEdge(position))
        {
            // A clip's edge or fade handle: dragging trims or fades the clip, a click places the cursor.
            _anchor = Snapped(frame);
            e.Handled = true;
            return;
        }

        if (e.ClickCount == 2 && position.Y >= RulerHeight && Clips.Count > 1)
        {
            // A double-click selects the clip.
            ReleaseMouseCapture();
            _leftDown = false;
            Selection = Clips[ClipIndexAt(frame)];
            _anchor = null;
            e.Handled = true;
            return;
        }

        if (IsOnClipBar(position) || MovesClipAt(position))
        {
            // A clip's tab (or, with the hand tool, its waveform): dragging slides the clip (Option+dragging its tab
            // moves it in among the others). A click on the tab selects the clip; on the waveform, it places the cursor.
            _draggedClip = ClipIndexAt(frame);
            _draggingClip = false;
            _dragX = position.X;
            _pressedFrame = XToFrame(position.X);
            _reordering = IsOnClipBar(position) && e.KeyModifiers.HasFlag(KeyModifiers.Alt);
            _slideOffset = 0;
            _slidePlan = null;
            _slideSnap = null;
            _anchor = IsOnClipBar(position) ? null : Snapped(frame);
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
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
            // Option+drag in a lane selects in that channel only.
            _channel = e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? LaneAt(position.Y) : null;
            _anchor = Snapped(frame);
        }

        e.Handled = true;
    }

    /// <summary>A frame moved to the snap point near it, unless ⌘ is held.</summary>
    private int Snapped(int frame) =>
        Snap is { } snap && !_modifiers.HasFlag(KeyModifiers.Meta) ? Math.Clamp(snap(frame), 0, Frames) : frame;

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _modifiers = e.KeyModifiers;
        var position = e.GetPosition(this);
        if (!IsMouseCaptured && _panFrom is null)
        {
            HoverClip(position);
            Cursor = IsOnLengthTag(position) || ListenButtonAt(position) is not null ? HandCursor
                : MarkerAt(position) is not null ? SideToSideCursor
                : EdgeAt(position, _modifiers.HasFlag(KeyModifiers.Shift)) is { } edge ? EdgeCursor(edge)
                : (IsOnClipBar(position) || MovesClipAt(position)) && _peaks is not null ? HandCursor
                : IBeamCursor;
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

        if (_grab is not null && IsMouseCaptured)
        {
            DragEdge(position);
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
            if (position.X < 0 || position.X > Bounds.Width)
            {
                var overshoot = position.X < 0 ? position.X : position.X - Bounds.Width;
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

        if (!IsMouseCaptured || !_leftDown || _anchor is null)
        {
            return;
        }

        if (!_selecting && Math.Abs(position.X - _pressedAt.X) < DragThreshold)
        {
            return;
        }

        _selecting = true;
        // Dragging past either edge scrolls the view along.
        if (position.X < 0 || position.X > Bounds.Width)
        {
            var overshoot = position.X < 0 ? position.X : position.X - Bounds.Width;
            _viewStart += overshoot * _samplesPerPixel * 0.5;
            ClampView();
            RaiseViewChanged();
        }

        SetSelectionTo(FrameAt(position.X));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _modifiers = e.KeyModifiers;
        var position = e.GetPosition(this);
        if (e.InitialPressMouseButton == MouseButton.Right || (_contextClick && e.InitialPressMouseButton == MouseButton.Left))
        {
            _contextClick = false;
            RightClicked?.Invoke(this, position);
            e.Handled = true;
            return;
        }

        if (e.InitialPressMouseButton == MouseButton.Middle)
        {
            if (_panFrom is not null)
            {
                _panFrom = null;
                ReleaseMouseCapture();
                Cursor = IBeamCursor;
                e.Handled = true;
            }

            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left || !IsMouseCaptured)
        {
            return;
        }

        _leftDown = false;
        // Letting go of the mouse forgets the drag, so read it first.
        var pressedClip = _draggedClip;
        var dragged = _draggingClip;
        var pressedMarker = _pressedMarker;
        var draggedMarker = _draggingMarker;
        var grab = _grab;
        var grabbed = _grabbing;
        ReleaseMouseCapture();
        if (grab is not null && ReleaseEdge(grab, grabbed, position.X))
        {
            e.Handled = true;
            return;
        }

        if (pressedMarker is { } marker)
        {
            if (draggedMarker)
            {
                MarkerMoved?.Invoke(this, (marker, Snapped(FrameAt(position.X))));
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
            if (!dragged && _anchor is { } placed)
            {
                // A click on a clip's waveform with the hand tool: the cursor goes there, as without it.
                _selection = new FrameRange(placed, 0);
                _cursor = placed;
                InvalidateVisual();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                CursorPlaced?.Invoke(this, EventArgs.Empty);
            }
            else if (!dragged && clip < clips.Count)
            {
                Selection = clips[clip];
            }
            else if (dragged && !_reordering && clip < clips.Count)
            {
                var (offset, _) = SlideOffset(clip, position.X);
                if (offset != 0)
                {
                    ClipSlideRequested?.Invoke(this, (clip, offset));
                }
            }
            else if (dragged && DropIndex(clip, XToFrame(position.X)) is var toIndex && toIndex != clip)
            {
                ClipMoveRequested?.Invoke(this, (clip, toIndex));
            }

            if (dragged)
            {
                _anchor = null;
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

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        // A sideways swipe on a trackpad (or Shift+wheel, which macOS turns sideways) scrolls; up and down zooms.
        var sideways = e.Delta.X != 0 ? e.Delta.X : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : 0;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            // Option+scroll makes the waves taller or shorter.
            AmplitudeZoom *= Math.Pow(1.25, e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X);
        }
        else if (sideways != 0)
        {
            ScrollBy(-sideways * 0.15);
        }
        else
        {
            Zoom(Math.Pow(0.8, e.Delta.Y), e.GetPosition(this).X);
        }

        e.Handled = true;
    }

    /// <summary>How much the waves are magnified, as a gain in decibels ("+12 dB").</summary>
    public static string AmplitudeZoomText(double zoom) =>
        "+" + (20 * Math.Log10(zoom)).ToString("0.#", CultureInfo.CurrentCulture) + " dB";

    private void OnPinch(object? sender, PointerDeltaEventArgs e)
    {
        // Spreading two fingers on the trackpad magnifies (a positive delta), so it zooms in around the pointer.
        var magnify = Math.Max(-0.9, e.Delta.X);
        if (_peaks is not null && magnify != 0)
        {
            Zoom(1 / (1 + magnify), e.GetPosition(this).X);
        }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _captured = null;
        _leftDown = false;
        // Esc, or another window taking the mouse, drops a clip, clip edge or marker back where it was.
        if (_draggedClip is not null || _pressedMarker is not null || _grab is not null)
        {
            _grab = null;
            _grabbing = false;
            _draggedClip = null;
            _draggingClip = false;
            _pressedMarker = null;
            _draggingMarker = false;
            InvalidateVisual();
        }

        if (_panFrom is not null)
        {
            _panFrom = null;
            Cursor = IBeamCursor;
        }
    }

    /// <summary>True while a clip's tab, a clip edge or a marker is being dragged.</summary>
    public bool IsDraggingClip => _draggingClip || _draggingMarker || _grabbing;

    /// <summary>Stops dragging a clip or marker, leaving it where it was.</summary>
    public void CancelClipDrag()
    {
        if ((_draggedClip is not null || _pressedMarker is not null || _grab is not null) && IsMouseCaptured)
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

    /// <summary>
    /// Whether pressing here grabs a clip to move it: with the hand tool on, in the lanes of a split sound, over a
    /// clip with sound in it (gaps of silence select), unless Shift (to select) or Option (to select in one channel) is held.
    /// </summary>
    private bool MovesClipAt(Point position)
    {
        if (!HandTool || _peaks is null || position.Y < RulerHeight + ClipBarHeight || Clips.Count < 2
            || _modifiers.HasFlag(KeyModifiers.Shift) || _modifiers.HasFlag(KeyModifiers.Alt))
        {
            return false;
        }

        var index = ClipIndexAt(FrameAt(position.X));
        return _clipEdges.Count != Clips.Count || index >= _clipEdges.Count || !_clipEdges[index].Silent;
    }

    private void SetSelectionTo(int frame)
    {
        frame = Snapped(frame);
        var anchor = _anchor ?? frame;
        _selection = new FrameRange(Math.Min(anchor, frame), Math.Abs(frame - anchor));
        _cursor = _selection.Start;
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
