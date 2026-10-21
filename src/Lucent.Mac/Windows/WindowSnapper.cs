using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Windows;

/// <summary>
/// Makes Lucent's windows behave like Winamp's: their edges snap to each other and to the
/// edges of the screen, and dragging the player window carries every window docked to it.
/// Drop a window beside others with one corner lined up and the other corner nearly lined up,
/// and it stretches to line that one up too. Resizing a window resizes the windows docked to it
/// along that edge: the edge between two windows moves both, and an outside edge shared by several
/// windows moves them all. Hold ⌘ while resizing to resize just the one window.
/// A window that's locked (<see cref="WindowLocks"/>) stays where it is: it can't be dragged or resized,
/// and nothing carries or resizes it along with another. With <see cref="MoveEveryWindow"/> on, dragging
/// the player carries every window, docked to it or not.
/// </summary>
/// <remarks>
/// On Windows this works on the window messages sent while Windows moves a window. On the Mac the
/// snapper moves windows itself: a press on a window's title bar (anything marked
/// <see cref="Chrome.IsCaptionProperty"/>) starts a drag that this class follows, working out where
/// the window would be without snapping (from how far the mouse has moved since the drag began) and
/// snapping that, so a window lets go again once the mouse is pulled further than the snap distance.
/// Resizing is done by macOS from the window's edges; the snapper follows it as it happens (moving
/// the windows linked to the resized edges) and snaps the resized edges when it ends.
/// Everything here is in the windows' own units (points), not pixels.
/// </remarks>
public sealed class WindowSnapper
{
    /// <summary>How close an edge has to come before it snaps.</summary>
    private const double SnapDistance = 16;

    /// <summary>How far a dropped window will stretch to line up its other corner.</summary>
    private const double CornerDistance = 40;

    /// <summary>
    /// How far a window can be pushed into another before it slides over it. Up to this, it stays
    /// against that window's edge instead, so windows don't end up overlapping by accident.
    /// </summary>
    private const double OverlapDistance = 64;

    /// <summary>How long after the last size change a resize counts as finished.</summary>
    private static readonly TimeSpan ResizeSettle = TimeSpan.FromMilliseconds(300);

    private readonly Window _main;
    private readonly List<Window> _windows = new();
    private readonly List<(Window Window, double OffsetX, double OffsetY)> _carried = new();
    // On the Mac, windows that belong to the player are moved along with it by macOS; those that
    // aren't docked to it are put back where they were.
    private readonly List<(Window Window, PixelPoint Position)> _staying = new();
    private readonly Dictionary<Window, Box> _known = new();
    private readonly Dictionary<Window, Box> _startBoxes = new();
    private readonly DispatcherTimer _resizeEnd;
    private Window? _moving;
    private bool _dragging;
    private Point _startCursor;
    private Box _startRect;
    private bool _resized;
    private bool _resizeAlone;
    private Sides _resizeSides;
    private bool _adjusting;

    /// <summary>The windows resizing along with the one being resized, found when the resize starts.</summary>
    private readonly List<Linked> _linked = new();

    public WindowSnapper(Window main)
    {
        _main = main;
        _resizeEnd = new DispatcherTimer { Interval = ResizeSettle };
        _resizeEnd.Tick += (_, _) => FinishResize();
        Add(main);
    }

    /// <summary>Raised when the user finishes moving or resizing one of the windows.</summary>
    public event EventHandler? MoveFinished;

    /// <summary>
    /// True while every window moves with the player, docked to it or not (the player's link button).
    /// Locked windows still stay put.
    /// </summary>
    public Func<bool>? MoveEveryWindow { get; set; }

