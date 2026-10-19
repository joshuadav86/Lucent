using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using WinampInspiredVisualizer.Editor;

namespace Lucent.Mac.Editor;

/// <summary>
/// Clip edges and fades, edited the way a DAW edits items. Near a clip's left or right edge the pointer turns into
/// a trimmer: dragging the edge in trims the clip (leaving silence, and keeping what was trimmed), and dragging it
/// out over silence brings back what was trimmed or cut. Where two clips touch, dragging moves the split between
/// them (Shift+drag trims just the clip the pointer is on). The handles at a clip's top corners drag out a fade in
/// or out. Edges snap to the other clips' edges, the cursor and the playhead; ⌘ drags freely.
/// </summary>
public sealed partial class WaveformView
{
    private const double EdgeReach = 5;
    private const double FadeHandleReach = 7;
    private const double FadeBandHeight = 16;
    private static readonly Cursor FadeInCursor = new(StandardCursorType.TopLeftCorner);
    private static readonly Cursor FadeOutCursor = new(StandardCursorType.TopRightCorner);
    private static readonly IBrush FadeShade = new ImmutableSolidColorBrush(Color.FromArgb(0x55, 0, 0, 0));

    private IReadOnlyList<ClipEdges> _clipEdges = Array.Empty<ClipEdges>();
    private EdgeGrab? _grab;
    private bool _grabbing;
    private int _grabFrame;
    private int? _grabSnap;
    private int? _hoverClip;

    /// <summary>A clip's edge was dragged to a frame (Start is true for its left edge).</summary>
    public event EventHandler<(int Clip, bool Start, int Frame)>? ClipEdgeRequested;

    /// <summary>The split between two touching clips was dragged to a frame.</summary>
    public event EventHandler<(int Split, int Frame)>? SplitMoveRequested;

    /// <summary>A clip's fade handle was dragged: its fade in (or out) should be this many frames long.</summary>
    public event EventHandler<(int Clip, bool FadeIn, int Frames)>? ClipFadeRequested;

    /// <summary>What each clip's edges can do (from <see cref="EditSession.ClipEdgeDetails"/>), in clip order.</summary>
    public IReadOnlyList<ClipEdges> ClipEdgeDetails
    {
        get => _clipEdges;
        set
        {
            _clipEdges = value;
            InvalidateVisual();
        }
    }

    private enum GrabKind
    {
        Start,
        End,
        Split,
        FadeIn,
        FadeOut,
    }

    /// <param name="Frame">Where the edge or the fade handle is now.</param>
    /// <param name="Min">How far left it can go.</param>
    /// <param name="Max">How far right it can go.</param>
    private sealed record EdgeGrab(GrabKind Kind, int Clip, int Frame, int Min, int Max);

