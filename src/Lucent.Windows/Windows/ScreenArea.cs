using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WinampInspiredVisualizer.Windows;

/// <summary>Finds the usable part (without the taskbar) of the screen a window is on.</summary>
public static class ScreenArea
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>The work area of the window's screen, in the same units as the window's Left and Top.</summary>
    public static Rect Of(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (handle == IntPtr.Zero
            || MonitorFromWindow(handle, MONITOR_DEFAULTTONEAREST) is var monitor && monitor == IntPtr.Zero
            || !GetMonitorInfo(monitor, ref info)
            || PresentationSource.FromVisual(window)?.CompositionTarget is not { } target)
        {
            return SystemParameters.WorkArea;
        }

        var fromDevice = target.TransformFromDevice;
        return new Rect(
            fromDevice.Transform(new Point(info.Work.Left, info.Work.Top)),
            fromDevice.Transform(new Point(info.Work.Right, info.Work.Bottom)));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
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
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