    public void Add(Window window)
    {
        if (_windows.Contains(window))
        {
            return;
        }

        _windows.Add(window);
        window.Closed += (_, _) =>
        {
            _windows.Remove(window);
            _known.Remove(window);
        };
        window.AddHandler(InputElement.PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
        window.AddHandler(InputElement.PointerMovedEvent, OnPointerMoved, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        window.AddHandler(InputElement.PointerCaptureLostEvent, (_, _) => FinishMove(), RoutingStrategies.Direct | RoutingStrategies.Bubble, handledEventsToo: true);
        window.PositionChanged += (_, _) => OnBoundsChanged(window, null);
        window.Resized += (_, e) => OnBoundsChanged(window, e.Reason);
    }

    // ---- Moving ----

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Window window || e.Handled || !e.GetCurrentPoint(window).Properties.IsLeftButtonPressed
            || e.ClickCount > 1 || window.WindowState != WindowState.Normal || FullScreen.Fills(window) || !OnCaption(e.Source as Visual, window))
        {
            return;
        }

        if (WindowLocks.IsLocked(window))
        {
            // Locked in place: its title bar doesn't move it (handled, so macOS doesn't either).
            e.Handled = true;
            return;
        }

        _moving = window;
        _dragging = true;
        _resized = false;
        _carried.Clear();
        _staying.Clear();
        _linked.Clear();
        _startCursor = Cursor(window, e);
        _startRect = BoxOf(window);
        if (window == _main)
        {
            FindDocked();
        }

        e.Pointer.Capture(window);
        // Handled, so the title bar doesn't also start the system's own window drag.
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || sender != _moving || _moving is not { } window)
        {
            return;
        }

        var rect = UnsnappedMove(Cursor(window, e));
        var (dx, dy) = SnapMove(window, rect);
        rect = rect.Offset(dx, dy);
        _adjusting = true;
        try
        {
            window.MoveTo(rect.Left, rect.Top);
            if (_carried.Count > 0)
            {
                MoveTogether(rect);
            }
        }
        finally
        {
            _adjusting = false;
        }