    /// <summary>The edge or fade handle at a point, if there is one.</summary>
    private EdgeGrab? EdgeAt(Point position, bool shift)
    {
        var edges = _clipEdges;
        var lanesTop = RulerHeight + ClipBarHeight;
        if (_peaks is null || position.Y < lanesTop || edges.Count != Clips.Count || edges.Count == 0 || edges[^1].Range.End != Frames)
        {
            return null;
        }

        // The fade handles, in a band along the top of the lanes.
        if (position.Y <= lanesTop + LaneGap + FadeBandHeight)
        {
            EdgeGrab? best = null;
            var nearest = FadeHandleReach;
            for (var i = 0; i < edges.Count; i++)
            {
                var clip = edges[i];
                if (clip.Silent)
                {
                    continue;
                }

                var fadeIn = clip.Range.Start + clip.FadeIn;
                var fadeOut = clip.Range.End - clip.FadeOut;
                foreach (var (kind, frame, min, max) in new[]
                {
                    (GrabKind.FadeIn, fadeIn, clip.Range.Start, fadeOut),
                    (GrabKind.FadeOut, fadeOut, fadeIn, clip.Range.End),
                })
                {
                    var distance = Math.Abs(FrameToX(frame) - position.X);
                    // A handle at the edge is only reached from inside the clip.
                    var inside = kind == GrabKind.FadeIn ? position.X >= FrameToX(clip.Range.Start) - 1 : position.X <= FrameToX(clip.Range.End) + 1;
                    if (distance < nearest && inside)
                    {
                        nearest = distance;
                        best = new EdgeGrab(kind, i, frame, min, max);
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        // The edges, while the audio is in clips.
        if (edges.Count < 2)
        {
            return null;
        }

        for (var i = 0; i <= edges.Count; i++)
        {
            var frame = i < edges.Count ? edges[i].Range.Start : Frames;
            var x = FrameToX(frame);
            if (Math.Abs(x - position.X) > EdgeReach)
            {
                continue;
            }

            var left = i > 0 && !edges[i - 1].Silent ? edges[i - 1] : null;
            var right = i < edges.Count && !edges[i].Silent ? edges[i] : null;
            if (left is not null && right is not null && !shift)
            {
                return new EdgeGrab(GrabKind.Split, i, frame, left.Range.Start + 1, right.Range.End - 1);
            }

            // Where two clips touch, Shift trims the one the pointer is over.
            if (left is not null && (right is null || position.X < x))
            {
                var silence = Silence(edges, i, 1);
                return new EdgeGrab(GrabKind.End, i - 1, frame, left.Range.Start + 1, frame + Math.Min(silence, left.HiddenAfter));
            }

            if (right is not null)
            {
                var silence = Silence(edges, i - 1, -1);
                return new EdgeGrab(GrabKind.Start, i, frame, frame - Math.Min(silence, right.HiddenBefore), right.Range.End - 1);
            }
        }

        return null;
    }

    /// <summary>How much silence there is in the silent clips from clip <paramref name="from"/> on, going one way.</summary>
    private static int Silence(IReadOnlyList<ClipEdges> edges, int from, int step)
    {
        var frames = 0;
        for (var i = from; i >= 0 && i < edges.Count && edges[i].Silent; i += step)
        {
            frames += edges[i].Range.Length;
        }

        return frames;
    }

    private static Cursor EdgeCursor(EdgeGrab grab) => grab.Kind switch
    {
        GrabKind.FadeIn => FadeInCursor,
        GrabKind.FadeOut => FadeOutCursor,
        _ => SideToSideCursor,
    };

    /// <summary>Starts dragging an edge or fade handle, if the press is on one.</summary>
    private bool PressEdge(Point position)
    {
        if (EdgeAt(position, _modifiers.HasFlag(KeyModifiers.Shift)) is not { } grab)
        {
            return false;
        }

        _grab = grab;
        _grabbing = false;
        _grabFrame = grab.Frame;
        _grabSnap = null;
        _anchor = null;
        return true;
    }

    private void DragEdge(Point position)
    {
        if (_grab is not { } grab || (!_grabbing && Math.Abs(position.X - _pressedAt.X) < DragThreshold))
        {
            return;
        }

        _grabbing = true;
        if (position.X < 0 || position.X > Bounds.Width)
        {
            var overshoot = position.X < 0 ? position.X : position.X - Bounds.Width;
            _viewStart += overshoot * _samplesPerPixel * 0.5;
            ClampView();
            RaiseViewChanged();
        }

        (_grabFrame, _grabSnap) = EdgeFrame(grab, position.X);
        InvalidateVisual();
    }

    /// <summary>
    /// Where a dragged edge or fade handle goes for an x position: snapped (unless Ctrl is held) to another clip's
    /// edge, the cursor or the playhead when it's close, and kept within what the clip allows.
    /// </summary>
    private (int Frame, int? Snap) EdgeFrame(EdgeGrab grab, double x)
    {
        var frame = XToFrame(x);
        int? snap = null;
        if (!_modifiers.HasFlag(KeyModifiers.Meta))
        {
            var best = 8 * _samplesPerPixel;
            var targets = _splits.Prepend(0).Append(Frames).Append(_cursor).Where(t => t != grab.Frame);
            if (_playheadFrame is { } playhead)
            {
                targets = targets.Append(playhead);
            }

            foreach (var target in targets)
            {
                if (Math.Abs(target - frame) < best && target >= grab.Min && target <= grab.Max)
                {
                    best = Math.Abs(target - frame);
                    snap = target;
                }
            }
        }

        return (Math.Clamp(snap ?? (int)Math.Round(frame), grab.Min, grab.Max), snap);
    }

    /// <summary>Finishes dragging an edge or fade handle, asking for the change. Returns false for a click that didn't drag.</summary>
    private bool ReleaseEdge(EdgeGrab grab, bool dragged, double x)
    {
        if (!dragged)
        {
            return false;
        }

        var (frame, _) = EdgeFrame(grab, x);
        if (frame == grab.Frame)
        {
            InvalidateVisual();
            return true;
        }

        var range = _clipEdges[Math.Min(grab.Clip, _clipEdges.Count - 1)].Range;
        switch (grab.Kind)
        {
            case GrabKind.Start:
            case GrabKind.End:
                ClipEdgeRequested?.Invoke(this, (grab.Clip, grab.Kind == GrabKind.Start, frame));
                break;
            case GrabKind.Split:
                SplitMoveRequested?.Invoke(this, (grab.Frame, frame));
                break;
            case GrabKind.FadeIn:
                ClipFadeRequested?.Invoke(this, (grab.Clip, true, frame - range.Start));
                break;
            case GrabKind.FadeOut:
                ClipFadeRequested?.Invoke(this, (grab.Clip, false, range.End - frame));
                break;
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>Keeps note of the clip under the pointer, whose fade handles are shown.</summary>
    private void HoverClip(Point position)
    {
        int? clip = _peaks is not null && position.Y >= RulerHeight && position.X >= 0 && position.X <= Bounds.Width
            ? ClipIndexAt(FrameAt(position.X))
            : null;
        if (clip != _hoverClip)
        {
            _hoverClip = clip;
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hoverClip is not null && !IsMouseCaptured)
        {
            _hoverClip = null;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Each clip's fades as curves across its lanes, the hovered clip's fade handles, and, while an edge is dragged,
    /// where the clip will be.
    /// </summary>
    private void DrawEdges(DrawingContext dc, double width, double height, int channels, double laneHeight)
    {
        var edges = _clipEdges;
        if (edges.Count != Clips.Count)
        {
            return;
        }

        var curvePen = new Pen(SelectedWaveBrush ?? Brushes.White, 1.5);
        var grab = _grabbing ? _grab : null;
        for (var i = 0; i < edges.Count; i++)
        {
            var clip = edges[i];
            if (clip.Silent)
            {
                continue;
            }

            var fadeIn = clip.FadeIn;
            var fadeOut = clip.FadeOut;
            if (grab is { } g && g.Clip == i)
            {
                fadeIn = g.Kind == GrabKind.FadeIn ? _grabFrame - clip.Range.Start : fadeIn;
                fadeOut = g.Kind == GrabKind.FadeOut ? clip.Range.End - _grabFrame : fadeOut;
            }

            var x0 = FrameToX(clip.Range.Start);
            var x1 = FrameToX(clip.Range.End);
            if (x1 < 0 || x0 > width)
            {
                continue;
            }

            for (var c = 0; c < channels; c++)
            {
                var top = LaneTop(c, laneHeight);
                if (fadeIn > 0)
                {
                    DrawFade(dc, x0, FrameToX(clip.Range.Start + fadeIn), top, laneHeight, rising: true, curvePen);
                }

                if (fadeOut > 0)
                {
                    DrawFade(dc, FrameToX(clip.Range.End - fadeOut), x1, top, laneHeight, rising: false, curvePen);
                }
            }

            if (_hoverClip == i || grab?.Clip == i && grab.Kind is GrabKind.FadeIn or GrabKind.FadeOut)
            {
                var handleTop = LaneTop(0, laneHeight) + 2;
                foreach (var x in new[] { FrameToX(clip.Range.Start + fadeIn), FrameToX(clip.Range.End - fadeOut) })
                {
                    var handle = new Rect(Math.Clamp(x, x0, x1 - 1) - 4, handleTop, 8, 8);
                    dc.DrawRectangle(SelectedWaveBrush ?? Brushes.White, new Pen(LaneBrush ?? Brushes.Black, 1), handle, 2, 2);
                }
            }
        }

        if (grab is null || grab.Kind is GrabKind.FadeIn or GrabKind.FadeOut)
        {
            return;
        }

        // The clip where it will end up, and a bright line on the edge being dragged.
        var lanesTop = RulerHeight + ClipBarHeight;
        if (grab.Kind != GrabKind.Split)
        {
            var range = edges[grab.Clip].Range;
            var start = grab.Kind == GrabKind.Start ? _grabFrame : range.Start;
            var end = grab.Kind == GrabKind.End ? _grabFrame : range.End;
            using (dc.PushOpacity(0.6))
            {
                dc.DrawRectangle(SelectionBrush, new Pen(SelectedWaveBrush ?? Brushes.White, 1),
                    new Rect(FrameToX(start), lanesTop, Math.Max(1, FrameToX(end) - FrameToX(start)), height - lanesTop), 4, 4);
            }
        }

        var edgeX = Math.Round(FrameToX(_grabFrame)) + 0.5;
        dc.DrawLine(new Pen(_grabSnap is null ? SelectedWaveBrush ?? Brushes.White : CursorBrush ?? Brushes.White, 2),
            new Point(edgeX, RulerHeight), new Point(edgeX, height));
    }

    /// <summary>A fade's curve across one lane, with the part above it shaded (what the fade takes away).</summary>
    private static void DrawFade(DrawingContext dc, double x0, double x1, double top, double laneHeight, bool rising, IPen pen)
    {
        if (x1 - x0 < 1)
        {
            return;
        }

        var bottom = top + laneHeight;
        var steps = Math.Clamp((int)(x1 - x0) / 3, 2, 64);
        var curve = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = curve.Open())
        using (var a = area.Open())
        {
            for (var i = 0; i <= steps; i++)
            {
                var gain = EditSession.FadeGain(i, steps + 1, rising);
                var point = new Point(x0 + (x1 - x0) * i / steps, bottom - gain * laneHeight);
                if (i == 0)
                {
                    g.BeginFigure(point, false);
                    a.BeginFigure(new Point(x0, top), true);
                }
                else
                {
                    g.LineTo(point);
                }

                a.LineTo(point);
            }

            a.LineTo(new Point(x1, top));
            g.EndFigure(false);
            a.EndFigure(true);
        }

        dc.DrawGeometry(FadeShade, null, area);
        dc.DrawGeometry(null, pen, curve);
    }
}
