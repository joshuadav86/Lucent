using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// Makes Lucid's windows behave like Winamp's: their edges snap to each other and to the
/// edges of the screen, and dragging the player window carries every window docked to it.
/// Works on the raw window rectangles (physical pixels) while Windows moves them.
/// </summary>
public sealed class WindowSnapper
{
    private const int WM_SIZING = 0x0214;
    private const int WM_MOVING = 0x0216;
    private const int WM_ENTERSIZEMOVE = 0x0231;
    private const int WM_EXITSIZEMOVE = 0x0232;

    private const int WMSZ_LEFT = 1;
    private const int WMSZ_RIGHT = 2;
    private const int WMSZ_TOP = 3;
    private const int WMSZ_TOPLEFT = 4;
    private const int WMSZ_TOPRIGHT = 5;
    private const int WMSZ_BOTTOM = 6;
    private const int WMSZ_BOTTOMLEFT = 7;
    private const int WMSZ_BOTTOMRIGHT = 8;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>How close (in pixels at 100% scaling) an edge has to come before it snaps.</summary>
    private const double SnapDistance = 12;

    private readonly Window _main;
    private readonly List<Window> _windows = new();
    private readonly List<(IntPtr Handle, int OffsetX, int OffsetY)> _carried = new();
    private IntPtr _moving;

    public WindowSnapper(Window main)
    {
        _main = main;
        Add(main);
    }

    /// <summary>Raised when the user finishes moving or resizing one of the windows.</summary>
    public event EventHandler? MoveFinished;

    public void Add(Window window)
    {
        _windows.Add(window);
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
            case WM_ENTERSIZEMOVE:
                _moving = hwnd;
                _carried.Clear();
                if (hwnd == Handle(_main))
                {
                    FindDocked(hwnd);
                }

                break;
            case WM_MOVING when hwnd == _moving:
            {
                var rect = Marshal.PtrToStructure<Rect>(lParam);
                var (dx, dy) = SnapMove(hwnd, rect);
                rect.Left += dx;
                rect.Right += dx;
                rect.Top += dy;
                rect.Bottom += dy;
                Marshal.StructureToPtr(rect, lParam, false);
                foreach (var (handle, offsetX, offsetY) in _carried)
                {
                    SetWindowPos(handle, IntPtr.Zero, rect.Left + offsetX, rect.Top + offsetY, 0, 0,
                        SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }

                break;
            }
            case WM_SIZING when hwnd == _moving:
            {
                var rect = Marshal.PtrToStructure<Rect>(lParam);
                SnapSize(hwnd, ref rect, wParam.ToInt32());
                Marshal.StructureToPtr(rect, lParam, false);
                break;
            }
            case WM_EXITSIZEMOVE:
                _moving = IntPtr.Zero;
                _carried.Clear();
                MoveFinished?.Invoke(this, EventArgs.Empty);
                break;
        }

        return IntPtr.Zero;
    }

    /// <summary>Every window touching the player, or touching a window that touches it, moves with it.</summary>
    private void FindDocked(IntPtr main)
    {
        GetWindowRect(main, out var mainRect);
        var group = new List<(IntPtr Handle, Rect Rect)> { (main, mainRect) };
        var others = OtherWindows(main).ToList();
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

        foreach (var (handle, rect) in group.Skip(1))
        {
            _carried.Add((handle, rect.Left - mainRect.Left, rect.Top - mainRect.Top));
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

    /// <summary>The visible Lucid windows other than this one (and the ones it's carrying), plus the screen it's on.</summary>
    private IEnumerable<(IntPtr Handle, Rect Rect)> OtherWindows(IntPtr self)
    {
        foreach (var window in _windows)
        {
            var handle = Handle(window);
            if (handle == IntPtr.Zero || handle == self || !window.IsVisible || window.WindowState != WindowState.Normal
                || _carried.Any(carried => carried.Handle == handle))
            {
                continue;
            }

            if (GetWindowRect(handle, out var rect))
            {
                yield return (handle, rect);
            }
        }
    }

    private (int Dx, int Dy) SnapMove(IntPtr hwnd, Rect rect)
    {
        var reach = (int)Math.Round(SnapDistance * Scale(hwnd));
        var bestX = reach + 1;
        var bestY = reach + 1;
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

        return (Math.Abs(bestX) <= reach ? bestX : 0, Math.Abs(bestY) <= reach ? bestY : 0);
    }

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
        foreach (var target in Targets(hwnd, rect))
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
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromRect(ref Rect rect, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