        e.Handled = true;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging && sender == _moving)
        {
            e.Pointer.Capture(null);
            FinishMove();
            e.Handled = true;
        }
    }

    private void FinishMove()
    {
        if (!_dragging || _moving is not { } window)
        {
            return;
        }

        _dragging = false;
        if (!_resized && _carried.Count == 0)
        {
            MatchCorners(window);
        }

        _moving = null;
        _carried.Clear();
        _staying.Clear();
        _linked.Clear();
        MoveFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True when the press landed on a title bar, and not on a button or other control in it.</summary>
    private static bool OnCaption(Visual? source, Window window)
    {
        for (var element = source; element is not null && element != window; element = element.GetVisualParent())
        {
            if (element is Button or Slider or TextBox or ComboBox or ListBoxItem or Thumb)
            {
                return false;
            }

            if (element is Control control && Chrome.GetIsCaption(control))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Where the mouse is on the screen, in the window's units.</summary>
    private static Point Cursor(Window window, PointerEventArgs e)
    {
        var screen = window.PointToScreen(e.GetPosition(window));
        var scale = Placement.Scale(window);
        return new Point(screen.X / scale, screen.Y / scale);
    }

    /// <summary>
    /// Moves the windows docked to the player along with it, and puts back any of the player's other
    /// windows that macOS moved with it.
    /// </summary>
    private void MoveTogether(Box rect)
    {
        foreach (var (window, offsetX, offsetY) in _carried)
        {
            window.MoveTo(rect.Left + offsetX, rect.Top + offsetY);
        }

        foreach (var (window, position) in _staying)
        {
            if (window.Position != position)
            {
                window.Position = position;
            }
        }
    }

    /// <summary>Where the window being dragged would be with no snapping: where it started, plus how far the mouse has moved.</summary>
    private Box UnsnappedMove(Point cursor) => _startRect.Offset(cursor.X - _startCursor.X, cursor.Y - _startCursor.Y);

    /// <summary>
    /// Every window touching the player, or touching a window that touches it, moves with it.
    /// Hidden windows count too, so a docked window that's switched off is still docked when it's switched back on.
    /// </summary>
    private void FindDocked()
    {
        var mainRect = BoxOf(_main);
        var group = new List<(Window Window, Box Rect)> { (_main, mainRect) };
        var others = DockableWindows().ToList();
        var locked = others.Where(other => WindowLocks.IsLocked(other.Window)).ToList();
        others.RemoveAll(other => WindowLocks.IsLocked(other.Window));
        if (MoveEveryWindow?.Invoke() == true)
        {
            // Linked: they all go with the player, wherever they are.
            group.AddRange(others);
            others.Clear();
        }

        for (var i = 0; i < group.Count; i++)
        {
            foreach (var other in others.ToList())
            {
                if (Touching(group[i].Rect, other.Rect))
                {
                    group.Add(other);
                    others.Remove(other);
                }
            }
        }

        foreach (var (window, rect) in group.Skip(1))
        {
            _carried.Add((window, rect.Left - mainRect.Left, rect.Top - mainRect.Top));
        }

        // Locked windows stay where they are, docked or not.
        foreach (var (window, _) in others.Concat(locked))
        {
            _staying.Add((window, window.Position));
        }
    }

    // ---- Resizing ----

    /// <summary>
    /// Follows a window's position and size. A size change the user is making (by dragging an edge)
    /// starts or continues a resize; anything else is just remembered as where the window is.
    /// </summary>
    private void OnBoundsChanged(Window window, WindowResizeReason? reason)
    {
        if (_adjusting || _dragging)
        {
            _known[window] = BoxOf(window);
            return;
        }

        var byUser = reason == WindowResizeReason.User
            || (reason is null or WindowResizeReason.Unspecified) && _moving == window && _resized
            || reason == WindowResizeReason.Unspecified && AppKit.IsMouseDown;
        if (!byUser || window.WindowState != WindowState.Normal)
        {
            if (_moving != window)
            {
                _known[window] = BoxOf(window);
            }

            return;
        }

        if (_moving != window)
        {
            // A resize starts: remember where everything was, and whether ⌘ is held to resize this one alone.
            FinishResize();
            _moving = window;
            _resized = true;
            _carried.Clear();
            _linked.Clear();
            _resizeSides = Sides.None;
            _resizeAlone = AppKit.IsCommandDown;
            _startRect = _known.TryGetValue(window, out var known) ? known : BoxOf(window);
            _startBoxes.Clear();
            foreach (var other in _windows)
            {
                _startBoxes[other] = _known.TryGetValue(other, out var box) ? box : BoxOf(other);
            }

            _startBoxes[window] = _startRect;
        }

        var rect = BoxOf(window);
        var sides = SidesMoved(_startRect, rect);
        if ((sides & ~_resizeSides) != Sides.None)
        {
            _resizeSides |= sides;
            if (!_resizeAlone)
            {
                _linked.Clear();
                FindLinked(window, _resizeSides);
            }
        }

        _adjusting = true;
        try
        {
            MoveLinked(ref rect);
        }
        finally
        {
            _adjusting = false;
        }

        _resizeEnd.Stop();
        _resizeEnd.Start();
    }

    /// <summary>The edges that have moved since the resize began.</summary>
    private static Sides SidesMoved(Box start, Box now)
    {
        var sides = Sides.None;
        if (Math.Abs(now.Left - start.Left) > 0.5)
        {
            sides |= Sides.LeftSide;
        }
        else if (Math.Abs(now.Right - start.Right) > 0.5)
        {
            sides |= Sides.RightSide;
        }

        if (Math.Abs(now.Top - start.Top) > 0.5)
        {
            sides |= Sides.TopSide;
        }
        else if (Math.Abs(now.Bottom - start.Bottom) > 0.5)
        {
            sides |= Sides.BottomSide;
        }

        return sides;
    }

    /// <summary>The resize has ended: snap the resized edges, bring the linked windows along, and remember it all.</summary>
    private void FinishResize()
    {
        _resizeEnd.Stop();
        if (!_resized || _moving is not { } window)
        {
            return;
        }

        _adjusting = true;
        try
        {
            var rect = BoxOf(window);
            // The player keeps the shape macOS held it to, so its edges aren't snapped.
            if (window != _main)
            {
                SnapSize(window, ref rect, _resizeSides);
            }

            MoveLinked(ref rect);
            var current = BoxOf(window);
            if (!rect.Equals(current))
            {
                Placement.Place(window, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            }
        }
        finally
        {
            _adjusting = false;
        }

        foreach (var other in _windows)
        {
            _known[other] = BoxOf(other);
        }

        _moving = null;
        _resized = false;
        _linked.Clear();
        _startBoxes.Clear();
        MoveFinished?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Finds the windows that resize along with this one. For each edge being dragged: a docked
    /// window on the other side of it gives way (its facing edge follows), and a docked window
    /// whose own edge runs along the same line goes with it (that edge follows too). The player
    /// isn't resized by other windows, so where it sits on the line nothing follows that edge, and a window
    /// that's also docked to something that isn't moving stays put.
    /// </summary>
    private void FindLinked(Window window, Sides edges)
    {
        const double flush = 2;
        var self = _startRect;
        var group = Group(window, self);
        var sides = new List<Side>();
        if (edges.HasFlag(Sides.LeftSide))
        {
            sides.Add(Side.Left);
        }
        else if (edges.HasFlag(Sides.RightSide))
        {
            sides.Add(Side.Right);
        }

        // The player keeps its shape, so dragging its top or bottom moves a side edge too (its right one).
        // The windows under it move with its bottom edge as its height changes, like when it changes skin.
        if (window == _main)
        {
            if (sides.Count == 0)
            {
                sides.Add(Side.Right);
            }
        }
        else if (edges.HasFlag(Sides.TopSide))
        {
            sides.Add(Side.Top);
        }
        else if (edges.HasFlag(Sides.BottomSide))
        {
            sides.Add(Side.Bottom);
        }

        var follows = new Dictionary<Window, Linked>();
        Linked For(Window other, Box rect) =>
            follows.TryGetValue(other, out var linked) ? linked : follows[other] = new Linked(other, rect, (other.MinWidth, other.MinHeight));

        foreach (var side in sides)
        {
            var line = Get(self, side);
            var opposite = Opposite(side);

            // Windows whose edge runs along the same line go with it.
            var along = group.Where(other => Math.Abs(Get(other.Rect, side) - line) <= flush).ToList();
            if (along.Any(other => !Resizable(other.Window)))
            {
                along.Clear();
            }

            foreach (var other in along)
            {
                For(other.Window, other.Rect).Follow(side, side);
            }

            // Windows on the other side of the line give way, unless something else docked to them stays put.
            var facing = group.Where(other => Math.Abs(Get(other.Rect, opposite) - line) <= flush && Across(self, other.Rect, side)).ToList();
            if (facing.Any(other => !Resizable(other.Window)))
            {
                facing.Clear();
            }

            foreach (var other in facing)
            {
                var pinned = group.Any(third => third.Window != other.Window
                    && Math.Abs(Get(third.Rect, side) - line) <= flush
                    && Across(third.Rect, other.Rect, side)
                    && !along.Any(moving => moving.Window == third.Window));
                if (!pinned)
                {
                    For(other.Window, other.Rect).Follow(opposite, side);
                }
            }
        }

        _linked.AddRange(follows.Values);
    }

    /// <summary>
    /// True for a window another window's resize can resize along with it. Not the player: it keeps its
    /// shape, so only dragging its own edges resizes it.
    /// </summary>
    private bool Resizable(Window window) => window != _main && window.CanResize;

    /// <summary>Moves the linked windows with the edges of the one being resized, keeping each above its minimum size.</summary>
    private void MoveLinked(ref Box rect)
    {
        if (_linked.Count == 0)
        {
            return;
        }

        var start = _startRect;
        foreach (var side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
        {
            var delta = Get(rect, side) - Get(start, side);
            foreach (var linked in _linked)
            {
                foreach (var (own, driver) in linked.Edges)
                {
                    if (driver != side)
                    {
                        continue;
                    }

                    // How far this edge can move before the window gets too small.
                    var horizontal = own is Side.Left or Side.Right;
                    var room = horizontal ? linked.Start.Right - linked.Start.Left - linked.Min.Width : linked.Start.Bottom - linked.Start.Top - linked.Min.Height;
                    delta = own is Side.Left or Side.Top ? Math.Min(delta, Math.Max(0, room)) : Math.Max(delta, -Math.Max(0, room));
                }
            }

            Set(ref rect, side, Get(start, side) + delta);
        }

        foreach (var linked in _linked)
        {
            // The edges that don't follow stay where they are now: where the resize found them, unless
            // something else moved them since (the windows under the player follow its bottom edge).
            var target = BoxOf(linked.Window);
            foreach (var (own, driver) in linked.Edges)
            {
                Set(ref target, own, Get(linked.Start, own) + Get(rect, driver) - Get(start, driver));
            }

            Placement.Place(linked.Window, target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top);
        }
    }

    /// <summary>The shown windows docked to this one, directly or through others, as they were when the resize began.</summary>
    private List<(Window Window, Box Rect)> Group(Window window, Box self)
    {
        var group = new List<(Window Window, Box Rect)> { (window, self) };
        var others = OtherWindows(window).Select(other => (other.Window, _startBoxes.TryGetValue(other.Window, out var box) ? box : other.Rect)).ToList();
        for (var i = 0; i < group.Count; i++)
        {
            foreach (var other in others.ToList())
            {
                if (Touching(group[i].Rect, other.Item2))
                {
                    group.Add(other);
                    others.Remove(other);
                }
            }
        }

        group.RemoveAt(0);
        return group;
    }

    /// <summary>True when the two rectangles overlap along the line of this side (so they meet across it, rather than corner to corner).</summary>
    private static bool Across(Box a, Box b, Side side) => side is Side.Left or Side.Right
        ? a.Top < b.Bottom && b.Top < a.Bottom
        : a.Left < b.Right && b.Left < a.Right;

    private static Side Opposite(Side side) => side switch
    {
        Side.Left => Side.Right,
        Side.Right => Side.Left,
        Side.Top => Side.Bottom,
        _ => Side.Top,
    };

    private static double Get(Box rect, Side side) => side switch
    {
        Side.Left => rect.Left,
        Side.Top => rect.Top,
        Side.Right => rect.Right,
        _ => rect.Bottom,
    };

    private static void Set(ref Box rect, Side side, double value)
    {
        switch (side)
        {
            case Side.Left:
                rect.Left = value;
                break;
            case Side.Top:
                rect.Top = value;
                break;
            case Side.Right:
                rect.Right = value;
                break;
            default:
                rect.Bottom = value;
                break;
        }
    }

    private enum Side
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    [Flags]
    private enum Sides
    {
        None = 0,
        LeftSide = 1,
        TopSide = 2,
        RightSide = 4,
        BottomSide = 8,
    }

    /// <summary>A window resizing along with the one being dragged: which of its edges follow which of the dragged edges.</summary>
    private sealed class Linked
    {
        public Linked(Window window, Box start, (double Width, double Height) min)
        {
            Window = window;
            Start = start;
            Min = min;
        }

        public Window Window { get; }

        public Box Start { get; }

        public (double Width, double Height) Min { get; }

        public List<(Side Own, Side Driver)> Edges { get; } = new();

        public void Follow(Side own, Side driver)
        {
            if (!Edges.Any(edge => edge.Own == own))
            {
                Edges.Add((own, driver));
            }
        }
    }

    // ---- Lining up ----

    /// <summary>
    /// After a window is dropped beside others with one corner lined up, stretches it so the
    /// other corner lines up too, if it's close: a window dropped beside the player and the queue
    /// takes the height of the two together. Only for windows that can be resized.
    /// </summary>
    private void MatchCorners(Window window)
    {
        if (!Resizable(window) || window.WindowState != WindowState.Normal)
        {
            return;
        }

        const double flush = 2;
        var reach = CornerDistance;
        var rect = BoxOf(window);
        var others = OtherWindows(window).Select(other => other.Rect).ToList();
        var minWidth = window.MinWidth;
        var minHeight = window.MinHeight;

        // The windows touching each side, taken together as one column or row.
        Box? Together(Func<Box, bool> touching)
        {
            var side = others.Where(touching).ToList();
            return side.Count == 0
                ? null
                : new Box { Left = side.Min(r => r.Left), Top = side.Min(r => r.Top), Right = side.Max(r => r.Right), Bottom = side.Max(r => r.Bottom) };
        }

        static bool Near(double gap, double reach) => Math.Abs(gap) > 0.5 && Math.Abs(gap) <= reach;

        var target = rect;
        foreach (var column in new[]
                 {
                     Together(r => Math.Abs(r.Right - rect.Left) <= flush && r.Top < rect.Bottom && rect.Top < r.Bottom),
                     Together(r => Math.Abs(r.Left - rect.Right) <= flush && r.Top < rect.Bottom && rect.Top < r.Bottom),
                 })
        {
            if (column is not { } c || target.Top != rect.Top || target.Bottom != rect.Bottom)
            {
                continue;
            }

            if (Math.Abs(c.Top - rect.Top) <= flush && Near(c.Bottom - rect.Bottom, reach) && c.Bottom - rect.Top >= minHeight)
            {
                target.Top = c.Top;
                target.Bottom = c.Bottom;
            }
            else if (Math.Abs(c.Bottom - rect.Bottom) <= flush && Near(c.Top - rect.Top, reach) && rect.Bottom - c.Top >= minHeight)
            {
                target.Top = c.Top;
                target.Bottom = c.Bottom;
            }
        }

        foreach (var row in new[]
                 {
                     Together(r => Math.Abs(r.Bottom - rect.Top) <= flush && r.Left < rect.Right && rect.Left < r.Right),
                     Together(r => Math.Abs(r.Top - rect.Bottom) <= flush && r.Left < rect.Right && rect.Left < r.Right),
                 })
        {
            if (row is not { } w || target.Left != rect.Left || target.Right != rect.Right)
            {
                continue;
            }

            if (Math.Abs(w.Left - rect.Left) <= flush && Near(w.Right - rect.Right, reach) && w.Right - rect.Left >= minWidth)
            {
                target.Left = w.Left;
                target.Right = w.Right;
            }
            else if (Math.Abs(w.Right - rect.Right) <= flush && Near(w.Left - rect.Left, reach) && rect.Right - w.Left >= minWidth)
            {
                target.Left = w.Left;
                target.Right = w.Right;
            }
        }

        if (target.Equals(rect))
        {
            return;
        }

        _adjusting = true;
        try
        {
            Placement.Place(window, target.Left, target.Top, target.Right - target.Left, target.Bottom - target.Top);
        }
        finally
        {
            _adjusting = false;
        }
    }

    // ---- Snapping ----

    private static bool Touching(Box a, Box b)
    {
        const double tolerance = 2;
        var overlapX = a.Left < b.Right && b.Left < a.Right;
        var overlapY = a.Top < b.Bottom && b.Top < a.Bottom;
        var sideBySide = overlapY && (Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance);
        var stacked = overlapX && (Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance);
        return sideBySide || stacked;
    }

    /// <summary>The visible Lucent windows other than this one (and the ones it's carrying or resizing).</summary>
    private IEnumerable<(Window Window, Box Rect)> OtherWindows(Window self)
    {
        foreach (var window in _windows)
        {
            if (window == self || !window.IsVisible || window.WindowState != WindowState.Normal || FullScreen.Fills(window)
                || _carried.Any(carried => carried.Window == window) || _linked.Any(linked => linked.Window == window))
            {
                continue;
            }

            yield return (window, BoxOf(window));
        }
    }

    /// <summary>Lucent's other windows that could be docked to the player, shown or hidden.</summary>
    private IEnumerable<(Window Window, Box Rect)> DockableWindows()
    {
        foreach (var window in _windows)
        {
            if (window != _main && window.PlatformImpl is not null && window.WindowState == WindowState.Normal && !FullScreen.Fills(window))
            {
                yield return (window, BoxOf(window));
            }
        }
    }

    private (double Dx, double Dy) SnapMove(Window window, Box rect)
    {
        var reach = SnapDistance;
        var bestX = reach + 1;
        var bestY = reach + 1;
        foreach (var target in Targets(window, rect))
        {
            var overlapY = rect.Top < target.Bottom + reach && target.Top < rect.Bottom + reach;
            var overlapX = rect.Left < target.Right + reach && target.Left < rect.Right + reach;
            if (overlapY || target.IsScreen)
            {
                Consider(ref bestX, target.Left - rect.Right);
                Consider(ref bestX, target.Right - rect.Left);
                Consider(ref bestX, target.Left - rect.Left);
                Consider(ref bestX, target.Right - rect.Right);
            }

            if (overlapX || target.IsScreen)
            {
                Consider(ref bestY, target.Top - rect.Bottom);
                Consider(ref bestY, target.Bottom - rect.Top);
                Consider(ref bestY, target.Top - rect.Top);
                Consider(ref bestY, target.Bottom - rect.Bottom);
            }
        }

        return KeepApart(window, rect, Math.Abs(bestX) <= reach ? bestX : 0, Math.Abs(bestY) <= reach ? bestY : 0);
    }

    /// <summary>
    /// When the window has been pushed a little way into another of Lucent's windows, moves it back
    /// out against that window's nearest edge. Pushed further in than that, it's meant to go over it.
    /// </summary>
    private (double Dx, double Dy) KeepApart(Window window, Box rect, double dx, double dy)
    {
        var resist = OverlapDistance;
        var others = OtherWindows(window).Select(other => other.Rect).ToList();

        // Pushing out of one window can land in the one beside it, so look twice.
        for (var pass = 0; pass < 2; pass++)
        {
            var moved = rect.Offset(dx, dy);
            if (others.Where(other => Overlapping(moved, other)).Cast<Box?>().FirstOrDefault() is not { } into)
            {
                break;
            }

            var best = (X: 0.0, Y: 0.0);
            var bestDistance = resist + 1;
            foreach (var (x, y) in new[] { (into.Left - moved.Right, 0.0), (into.Right - moved.Left, 0.0), (0.0, into.Top - moved.Bottom), (0.0, into.Bottom - moved.Top) })
            {
                var distance = Math.Abs(x) + Math.Abs(y);
                var outside = moved.Offset(x, y);
                if (distance < bestDistance && !others.Any(other => Overlapping(outside, other)))
                {
                    best = (x, y);
                    bestDistance = distance;
                }
            }

            if (bestDistance > resist)
            {
                break;
            }

            dx += best.X;
            dy += best.Y;
        }

        return (dx, dy);
    }

    /// <summary>True when the rectangles cover some of the same area, more than the slack allowed for edges that touch.</summary>
    private static bool Overlapping(Box a, Box b)
    {
        const double flush = 2;
        return a.Left < b.Right - flush && b.Left < a.Right - flush && a.Top < b.Bottom - flush && b.Top < a.Bottom - flush;
    }

    private void SnapSize(Window window, ref Box rect, Sides edges)
    {
        var reach = SnapDistance;
        var left = edges.HasFlag(Sides.LeftSide);
        var right = !left && edges.HasFlag(Sides.RightSide);
        var top = edges.HasFlag(Sides.TopSide);
        var bottom = !top && edges.HasFlag(Sides.BottomSide);

        var bestX = reach + 1;
        var bestY = reach + 1;
        var moving = rect;
        foreach (var target in Targets(window, rect))
        {
            var overlapY = moving.Top < target.Bottom + reach && target.Top < moving.Bottom + reach;
            var overlapX = moving.Left < target.Right + reach && target.Left < moving.Right + reach;
            var edgeX = left ? moving.Left : moving.Right;
            var edgeY = top ? moving.Top : moving.Bottom;
            if ((left || right) && (overlapY || target.IsScreen))
            {
                Consider(ref bestX, target.Left - edgeX);
                Consider(ref bestX, target.Right - edgeX);
            }

            if ((top || bottom) && (overlapX || target.IsScreen))
            {
                Consider(ref bestY, target.Top - edgeY);
                Consider(ref bestY, target.Bottom - edgeY);
            }
        }

        if (Math.Abs(bestX) <= reach)
        {
            if (left)
            {
                rect.Left += bestX;
            }
            else if (right)
            {
                rect.Right += bestX;
            }
        }

        if (Math.Abs(bestY) <= reach)
        {
            if (top)
            {
                rect.Top += bestY;
            }
            else if (bottom)
            {
                rect.Bottom += bestY;
            }
        }

        // An edge pulled a little way into a window it started outside of stops at that window's edge.
        const double flush = 2;
        var resist = OverlapDistance;
        var start = _startRect;
        foreach (var (_, other) in OtherWindows(window))
        {
            var besideY = rect.Top < other.Bottom - flush && other.Top < rect.Bottom - flush;
            var besideX = rect.Left < other.Right - flush && other.Left < rect.Right - flush;
            if (right && besideY && start.Right <= other.Left + flush && rect.Right > other.Left && rect.Right - other.Left <= resist)
            {
                rect.Right = other.Left;
            }
            else if (left && besideY && start.Left >= other.Right - flush && rect.Left < other.Right && other.Right - rect.Left <= resist)
            {
                rect.Left = other.Right;
            }

            if (bottom && besideX && start.Bottom <= other.Top + flush && rect.Bottom > other.Top && rect.Bottom - other.Top <= resist)
            {
                rect.Bottom = other.Top;
            }
            else if (top && besideX && start.Top >= other.Bottom - flush && rect.Top < other.Bottom && other.Bottom - rect.Top <= resist)
            {
                rect.Top = other.Bottom;
            }
        }

        // Never smaller than the window allows.
        if (rect.Right - rect.Left < window.MinWidth)
        {
            if (left)
            {
                rect.Left = rect.Right - window.MinWidth;
            }
            else
            {
                rect.Right = rect.Left + window.MinWidth;
            }
        }

        if (rect.Bottom - rect.Top < window.MinHeight)
        {
            if (top)
            {
                rect.Top = rect.Bottom - window.MinHeight;
            }
            else
            {
                rect.Bottom = rect.Top + window.MinHeight;
            }
        }
    }

    private static void Consider(ref double best, double candidate)
    {
        if (Math.Abs(candidate) < Math.Abs(best))
        {
            best = candidate;
        }
    }

    private IEnumerable<SnapTarget> Targets(Window window, Box rect)
    {
        foreach (var (_, other) in OtherWindows(window))
        {
            yield return new SnapTarget(other.Left, other.Top, other.Right, other.Bottom, false);
        }

        var work = Placement.WorkAreaAt(window, new Rect(rect.Left, rect.Top, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top)));
        yield return new SnapTarget(work.Left, work.Top, work.Right, work.Bottom, true);
    }

    /// <summary>
    /// The part of the window you can see. Lucent's windows draw their whole frame themselves (no
    /// system title bar), so that's the window's own rectangle.
    /// </summary>
    private static Box BoxOf(Window window)
    {
        var bounds = Placement.BoundsOf(window);
        return new Box { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
    }

    private readonly record struct SnapTarget(double Left, double Top, double Right, double Bottom, bool IsScreen);

    /// <summary>A rectangle by its edges, which the snapping moves one at a time.</summary>
    private record struct Box
    {
        public double Left;
        public double Top;
        public double Right;
        public double Bottom;

        public readonly Box Offset(double dx, double dy) => new()
        {
            Left = Left + dx,
            Top = Top + dy,
            Right = Right + dx,
            Bottom = Bottom + dy,
        };
    }
}
