using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Hosts a native child window inside the WPF layout for Direct3D to draw into,
/// and owns the renderer that draws there.
/// </summary>
public sealed class VisualizerHost : HwndHost
{
    private const string WindowClassName = "WinampInspiredVisualizerSurface";
    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CLIPSIBLINGS = 0x04000000;
    private const int CS_DBLCLKS = 0x0008;
    private const int WM_ERASEBKGND = 0x0014;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int IDC_ARROW = 32512;

    private static bool _classRegistered;

    private readonly AudioAnalyzer _analyzer;
    private Renderer? _renderer;
    private int _presetIndex;

    public VisualizerHost(AudioCapture capture)
    {
        _analyzer = new AudioAnalyzer(capture);
    }

    /// <summary>Raised on the UI thread with the names of the visuals that loaded.</summary>
    public event EventHandler<IReadOnlyList<string>>? PresetsLoaded;

    /// <summary>Raised on the UI thread with a message when something goes wrong.</summary>
    public event EventHandler<string>? ProblemReported;

    public event EventHandler? DoubleClicked;

    public int PresetIndex
    {
        get => _presetIndex;
        set
        {
            _presetIndex = value;
            if (_renderer is not null)
            {
                _renderer.PresetIndex = value;
            }
        }
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        RegisterWindowClass();
        var hwnd = CreateWindowEx(
            0, WindowClassName, string.Empty, WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS,
            0, 0, Math.Max(1, (int)ActualWidth), Math.Max(1, (int)ActualHeight),
            hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException("Couldn't create the visualizer window.");
        }

        var dispatcher = Dispatcher;
        _renderer = new Renderer(hwnd, _analyzer) { PresetIndex = _presetIndex };
        _renderer.PresetsReady += names => dispatcher.InvokeAsync(() => PresetsLoaded?.Invoke(this, names));
        _renderer.PresetFailed += message => dispatcher.InvokeAsync(() => ProblemReported?.Invoke(this, $"A visual didn't load. {message}"));
        _renderer.Failed += message => dispatcher.InvokeAsync(() => ProblemReported?.Invoke(this, $"The GPU visualizer stopped: {message}"));
        _renderer.Start();

        return new HandleRef(this, hwnd);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        _renderer?.Dispose();
        _renderer = null;
        DestroyWindow(hwnd.Handle);
    }

    protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_ERASEBKGND:
                // Direct3D covers the whole window; skipping the erase avoids flicker.
                handled = true;
                return new IntPtr(1);
            case WM_LBUTTONDBLCLK:
                DoubleClicked?.Invoke(this, EventArgs.Empty);
                handled = true;
                return IntPtr.Zero;
            default:
                return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }
    }

    private static void RegisterWindowClass()
    {
        if (_classRegistered)
        {
            return;
        }

        var windowClass = new WndClassEx
        {
            cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
            style = CS_DBLCLKS,
            // The window's own messages are handled by HwndHost.WndProc above,
            // so the class just needs the default handler.
            lpfnWndProc = GetProcAddress(GetModuleHandle("user32.dll"), "DefWindowProcW"),
            hInstance = GetModuleHandle(null),
            hCursor = LoadCursor(IntPtr.Zero, new IntPtr(IDC_ARROW)),
            lpszClassName = WindowClassName,
        };

        if (RegisterClassEx(ref windowClass) == 0)
        {
            throw new InvalidOperationException("Couldn't register the visualizer window class.");
        }

        _classRegistered = true;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr module, string procName);
}
