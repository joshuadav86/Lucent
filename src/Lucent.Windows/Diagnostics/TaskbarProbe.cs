using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace Lucent;

// TEMPORARY diagnostic (removed before merge): does the visuals' full screen cover the taskbar?
public partial class MainWindow
{
    private void RunTaskbarProbeIfAsked()
    {
        if (Environment.GetEnvironmentVariable("LUCENT_TASKBAR_PROBE") is { Length: > 0 } file)
        {
            _ = RunTaskbarProbeAsync(file);
        }
    }

    private async Task RunTaskbarProbeAsync(string file)
    {
        var lines = new List<string>();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            if (InSingleWindow)
            {
                LeaveSingleWindow();
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            if (!_visuals.IsVisible)
            {
                ToggleWindow(_visuals);
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            var tray = Probe.FindWindow("Shell_TrayWnd", null);
            Probe.GetWindowRect(tray, out var trayRect);
            lines.Add($"taskbar {tray} visible={Probe.IsWindowVisible(tray)} rect {Describe(trayRect)}");
            var hwnd = new WindowInteropHelper(_visuals).Handle;
            var owner = Probe.GetWindow(hwnd, 4);
            lines.Add($"visuals {hwnd} owner {owner} ({Probe.ClassOf(owner)}) exstyle 0x{Probe.GetWindowLongPtr(hwnd, -20):x}");

            foreach (var variant in new[] { "as is", "no chrome", "topmost after", "as is again" })
            {
                if (variant.StartsWith("unowned"))
                {
                    Probe.SetWindowLongPtr(hwnd, -8, IntPtr.Zero);
                }

                if (variant.EndsWith("mark"))
                {
                    Probe.Mark(hwnd, true);
                }

                System.Windows.Shell.WindowChrome? chrome = null;
                if (variant == "no chrome")
                {
                    chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(_visuals);
                    System.Windows.Shell.WindowChrome.SetWindowChrome(_visuals, null);
                }

                _visuals.ToggleFullscreen();
                if (variant.EndsWith("topmost") || variant == "topmost after")
                {
                    _visuals.Topmost = true;
                    _visuals.Activate();
                }

                await Task.Delay(TimeSpan.FromSeconds(0.3));
                Probe.GetWindowRect(tray, out var early);
                var earlyHit = Probe.GetAncestor(Probe.WindowFromPoint(new Probe.POINT { X = (early.Left + early.Right) / 2, Y = (early.Top + early.Bottom) / 2 }), 2);
                lines.Add($"{variant} after 0.3 s: {(earlyHit == hwnd ? "covered" : Probe.ClassOf(earlyHit))}, owner now {Probe.GetWindow(hwnd, 4)}");
                await Task.Delay(TimeSpan.FromSeconds(2));
                Probe.GetWindowRect(hwnd, out var rect);
                var monitor = Probe.MonitorFromWindow(hwnd, 2);
                var info = new Probe.MONITORINFO { cbSize = Marshal.SizeOf<Probe.MONITORINFO>() };
                Probe.GetMonitorInfo(monitor, ref info);
                Probe.GetWindowRect(tray, out trayRect);
                var x = (Math.Max(trayRect.Left, info.rcMonitor.Left) + Math.Min(trayRect.Right, info.rcMonitor.Right)) / 2;
                var y = (Math.Max(trayRect.Top, info.rcMonitor.Top) + Math.Min(trayRect.Bottom, info.rcMonitor.Bottom)) / 2;
                var hit = Probe.GetAncestor(Probe.WindowFromPoint(new Probe.POINT { X = x, Y = y }), 2);
                var fg = Probe.GetForegroundWindow();
                lines.Add($"{variant}: style 0x{Probe.GetWindowLongPtr(hwnd, -16):x} ex 0x{Probe.GetWindowLongPtr(hwnd, -20):x}; " + Probe.Hit(tray, hwnd));
                lines.Add($"{variant}: window {Describe(rect)}, monitor {Describe(info.rcMonitor)} work {Describe(info.rcWork)}, " +
                    $"foreground {(fg == hwnd ? "visuals" : $"{fg} {Probe.ClassOf(fg)}")}, at {x},{y} on the taskbar: " +
                    $"{(hit == hwnd ? "THE VISUALS (covered)" : $"{hit} {Probe.ClassOf(hit)} (TASKBAR SHOWS)")}");
                _visuals.ToggleFullscreen();
                await Task.Delay(TimeSpan.FromSeconds(1));
                if (variant.EndsWith("mark"))
                {
                    Probe.Mark(hwnd, false);
                }

                _visuals.Topmost = false;
                if (chrome is not null)
                {
                    System.Windows.Shell.WindowChrome.SetWindowChrome(_visuals, chrome);
                }

                if (variant.StartsWith("unowned"))
                {
                    Probe.SetWindowLongPtr(hwnd, -8, owner);
                }

                await Task.Delay(TimeSpan.FromSeconds(0.5));
            }

            // Controls: a plain WPF window and a raw Win32 popup filling the screen.
            lines.Add($"taskbar topmost now: {(Probe.GetWindowLongPtr(tray, -20).ToInt64() & 8) != 0}");
            var plain = new Window { WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Background = System.Windows.Media.Brushes.DarkBlue, Title = "plain" };
            plain.Show();
            plain.WindowState = WindowState.Maximized;
            plain.Activate();
            await Task.Delay(TimeSpan.FromSeconds(2));
            lines.Add($"plain style 0x{Probe.GetWindowLongPtr(new WindowInteropHelper(plain).Handle, -16):x}");
            lines.Add("plain WPF: " + Probe.Hit(tray, new WindowInteropHelper(plain).Handle));
            plain.Owner = this;
            plain.ShowInTaskbar = false;
            plain.WindowState = WindowState.Normal;
            plain.WindowState = WindowState.Maximized;
            plain.Activate();
            await Task.Delay(TimeSpan.FromSeconds(2));
            lines.Add("plain WPF owned: " + Probe.Hit(tray, new WindowInteropHelper(plain).Handle));
            plain.Close();
            await Task.Delay(TimeSpan.FromSeconds(1));
            var info2 = new Probe.MONITORINFO { cbSize = Marshal.SizeOf<Probe.MONITORINFO>() };
            Probe.GetMonitorInfo(Probe.MonitorFromWindow(tray, 2), ref info2);
            var raw = Probe.CreateWindowEx(0, "Static", "raw", unchecked((int)0x90000000), info2.rcMonitor.Left, info2.rcMonitor.Top,
                info2.rcMonitor.Right - info2.rcMonitor.Left, info2.rcMonitor.Bottom - info2.rcMonitor.Top, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            Probe.SetForegroundWindow(raw);
            await Task.Delay(TimeSpan.FromSeconds(2));
            lines.Add($"raw popup {raw} (foreground {Probe.GetForegroundWindow() == raw}): " + Probe.Hit(tray, raw));
            Probe.DestroyWindow(raw);
        }
        catch (Exception ex)
        {
            lines.Add($"probe failed: {ex}");
        }

        await System.IO.File.WriteAllLinesAsync(file, lines);
        Close();
    }

    private static string Describe(Probe.RECT r) => $"{r.Left},{r.Top} to {r.Right},{r.Bottom}";

    private static class Probe
    {
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, int f);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, int f);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr CreateWindowEx(int ex, string cls, string title, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr h);

        public static string Hit(IntPtr tray, IntPtr mine)
        {
            GetWindowRect(tray, out var r);
            var hit = GetAncestor(WindowFromPoint(new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 }), 2);
            GetWindowRect(mine, out var m);
            return $"window {m.Left},{m.Top} to {m.Right},{m.Bottom}, taskbar topmost {(GetWindowLongPtr(tray, -20).ToInt64() & 8) != 0}, " +
                $"on the taskbar: {(hit == mine ? "COVERED" : ClassOf(hit) + " SHOWS")}";
        }

        public static string ClassOf(IntPtr h)
        {
            var s = new StringBuilder(256);
            GetClassName(h, s, s.Capacity);
            return s.ToString();
        }

        [ComImport, Guid("602D4995-B13A-429b-A66E-1935E44F4317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList2
        {
            void HrInit();
            void AddTab(IntPtr h);
            void DeleteTab(IntPtr h);
            void ActivateTab(IntPtr h);
            void SetActiveAlt(IntPtr h);
            void MarkFullscreenWindow(IntPtr h, [MarshalAs(UnmanagedType.Bool)] bool full);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        private class TaskbarList { }

        public static void Mark(IntPtr h, bool on)
        {
            var list = (ITaskbarList2)new TaskbarList();
            list.HrInit();
            list.MarkFullscreenWindow(h, on);
        }
    }
}
