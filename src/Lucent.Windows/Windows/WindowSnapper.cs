using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Lucent.Layout;

namespace Lucent.Windows;

/// <summary>
/// Makes Lucent's windows behave like Winamp's: their edges snap to each other and to the
/// edges of the screen, and dragging the player window carries every window docked to it.
/// Drop a window beside others with one corner lined up and the other corner nearly lined up,
/// and it stretches to line that one up too. Resizing a window resizes the windows docked to it
/// along that edge: the edge between two windows moves both, and an outside edge shared by several
/// windows moves them all. Hold Ctrl while resizing to resize just the one window.
/// A window that's locked (<see cref="WindowLocks"/>) stays where it is: it can't be dragged or resized,
/// and nothing carries or resizes it along with another. With <see cref="MoveEveryWindow"/> on, dragging
/// the player carries every window, docked to it or not.
/// Drag a window by its title bar and drop it with the mouse well inside another, and the two swap places
/// and sizes (<see cref="WindowSwap"/>); an outline over the other window shows when a drop will swap.
/// The player and the windows it carries snap as one group, and no title bar can be dragged up off the top
/// of the screen. A window dropped just short of another (a gap a little wider than snapping reaches)
/// closes the gap and docks. Dragging the player away from a locked window that's docked to it (or linked)
/// outlines the locked window, saying it's locked, and trying to drag a locked window says so too.
/// Works on the raw window rectangles (physical pixels) while Windows moves them.
/// </summary>
/// <remarks>
/// Windows works out each step of a drag from where the window was after the previous step, so
/// snapping that rectangle directly makes a window stick: once it has jumped to an edge, every small
/// mouse movement lands it back inside the snap distance and it jumps back again. So the snapper
/// works out where the window would be without snapping (from how far the mouse has moved since
/// the drag began) and snaps that. Pull the mouse further than the snap distance and the window lets go.
/// </remarks>
public sealed class WindowSnapper
{
    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_NCRBUTTONUP = 0x00A5;
    private const int HTCAPTION = 2;

    private const int SC_SIZE = 0xF000;
    private const int SC_MOVE = 0xF010;

    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    private const int VK_CONTROL = 0x11;
    private const int VK_LBUTTON = 0x01;
    private const uint GW_HWNDNEXT = 2;

    /// <summary>How close (in pixels at 100% scaling) an edge has to come before it snaps.</summary>
    private const double SnapDistance = 16;

    /// <summary>How far (in pixels at 100% scaling) a dropped window will stretch to line up its other corner.</summary>
    private const double CornerDistance = 40;

    /// <summary>
    /// How far (in pixels at 100% scaling) a window can be pushed into another before it slides over it.
    /// Up to this, it stays against that window's edge instead, so windows don't end up overlapping by accident.
    /// </summary>
    private const double OverlapDistance = 64;

    /// <summary>
    /// How wide a gap (in pixels at 100% scaling) a window dropped beside another closes, docking to it. Wider
    /// than the snap distance, since a window let go of just outside that would otherwise sit a sliver away.
    /// </summary>
    private const double DropDistance = 32;

    /// <summary>How far (in pixels at 100% scaling) the mouse moves on a locked window's title bar before it says it's locked.</summary>
    private const double LockedNudge = 4;

    private readonly Window _main;
    private readonly List<Window> _windows = new();

    /// <summary>The windows it snaps, less those that are panels in the single window (they're out of sight, their insides lent to it).</summary>
    private IEnumerable<Window> Live => _windows.Where(window => !SingleWindow.Holds(window));
    private readonly List<Carried> _carried = new();

    /// <summary>
    /// Where each window the player carries was as the drag began, against the player's top left corner (in
    /// pixels): so the group snaps by its outside edges, and stays on the screen, as one.
    /// </summary>
    private readonly List<Rect> _carriedOffsets = new();

    /// <summary>Locked windows the player's being dragged away from (docked to it, or linked), outlined while it moves.</summary>
    private readonly List<IntPtr> _leftBehind = new();

    private readonly List<SwapHighlight> _lockOutlines = new();

    /// <summary>Where the player is right now during a drag that carries other windows, and its scale there.</summary>
    private Rect _carrierRect;
    private double _carrierScale = 1;
    private IntPtr _moving;
    private Point _startCursor;
    private Rect _startRect;
    private Rect _startMargins;
    private bool _resized;

    /// <summary>True while a dropped group is lined up again around the player (<see cref="Settle"/>).</summary>
    private bool _settling;

    /// <summary>True while Windows is moving or resizing a locked window, which is held where it started.</summary>
    private bool _held;

    /// <summary>True once this resize has said a locked window is stopping it, so it's said only once.</summary>
    private bool _saidLocked;

    /// <summary>The windows resizing along with the one being resized, found when the resize starts.</summary>
    private readonly List<Linked> _linked = new();

    /// <summary>The window the one being dragged swaps with if it's dropped now, if any.</summary>
    private IntPtr _swapWith;

    /// <summary>Lucent's windows from front to back as the drag began, so the one in front decides where they overlap.</summary>
    private readonly List<IntPtr> _frontToBack = new();

    private SwapHighlight? _highlight;

    public WindowSnapper(Window main)
    {
        _main = main;
        Add(main);
    }

    /// <summary>
    /// True while a window is being dragged (not resized). The snapper places the windows then, so
    /// nothing else should move or resize them until the drag ends.
    /// </summary>
    public bool IsDragging => _moving != IntPtr.Zero && !_resized && !_held;

    /// <summary>Raised when the user finishes moving or resizing one of the windows.</summary>
    public event EventHandler? MoveFinished;

    /// <summary>
    /// The player's shape (its height over its width), which it keeps while an edge is dragged, since it
    /// scales as a whole. Null while the player can't be resized.
    /// </summary>
    public Func<double?>? PlayerShape { get; set; }

    /// <summary>
    /// True while every window moves with the player, docked to it or not (the player's link button).
    /// Locked windows still stay put.
    /// </summary>
    public Func<bool>? MoveEveryWindow { get; set; }

    /// <summary>Raised when a window dragged onto another swaps places with it: the dragged one, then the other.</summary>
    public event Action<Window, Window>? Swapped;

    public void Add(Window window)
    {
        // The swap outline isn't one of the windows: nothing snaps to it or swaps with it.
        if (_windows.Contains(window) || window is SwapHighlight)
        {
            return;
        }

        _windows.Add(window);
        window.Closed += (_, _) => _windows.Remove(window);
        if (PresentationSource.FromVisual(window) is HwndSource source)
        {
            source.AddHook(WndProc);
        }
        else
        {
            window.SourceInitialized += (_, _) => (PresentationSource.FromVisual(window) as HwndSource)?.AddHook(WndProc);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            // A right-click on a title bar: Lucent's own menu, with "Full screen Lucent", instead of Windows' one.
            case WM_NCRBUTTONUP when wParam.ToInt64() == HTCAPTION
                && HwndSource.FromHwnd(hwnd)?.RootVisual is Window titled && LucentFullScreen.ShowWindowMenu(titled):
                handled = true;
                break;
            // A locked window doesn't start moving or resizing at all (its title bar and edges do nothing), but
            // says why if the mouse is dragged.
            case WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) is SC_MOVE or SC_SIZE && IsLocked(hwnd):
                handled = true;
                WatchLockedDrag(hwnd);
                break;
            case WM_ENTERSIZEMOVE when IsLocked(hwnd):
                // Something got it moving anyway: it's held where it is until it lets go.
                _moving = hwnd;
                _held = true;
                GetWindowRect(hwnd, out _startRect);
                break;
            case WM_MOVING or WM_SIZING when hwnd == _moving && _held:
                Marshal.StructureToPtr(_startRect, lParam, false);
                break;
            case WM_EXITSIZEMOVE when hwnd == _moving && _held:
                _moving = IntPtr.Zero;
                _held = false;
                break;
            case WM_ENTERSIZEMOVE:
                _moving = hwnd;
                _resized = false;
                _saidLocked = false;
                _carried.Clear();
                _carriedOffsets.Clear();
                _leftBehind.Clear();
                _linked.Clear();
                _swapWith = IntPtr.Zero;
                FindFrontToBack();
                GetCursorPos(out _startCursor);
                GetWindowRect(hwnd, out _startRect);
                _startMargins = Margins(hwnd);
                if (hwnd == Handle(_main))
                {
                    FindDocked(hwnd);
                }

                break;
            case WM_MOVING when hwnd == _moving:
            {
                // Dragging a maximized window restores it first, at a new size under the mouse;
                // start counting the mouse's movement again from there.
                var proposed = Marshal.PtrToStructure<Rect>(lParam);
                if (proposed.Right - proposed.Left != _startRect.Right - _startRect.Left
                    || proposed.Bottom - proposed.Top != _startRect.Bottom - _startRect.Top)
                {
                    _startRect = proposed;
                    GetCursorPos(out _startCursor);
                    _startMargins = Margins(hwnd);
                }

                var rect = UnsnappedMove();
                var (dx, dy) = SnapMove(hwnd, Visible(rect, _startMargins));
                if (_leftBehind.Count > 0 && (rect.Left + dx != _startRect.Left || rect.Top + dy != _startRect.Top))
                {
                    OutlineLeftBehind();
                }

                rect.Left += dx;
                rect.Right += dx;
                rect.Top += dy;
                rect.Bottom += dy;
                Marshal.StructureToPtr(rect, lParam, false);
                if (_carried.Count > 0)
                {
                    _carrierRect = rect;
                    _carrierScale = Scale(hwnd);
                    MoveTogether(hwnd, rect);
                }
                else
                {
                    FindSwap(hwnd);
                }

                break;
            }
            case WM_SIZING when hwnd == _moving:
            {
                var edge = wParam.ToInt32();
                if (!_resized)
                {
                    _resized = true;
                    // The player found the windows docked to it to carry them, but it's being resized, not moved.
                    _carried.Clear();
                    if (GetKeyState(VK_CONTROL) >= 0)
                    {
                        FindLinked(hwnd, edge);
                    }
                }

                var rect = Visible(UnsnappedSize(hwnd, edge), _startMargins);
                SnapSize(hwnd, ref rect, edge);
                if (hwnd == Handle(_main) && PlayerShape?.Invoke() is { } shape)
                {
                    KeepShape(hwnd, ref rect, edge, shape);
                }

                MoveLinked(ref rect);
                Marshal.StructureToPtr(Outer(rect, _startMargins), lParam, false);
                break;
            }
            case WM_DPICHANGED when (_moving != IntPtr.Zero || _settling) && _carried.Count > 0:
                OnDpiChanged(hwnd, wParam, lParam);
                break;
            case WM_EXITSIZEMOVE:
                _highlight?.Hide();
                _lockOutlines.ForEach(outline => outline.Hide());
                if (hwnd == _moving && !_resized && _swapWith != IntPtr.Zero)
                {
                    Swap(hwnd, _swapWith);
                }
                else if (hwnd == _moving && !_resized && _carried.Count == 0)
                {
                    CloseGap(hwnd);
                    if (hwnd != Handle(_main))
                    {
                        MatchCorners(hwnd);
                    }
                }

                _swapWith = IntPtr.Zero;

                if (hwnd == _moving && _carried.Count > 0)
                {
                    // Put the group back together where the player was dropped, at the scale it ended up at.
                    GetWindowRect(hwnd, out _carrierRect);
                    _carrierScale = Scale(hwnd);
                    MoveTogether(hwnd, _carrierRect);
                    Settle(hwnd, _carried.ToList());
                }

                _moving = IntPtr.Zero;
                _carried.Clear();
                _carriedOffsets.Clear();
                _leftBehind.Clear();
                _linked.Clear();
                MoveFinished?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Works out which window the one being dragged would swap with if it were dropped now (the one the
    /// mouse is well inside), and shows the outline over it, or hides the outline if there's none.
    /// </summary>
    private void FindSwap(IntPtr hwnd)
    {
        GetCursorPos(out var cursor);
        var others = OtherWindows(hwnd)
            .OrderBy(other => _frontToBack.IndexOf(other.Handle) is var index and >= 0 ? index : int.MaxValue)
            .ToList();
        var target = WindowSwap.TargetAt(cursor.X, cursor.Y, Candidate(hwnd, Visible(_startRect, _startMargins)),
            others.Select(other => Candidate(other.Handle, other.Rect)).ToList(), WindowSwap.Depth * Scale(hwnd));
        _swapWith = target >= 0 ? others[target].Handle : IntPtr.Zero;
        if (_swapWith == IntPtr.Zero)
        {
            _highlight?.Hide();
            return;
        }

        var rect = others[target].Rect;
        if (_highlight is null)
        {
            var highlight = new SwapHighlight();
            _main.Closed += (_, _) => highlight.Close();
            _highlight = highlight;
        }

        _highlight.ShowAt(rect.Left, rect.Top, rect.Right, rect.Bottom, WindowLocks.NameOf(HwndSource.FromHwnd(_swapWith)?.RootVisual as Window));
    }

    /// <summary>A window as one that could swap: where it is, its smallest size, and whether it can (it's resizable and not locked).</summary>
    private SwapCandidate Candidate(IntPtr handle, Rect rect)
    {
        var min = MinSize(handle);
        return new SwapCandidate(new TileRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), min.Width, min.Height, Resizable(handle));
    }

    /// <summary>
    /// Swaps the dropped window with the one it was dropped on: it takes that window's place and size, and
    /// that window takes the place and size the dropped one had before the drag.
    /// </summary>
    private void Swap(IntPtr hwnd, IntPtr other)
    {
        var start = Visible(_startRect, _startMargins);
        var target = VisibleRect(other);
        var here = Outer(target, Margins(hwnd));
        var there = Outer(start, Margins(other));
        SetWindowPos(other, IntPtr.Zero, there.Left, there.Top, there.Right - there.Left, there.Bottom - there.Top, SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowPos(hwnd, IntPtr.Zero, here.Left, here.Top, here.Right - here.Left, here.Bottom - here.Top, SWP_NOZORDER | SWP_NOACTIVATE);
        if (HwndSource.FromHwnd(hwnd)?.RootVisual is Window dragged && HwndSource.FromHwnd(other)?.RootVisual is Window swapped)
        {
            Swapped?.Invoke(dragged, swapped);
        }
    }

    /// <summary>Notes the order Lucent's windows are in, front to back.</summary>
    private void FindFrontToBack()
    {
        _frontToBack.Clear();
        var ours = _windows.Select(Handle).Where(handle => handle != IntPtr.Zero).ToHashSet();
        for (var handle = GetTopWindow(IntPtr.Zero); handle != IntPtr.Zero && _frontToBack.Count < ours.Count; handle = GetWindow(handle, GW_HWNDNEXT))
        {
            if (ours.Contains(handle))
            {
                _frontToBack.Add(handle);
            }
        }
    }

    /// <summary>
    /// Moves the player and the windows docked to it in one go. Moved one at a time, the docked
    /// windows would land a moment before or after the player, so during a drag they'd seem to
    /// pull apart from it and overlap it by turns.
    /// </summary>
    /// <remarks>
    /// While the player is changing scale (<paramref name="includePlayer"/> false), only the docked
    /// windows move: Windows hasn't resized the player yet, and moving it at its old size to its new
    /// spot can put most of it back on the screen it just left, so it changes scale straight back.
    /// </remarks>
    private void MoveTogether(IntPtr hwnd, Rect rect, bool includePlayer = true)
    {
        const uint flags = SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE;
        var places = _carried.Select(carried =>
        {
            GetWindowRect(carried.Handle, out var own);
            return (carried.Handle, Place: carried.PlaceBeside(rect, _carrierScale, own.Right - own.Left, own.Bottom - own.Top, Scale(carried.Handle)));
        }).ToList();
        var batch = BeginDeferWindowPos(places.Count + 1);
        if (batch != IntPtr.Zero && includePlayer)
        {
            batch = DeferWindowPos(batch, hwnd, IntPtr.Zero, rect.Left, rect.Top, 0, 0, flags);
        }

        foreach (var (handle, place) in places)
        {
            if (batch != IntPtr.Zero)
            {
                batch = DeferWindowPos(batch, handle, IntPtr.Zero, place.X, place.Y, 0, 0, flags);
            }
        }

        if (batch != IntPtr.Zero && EndDeferWindowPos(batch))
        {
            return;
        }

        // Windows couldn't move them together; move them one by one instead.
        foreach (var (handle, place) in places)
        {
            SetWindowPos(handle, IntPtr.Zero, place.X, place.Y, 0, 0, flags);
        }
    }

    /// <summary>
    /// Once a dropped group has had time to change scale (each window does when it's moved mostly onto
    /// another screen, and WPF redraws it at its new size after that), lines it up around the player
    /// once more, so no window is left where it was put while it was still at its old size.
    /// </summary>
    private void Settle(IntPtr hwnd, List<Carried> carried)
    {
        _main.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (_moving != IntPtr.Zero || !GetWindowRect(hwnd, out var player))
            {
                return;
            }

            // A window that changes scale as it's moved is put beside the player as during a drag.
            _settling = true;
            _carried.AddRange(carried);
            _carrierRect = player;
            _carrierScale = Scale(hwnd);
            try
            {
                MoveTogether(hwnd, player, includePlayer: false);
            }
            finally
            {
                _carried.Clear();
                _settling = false;
            }
        }));
    }

    /// <summary>
    /// Keeps a carried group together as it crosses onto a screen with a different scale. Windows
    /// rescales each window on its own, when most of that window is over the new screen, and moves
    /// it to a spot of its own choosing; left to that, the docked windows scatter for a moment and
    /// the gaps between them open and close. So when the player changes scale, the docked windows
    /// are laid out again around it at its new scale, and when a docked window changes scale, it's
    /// put where it belongs beside the player rather than where Windows suggested.
    /// </summary>
    private void OnDpiChanged(IntPtr hwnd, IntPtr wParam, IntPtr lParam)
    {
        var suggested = Marshal.PtrToStructure<Rect>(lParam);
        var dpi = wParam.ToInt64() & 0xFFFF;
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;
        if (hwnd == _moving)
        {
            // Windows keeps the player under the mouse and puts it there itself, at its new size, once
            // this message has been handled; the docked windows follow it there.
            _carrierRect = suggested;
            _carrierScale = scale;
            MoveTogether(hwnd, suggested, includePlayer: false);
            return;
        }

        var index = _carried.FindIndex(carried => carried.Handle == hwnd);
        if (index < 0)
        {
            return;
        }

        var width = suggested.Right - suggested.Left;
        var height = suggested.Bottom - suggested.Top;
        var place = _carried[index].PlaceBeside(_carrierRect, _carrierScale, width, height, scale);
        Marshal.StructureToPtr(new Rect { Left = place.X, Top = place.Y, Right = place.X + width, Bottom = place.Y + height }, lParam, false);
    }

    /// <summary>
    /// Finds the windows that resize along with this one. For each edge being dragged: a docked
    /// window on the other side of it gives way (its facing edge follows), and a docked window
    /// whose own edge runs along the same line goes with it (that edge follows too). The player
    /// isn't resized by other windows, so where it sits on the line nothing follows that edge, and a window
    /// that's also docked to something that isn't moving stays put.
    /// </summary>
    private void FindLinked(IntPtr hwnd, int edge)
    {
        const int flush = 2;
        var self = Visible(_startRect, _startMargins);
        var group = Group(hwnd, self);
        var sides = new List<Side>();
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
        {
            sides.Add(Side.Left);
        }
        else if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT)
        {
            sides.Add(Side.Right);
        }

        // The player keeps its shape, so dragging its top or bottom moves a side edge too (its right one).
        // The windows under it move with its bottom edge as its height changes, like when it changes skin.
        if (hwnd == Handle(_main))
        {
            if (sides.Count == 0)
            {
                sides.Add(Side.Right);
            }
        }
        else if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
        {
            sides.Add(Side.Top);
        }
        else if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT)
        {
            sides.Add(Side.Bottom);
        }

        var follows = new Dictionary<IntPtr, Linked>();
        Linked For(IntPtr handle, Rect rect) =>
            follows.TryGetValue(handle, out var linked) ? linked : follows[handle] = new Linked(handle, rect, Margins(handle), MinSize(handle));

        foreach (var side in sides)
        {
            var line = Get(self, side);
            var opposite = Opposite(side);

            // Windows whose edge runs along the same line go with it.
            var along = group.Where(other => Math.Abs(Get(other.Rect, side) - line) <= flush).ToList();
            if (along.Any(other => !Resizable(other.Handle)))
            {
                along.Clear();
            }

            foreach (var other in along)
            {
                For(other.Handle, other.Rect).Follow(side, side);
            }

            // Windows on the other side of the line give way, unless something else docked to them stays put.
            var facing = group.Where(other => Math.Abs(Get(other.Rect, opposite) - line) <= flush && Across(self, other.Rect, side)).ToList();
            if (facing.Any(other => !Resizable(other.Handle)))
            {
                facing.Clear();
            }

            foreach (var other in facing)
            {

                var pinned = group.Any(third => third.Handle != other.Handle
                    && Math.Abs(Get(third.Rect, side) - line) <= flush
                    && Across(third.Rect, other.Rect, side)
                    && !along.Any(moving => moving.Handle == third.Handle));
                if (!pinned)
                {
                    For(other.Handle, other.Rect).Follow(opposite, side);
                }
            }
        }

        _linked.AddRange(follows.Values);
    }

    /// <summary>Moves the linked windows with the edges of the one being resized, keeping each above its minimum size.</summary>
    private void MoveLinked(ref Rect rect)
    {
        if (_linked.Count == 0)
        {
            return;
        }

        var start = Visible(_startRect, _startMargins);
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
                    var room = (horizontal ? linked.Start.Right - linked.Start.Left - linked.Min.Width : linked.Start.Bottom - linked.Start.Top - linked.Min.Height);
                    delta = own is Side.Left or Side.Top ? Math.Min(delta, Math.Max(0, room)) : Math.Max(delta, -Math.Max(0, room));
                }
            }

            Set(ref rect, side, Get(start, side) + delta);
        }

        foreach (var linked in _linked)
        {
            // The edges that don't follow stay where they are now: where the resize found them, unless
            // something else moved them since (the windows under the player follow its bottom edge).
            var target = VisibleRect(linked.Handle);
            foreach (var (own, driver) in linked.Edges)
            {
                Set(ref target, own, Get(linked.Start, own) + Get(rect, driver) - Get(start, driver));
            }

            var outer = Outer(target, linked.Margins);
            SetWindowPos(linked.Handle, IntPtr.Zero, outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top,
                SWP_NOZORDER | SWP_NOACTIVATE);
        }
    }

    /// <summary>The shown windows docked to this one, directly or through others.</summary>
    private List<(IntPtr Handle, Rect Rect)> Group(IntPtr hwnd, Rect self)
    {
        var group = new List<(IntPtr Handle, Rect Rect)> { (hwnd, self) };
        var others = OtherWindows(hwnd).ToList();
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

        group.RemoveAt(0);
        return group;
    }

    /// <summary>True when the two rectangles overlap along the line of this side (so they meet across it, rather than corner to corner).</summary>
    private static bool Across(Rect a, Rect b, Side side) => side is Side.Left or Side.Right
        ? a.Top < b.Bottom && b.Top < a.Bottom
        : a.Left < b.Right && b.Left < a.Right;

    /// <summary>
    /// True for a window another window's resize can resize along with it. Not the player: it keeps its
    /// shape, so only dragging its own edges resizes it. Nor a locked window.
    /// </summary>
    private bool Resizable(IntPtr handle) =>
        handle != Handle(_main)
        && HwndSource.FromHwnd(handle)?.RootVisual is Window { ResizeMode: ResizeMode.CanResize or ResizeMode.CanResizeWithGrip } window
        && !WindowLocks.IsLocked(window);

    private static bool IsLocked(IntPtr handle) => HwndSource.FromHwnd(handle)?.RootVisual is Window window && WindowLocks.IsLocked(window);

    /// <summary>
    /// Keeps the player's shape while one of its edges is dragged: the width follows the mouse (or, dragging
    /// the top or bottom, the height does and the width follows it), between the player's smallest and
    /// largest size, and the edges opposite the dragged ones stay put.
    /// </summary>
    private void KeepShape(IntPtr hwnd, ref Rect rect, int edge, double shape)
    {
        var scale = Scale(hwnd);
        var min = (int)Math.Ceiling(_main.MinWidth * scale);
        var max = double.IsInfinity(_main.MaxWidth) ? int.MaxValue : (int)Math.Floor(_main.MaxWidth * scale);
        var width = edge is WMSZ_TOP or WMSZ_BOTTOM
            ? (int)Math.Round((rect.Bottom - rect.Top) / shape)
            : rect.Right - rect.Left;
        width = Math.Clamp(width, min, Math.Max(min, max));
        var height = (int)Math.Round(width * shape);

        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
        {
            rect.Left = rect.Right - width;
        }
        else
        {
            rect.Right = rect.Left + width;
        }

        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
        {
            rect.Top = rect.Bottom - height;
        }
        else
        {
            rect.Bottom = rect.Top + height;
        }
    }

    private static (int Width, int Height) MinSize(IntPtr handle)
    {
        var window = HwndSource.FromHwnd(handle)?.RootVisual as Window;
        var scale = Scale(handle);
        return ((int)Math.Ceiling((window?.MinWidth ?? 0) * scale), (int)Math.Ceiling((window?.MinHeight ?? 0) * scale));
    }

    private static Side Opposite(Side side) => side switch
    {
        Side.Left => Side.Right,
        Side.Right => Side.Left,
        Side.Top => Side.Bottom,
        _ => Side.Top,
    };

    private static int Get(Rect rect, Side side) => side switch
    {
        Side.Left => rect.Left,
        Side.Top => rect.Top,
        Side.Right => rect.Right,
        _ => rect.Bottom,
    };

    private static void Set(ref Rect rect, Side side, int value)
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

    /// <summary>A window resizing along with the one being dragged: which of its edges follow which of the dragged edges.</summary>
    private sealed class Linked
    {
        public Linked(IntPtr handle, Rect start, Rect margins, (int Width, int Height) min)
        {
            Handle = handle;
            Start = start;
            Margins = margins;
            Min = min;
        }

        public IntPtr Handle { get; }

        public Rect Start { get; }

        public Rect Margins { get; }

        public (int Width, int Height) Min { get; }

        public List<(Side Own, Side Driver)> Edges { get; } = new();

        public void Follow(Side own, Side driver)
        {
            if (!Edges.Any(edge => edge.Own == own))
            {
                Edges.Add((own, driver));
            }
        }
    }

    /// <summary>
    /// A window docked to the player during a drag, and where it sits relative to the player, in
    /// pixels at 100% scaling so that it keeps its place when the group changes scale.
    /// </summary>
    private sealed class Carried
    {
        private readonly double _left;
        private readonly double _top;
        private readonly double _right;
        private readonly double _bottom;

        public Carried(IntPtr handle, Rect offset, double scale)
        {
            Handle = handle;
            _left = offset.Left / scale;
            _top = offset.Top / scale;
            _right = offset.Right / scale;
            _bottom = offset.Bottom / scale;
        }

        public IntPtr Handle { get; }

        /// <summary>
        /// Where the window goes, at this size and its own scale, beside the player at that rectangle and
        /// scale. A window left of (or above) the player keeps its right (or bottom) edge where it was
        /// against the player; every other window keeps its left (or top) edge in place.
        /// </summary>
        /// <remarks>
        /// A window still at another scale from the player (it's mostly on another screen) is a different
        /// size from the one it'll be beside the player, so it's centred on its place instead. Lined up by
        /// an edge, it would grow or shrink away from that edge as it changed scale, and near the line
        /// between the screens that can carry most of it back to the screen it came from, so it changes
        /// back, again and again. Centred, which screen most of it is on doesn't depend on its size.
        /// </remarks>
        public (int X, int Y) PlaceBeside(Rect player, double scale, int width, int height, double ownScale)
        {
            if (Math.Abs(ownScale - scale) > 0.01)
            {
                return (player.Left + (int)Math.Round((_left + _right) / 2 * scale) - width / 2,
                    player.Top + (int)Math.Round((_top + _bottom) / 2 * scale) - height / 2);
            }

            const double tolerance = 2;
            var x = _right * scale <= tolerance
                ? player.Left + (int)Math.Round(_right * scale) - width
                : player.Left + (int)Math.Round(_left * scale);
            var y = _bottom * scale <= tolerance
                ? player.Top + (int)Math.Round(_bottom * scale) - height
                : player.Top + (int)Math.Round(_top * scale);
            return (x, y);
        }
    }

    /// <summary>
    /// After a window is dropped beside others with one corner lined up, stretches it so the
    /// other corner lines up too, if it's close: a window dropped beside the player and the queue
    /// takes the height of the two together. Only for windows that can be resized.
    /// </summary>
    private void MatchCorners(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd)?.RootVisual is not Window { ResizeMode: ResizeMode.CanResize or ResizeMode.CanResizeWithGrip } window
            || window.WindowState != WindowState.Normal)
        {
            return;
        }

        const int flush = 2;
        var reach = (int)Math.Round(CornerDistance * Scale(hwnd));
        var rect = VisibleRect(hwnd);
        var others = OtherWindows(hwnd).Select(other => other.Rect).ToList();
        var minWidth = (int)Math.Ceiling(window.MinWidth * Scale(hwnd));
        var minHeight = (int)Math.Ceiling(window.MinHeight * Scale(hwnd));

        // The windows touching each side, taken together as one column or row.
        Rect? Together(Func<Rect, bool> touching)
        {
            var side = others.Where(touching).ToList();
            return side.Count == 0
                ? null
                : new Rect { Left = side.Min(r => r.Left), Top = side.Min(r => r.Top), Right = side.Max(r => r.Right), Bottom = side.Max(r => r.Bottom) };
        }

        static bool Near(int gap, int reach) => gap != 0 && Math.Abs(gap) <= reach;

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

        var outer = Outer(target, Margins(hwnd));
        SetWindowPos(hwnd, IntPtr.Zero, outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }

    /// <summary>Where the window being dragged would be with no snapping: where it started, plus how far the mouse has moved.</summary>
    private Rect UnsnappedMove()
    {
        GetCursorPos(out var cursor);
        var dx = cursor.X - _startCursor.X;
        var dy = cursor.Y - _startCursor.Y;
        return new Rect
        {
            Left = _startRect.Left + dx,
            Top = _startRect.Top + dy,
            Right = _startRect.Right + dx,
            Bottom = _startRect.Bottom + dy,
        };
    }

    /// <summary>The size the window would have with no snapping: the dragged edges follow the mouse, down to the window's minimum size.</summary>
    private Rect UnsnappedSize(IntPtr hwnd, int edge)
    {
        GetCursorPos(out var cursor);
        var dx = cursor.X - _startCursor.X;
        var dy = cursor.Y - _startCursor.Y;
        var scale = Scale(hwnd);
        var window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
        var minWidth = (int)Math.Ceiling((window?.MinWidth ?? 0) * scale);
        var minHeight = (int)Math.Ceiling((window?.MinHeight ?? 0) * scale);

        var rect = _startRect;
        if (edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT)
        {
            rect.Left = Math.Min(_startRect.Left + dx, _startRect.Right - minWidth);
        }
        else if (edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT)
        {
            rect.Right = Math.Max(_startRect.Right + dx, _startRect.Left + minWidth);
        }

        if (edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT)
        {
            rect.Top = Math.Min(_startRect.Top + dy, _startRect.Bottom - minHeight);
        }
        else if (edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT)
        {
            rect.Bottom = Math.Max(_startRect.Bottom + dy, _startRect.Top + minHeight);
        }

        return rect;
    }

    /// <summary>
    /// Every window touching the player, or touching a window that touches it, moves with it.
    /// Hidden windows count too, so a docked window that's switched off is still docked when it's switched back on.
    /// </summary>
    private void FindDocked(IntPtr main)
    {
        GetWindowRect(main, out var mainRect);
        var group = new List<(IntPtr Handle, Rect Rect)> { (main, VisibleRect(main)) };
        var others = DockableWindows(main).Where(other => !IsLocked(other.Handle)).ToList();
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

        _carrierRect = mainRect;
        _carrierScale = Scale(main);
        var visibleMain = group[0].Rect;
        foreach (var (handle, visible) in group.Skip(1))
        {
            GetWindowRect(handle, out var rect);
            _carried.Add(new Carried(handle, Offset(rect, -mainRect.Left, -mainRect.Top), _carrierScale));
            _carriedOffsets.Add(Offset(visible, -visibleMain.Left, -visibleMain.Top));
        }

        // The locked windows it leaves behind: shown ones docked to the group, or all shown ones when linked.
        var linked = MoveEveryWindow?.Invoke() == true;
        foreach (var window in Live)
        {
            var handle = Handle(window);
            if (handle != IntPtr.Zero && handle != main && window.IsVisible && window.WindowState == WindowState.Normal && IsLocked(handle)
                && (linked || group.Any(member => Touching(member.Rect, VisibleRect(handle)))))
            {
                _leftBehind.Add(handle);
            }
        }
    }

    /// <summary>Outlines each locked window the player's leaving behind, saying it's locked.</summary>
    private void OutlineLeftBehind()
    {
        while (_lockOutlines.Count < _leftBehind.Count)
        {
            var outline = new SwapHighlight();
            _main.Closed += (_, _) => outline.Close();
            _lockOutlines.Add(outline);
        }

        for (var i = 0; i < _leftBehind.Count; i++)
        {
            var rect = VisibleRect(_leftBehind[i]);
            _lockOutlines[i].ShowLocked(rect.Left, rect.Top, rect.Right, rect.Bottom, WindowLocks.NameOf(HwndSource.FromHwnd(_leftBehind[i])?.RootVisual as Window));
        }
    }

    /// <summary>
    /// A press on a locked window's title bar or edge: Windows doesn't move it, and if the mouse is then
    /// dragged with the button held, a note says it's locked and how to unlock it.
    /// </summary>
    private void WatchLockedDrag(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd)?.RootVisual is not Window window)
        {
            return;
        }

        GetCursorPos(out var start);
        var reach = LockedNudge * Scale(hwnd);
        var ticks = 0;
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            GetCursorPos(out var now);
            if (GetAsyncKeyState(VK_LBUTTON) >= 0 || ++ticks > 100)
            {
                timer.Stop();
            }
            else if (Math.Abs(now.X - start.X) > reach || Math.Abs(now.Y - start.Y) > reach)
            {
                timer.Stop();
                Toast.Show(window, $"{WindowLocks.NameOf(window) ?? "This window"} is locked. Click its padlock to unlock it.");
            }
        };
        timer.Start();
    }

    /// <summary>
    /// After a window's dropped on its own a little way from another (further than snapping reaches, but no
    /// further than <see cref="DropDistance"/>), moves it up against that one, so it docks instead of sitting
    /// a sliver away. Only where it'd then be over nothing.
    /// </summary>
    private void CloseGap(IntPtr hwnd)
    {
        if (HwndSource.FromHwnd(hwnd)?.RootVisual is not Window { WindowState: WindowState.Normal })
        {
            return;
        }

        const int flush = 2;
        var reach = (int)Math.Round(DropDistance * Scale(hwnd));
        var rect = VisibleRect(hwnd);
        var others = OtherWindows(hwnd).Select(other => other.Rect).ToList();
        var bestX = 0;
        var bestY = 0;
        foreach (var other in others)
        {
            var besideY = rect.Top < other.Bottom - flush && other.Top < rect.Bottom - flush;
            var besideX = rect.Left < other.Right - flush && other.Left < rect.Right - flush;
            if (besideY)
            {
                Gap(ref bestX, other.Left - rect.Right);
                Gap(ref bestX, other.Right - rect.Left);
            }

            if (besideX)
            {
                Gap(ref bestY, other.Top - rect.Bottom);
                Gap(ref bestY, other.Bottom - rect.Top);
            }
        }

        // A gap to the right (or below) is positive, one to the left (or above) negative: the nearest, inside the reach.
        void Gap(ref int best, int gap)
        {
            var away = gap > 0 ? gap : -gap;
            var facing = gap > 0 ? gap > flush : gap < -flush;
            if (facing && away <= reach && (best == 0 || away < Math.Abs(best)))
            {
                best = gap;
            }
        }

        foreach (var (dx, dy) in new[] { (bestX, bestY), (bestX, 0), (0, bestY) })
        {
            var moved = Offset(rect, dx, dy);
            if (dx == 0 && dy == 0 || others.Any(other => Overlapping(moved, other)))
            {
                continue;
            }

            var outer = Outer(moved, Margins(hwnd));
            SetWindowPos(hwnd, IntPtr.Zero, outer.Left, outer.Top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            return;
        }
    }

    private static bool Touching(Rect a, Rect b)
    {
        const int tolerance = 2;
        var overlapX = a.Left < b.Right && b.Left < a.Right;
        var overlapY = a.Top < b.Bottom && b.Top < a.Bottom;
        var sideBySide = overlapY && (Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance);
        var stacked = overlapX && (Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance);
        return sideBySide || stacked;
    }

    /// <summary>The visible Lucent windows other than this one (and the ones it's carrying), plus the screen it's on.</summary>
    private IEnumerable<(IntPtr Handle, Rect Rect)> OtherWindows(IntPtr self)
    {
        foreach (var window in Live)
        {
            var handle = Handle(window);
            if (handle == IntPtr.Zero || handle == self || !window.IsVisible || window.WindowState != WindowState.Normal
                || _carried.Any(carried => carried.Handle == handle) || _linked.Any(linked => linked.Handle == handle))
            {
                continue;
            }

            yield return (handle, VisibleRect(handle));
        }
    }

    /// <summary>Lucent's other windows that could be docked to the player, shown or hidden.</summary>
    private IEnumerable<(IntPtr Handle, Rect Rect)> DockableWindows(IntPtr main)
    {
        foreach (var window in Live)
        {
            var handle = Handle(window);
            if (handle != IntPtr.Zero && handle != main && window.WindowState == WindowState.Normal)
            {
                yield return (handle, VisibleRect(handle));
            }
        }
    }

    private (int Dx, int Dy) SnapMove(IntPtr hwnd, Rect rect)
    {
        var reach = (int)Math.Round(SnapDistance * Scale(hwnd));
        var bestX = reach + 1;
        var bestY = reach + 1;
        // The player and the windows it carries snap as one: whichever of their edges is nearest an edge.
        foreach (var member in Members(rect))
        {
            Nearest(hwnd, member, reach, ref bestX, ref bestY);
        }

        var (dx, dy) = KeepApart(hwnd, rect, Math.Abs(bestX) <= reach ? bestX : 0, Math.Abs(bestY) <= reach ? bestY : 0);
        return (dx, KeepOnScreen(rect, dy));
    }

    /// <summary>The nearest edges, across and down, this rectangle could snap to.</summary>
    private void Nearest(IntPtr hwnd, Rect rect, int reach, ref int bestX, ref int bestY)
    {
        foreach (var target in Targets(hwnd, rect))
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
    }

    /// <summary>The window being dragged, and the windows it carries where they'd be beside it there.</summary>
    private IEnumerable<Rect> Members(Rect rect)
    {
        yield return rect;
        foreach (var offset in _carriedOffsets)
        {
            yield return Offset(offset, rect.Left, rect.Top);
        }
    }

    /// <summary>
    /// How far down to move the dragged window (and what it carries) so none of their tops goes above the
    /// top of the screen it's on, where its title bar (and the buttons on it) couldn't be reached.
    /// </summary>
    private int KeepOnScreen(Rect rect, int dy)
    {
        foreach (var member in Members(rect))
        {
            var moved = Offset(member, 0, dy);
            var monitor = MonitorFromRect(ref moved, MONITOR_DEFAULTTONEAREST);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info) && moved.Top < info.Work.Top)
            {
                dy += info.Work.Top - moved.Top;
            }
        }

        return dy;
    }

    /// <summary>
    /// When the window has been pushed a little way into another of Lucent's windows, moves it back
    /// out against that window's nearest edge. Pushed further in than that, it's meant to go over it.
    /// </summary>
    private (int Dx, int Dy) KeepApart(IntPtr hwnd, Rect rect, int dx, int dy)
    {
        var resist = (int)Math.Round(OverlapDistance * Scale(hwnd));
        var others = OtherWindows(hwnd).Select(other => other.Rect).ToList();

        // Pushing out of one window can land in the one beside it, so look twice.
        for (var pass = 0; pass < 2; pass++)
        {
            var moved = Offset(rect, dx, dy);
            if (others.Where(other => Overlapping(moved, other)).Cast<Rect?>().FirstOrDefault() is not { } into)
            {
                break;
            }

            var best = (X: 0, Y: 0);
            var bestDistance = resist + 1;
            foreach (var (x, y) in new[] { (into.Left - moved.Right, 0), (into.Right - moved.Left, 0), (0, into.Top - moved.Bottom), (0, into.Bottom - moved.Top) })
            {
                var distance = Math.Abs(x) + Math.Abs(y);
                var outside = Offset(moved, x, y);
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
    private static bool Overlapping(Rect a, Rect b)
    {
        const int flush = 2;
        return a.Left < b.Right - flush && b.Left < a.Right - flush && a.Top < b.Bottom - flush && b.Top < a.Bottom - flush;
    }

    private static Rect Offset(Rect rect, int dx, int dy) => new()
    {
        Left = rect.Left + dx,
        Top = rect.Top + dy,
        Right = rect.Right + dx,
        Bottom = rect.Bottom + dy,
    };

    private void SnapSize(IntPtr hwnd, ref Rect rect, int edge)
    {
        var reach = (int)Math.Round(SnapDistance * Scale(hwnd));
        var left = edge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT;
        var right = edge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT;
        var top = edge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT;
        var bottom = edge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT;

        var bestX = reach + 1;
        var bestY = reach + 1;
        var moving = rect;
        var start = Visible(_startRect, _startMargins);
        var startX = left ? start.Left : start.Right;
        var startY = top ? start.Top : start.Bottom;
        foreach (var target in Targets(hwnd, rect))
        {
            var overlapY = moving.Top < target.Bottom + reach && target.Top < moving.Bottom + reach;
            var overlapX = moving.Left < target.Right + reach && target.Left < moving.Right + reach;
            var edgeX = left ? moving.Left : moving.Right;
            var edgeY = top ? moving.Top : moving.Bottom;
            if ((left || right) && (overlapY || target.IsScreen))
            {
                ConsiderAhead(ref bestX, target.Left, edgeX, startX);
                ConsiderAhead(ref bestX, target.Right, edgeX, startX);
            }

            if ((top || bottom) && (overlapX || target.IsScreen))
            {
                ConsiderAhead(ref bestY, target.Top, edgeY, startY);
                ConsiderAhead(ref bestY, target.Bottom, edgeY, startY);
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

        // An edge pulled a little way into a window it started outside of stops at that window's edge. A locked
        // window stops it however far it's pulled, and says it's locked once the mouse pushes on past.
        const int flush = 2;
        var resist = (int)Math.Round(OverlapDistance * Scale(hwnd));
        var nudge = (int)Math.Round(LockedNudge * Scale(hwnd));
        foreach (var (handle, other) in OtherWindows(hwnd))
        {
            var locked = IsLocked(handle);
            var limit = locked ? int.MaxValue : resist;
            var besideY = rect.Top < other.Bottom - flush && other.Top < rect.Bottom - flush;
            var besideX = rect.Left < other.Right - flush && other.Left < rect.Right - flush;
            var pushed = 0;
            if (right && besideY && start.Right <= other.Left + flush && rect.Right > other.Left && rect.Right - other.Left <= limit)
            {
                pushed = rect.Right - other.Left;
                rect.Right = other.Left;
            }
            else if (left && besideY && start.Left >= other.Right - flush && rect.Left < other.Right && other.Right - rect.Left <= limit)
            {
                pushed = other.Right - rect.Left;
                rect.Left = other.Right;
            }

            if (bottom && besideX && start.Bottom <= other.Top + flush && rect.Bottom > other.Top && rect.Bottom - other.Top <= limit)
            {
                pushed = Math.Max(pushed, rect.Bottom - other.Top);
                rect.Bottom = other.Top;
            }
            else if (top && besideX && start.Top >= other.Bottom - flush && rect.Top < other.Bottom && other.Bottom - rect.Top <= limit)
            {
                pushed = Math.Max(pushed, other.Bottom - rect.Top);
                rect.Top = other.Bottom;
            }

            if (locked && pushed > nudge && !_saidLocked)
            {
                _saidLocked = true;
                SayLocked(handle);
            }
        }
    }

    /// <summary>
    /// Snaps a dragged edge to a line only when the line's on the side the mouse has taken the edge from
    /// where it started (or the edge started on it). An edge that started near a line on the other side
    /// would otherwise jump back against the drag the moment it moved, and hang there: in Lucent full
    /// screen, the edge between the visuals and the analyzer often starts just short of the line between
    /// windows in the next column.
    /// </summary>
    private static void ConsiderAhead(ref int best, int line, int edge, int start)
    {
        if (line == start || (edge != start && (line - start > 0) == (edge - start > 0)))
        {
            Consider(ref best, line - edge);
        }
    }

    /// <summary>A resize ran into a locked window: says so, and how to unlock it.</summary>
    private static void SayLocked(IntPtr handle)
    {
        if (HwndSource.FromHwnd(handle)?.RootVisual is Window window)
        {
            Toast.Show(window, $"{WindowLocks.NameOf(window) ?? "This window"} is locked. Click its padlock to unlock it.");
        }
    }

    private static void Consider(ref int best, int candidate)
    {
        if (Math.Abs(candidate) < Math.Abs(best))
        {
            best = candidate;
        }
    }

    private IEnumerable<SnapTarget> Targets(IntPtr hwnd, Rect rect)
    {
        foreach (var (_, other) in OtherWindows(hwnd))
        {
            yield return new SnapTarget(other.Left, other.Top, other.Right, other.Bottom, false);
        }

        var monitor = MonitorFromRect(ref rect, MONITOR_DEFAULTTONEAREST);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
        {
            var work = info.Work;
            yield return new SnapTarget(work.Left, work.Top, work.Right, work.Bottom, true);
        }
    }

    /// <summary>
    /// The part of the window you can see. Windows can pad a window's rectangle with invisible
    /// borders, and snapping to those would leave gaps between windows, so edges are measured here.
    /// Lucid's own windows draw their whole rectangle themselves (no Windows frame), so for those
    /// the rectangle is what you see, whatever Windows reports for the frame it would have drawn.
    /// </summary>
    private static Rect VisibleRect(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var outer);
        if (GetClientRect(hwnd, out var client)
            && client.Right - client.Left == outer.Right - outer.Left
            && client.Bottom - client.Top == outer.Bottom - outer.Top)
        {
            return outer;
        }

        if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var visible, Marshal.SizeOf<Rect>()) == 0
            && visible.Right > visible.Left && visible.Bottom > visible.Top)
        {
            return visible;
        }

        return outer;
    }

    /// <summary>How far each visible edge sits inside the window's rectangle.</summary>
    private static Rect Margins(IntPtr hwnd)
    {
        GetWindowRect(hwnd, out var outer);
        var visible = VisibleRect(hwnd);
        return new Rect
        {
            Left = visible.Left - outer.Left,
            Top = visible.Top - outer.Top,
            Right = outer.Right - visible.Right,
            Bottom = outer.Bottom - visible.Bottom,
        };
    }

    private static Rect Visible(Rect outer, Rect margins) => new()
    {
        Left = outer.Left + margins.Left,
        Top = outer.Top + margins.Top,
        Right = outer.Right - margins.Right,
        Bottom = outer.Bottom - margins.Bottom,
    };

    private static Rect Outer(Rect visible, Rect margins) => new()
    {
        Left = visible.Left - margins.Left,
        Top = visible.Top - margins.Top,
        Right = visible.Right + margins.Right,
        Bottom = visible.Bottom + margins.Bottom,
    };

    private static double Scale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static IntPtr Handle(Window window) => new WindowInteropHelper(window).Handle;

    private readonly record struct SnapTarget(int Left, int Top, int Right, int Bottom, bool IsScreen);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginDeferWindowPos(int count);

    [DllImport("user32.dll")]
    private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndDeferWindowPos(IntPtr batch);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);


    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int key);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    private static extern IntPtr GetTopWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out Rect value, int size);
}
