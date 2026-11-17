using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Lucent.Windows;

/// <summary>
/// Full screen for a window with Lucent's own title bar (the audio editor and the analyzer): the
/// window covers its whole screen, taskbar too, and everything in it grows to fill the space. The
/// title bar stays, so the same button takes it back. F11 or the button toggles it; Esc leaves it.
/// The video window hides its title bar while full screen, so the title bar handling here is for the other two.
/// </summary>
public sealed class FullScreen
{
    private readonly Window _window;
    private readonly Button _button;
    private readonly Path _icon;
    private WindowState _stateBefore;
    private Rect _boundsBefore;
    private bool _switching;
    private bool _takenOut;
    private readonly bool _titleBarStays;
    private Point? _titlePressedAt;

    /// <param name="button">The title bar button that toggles it, whose icon and tooltip follow it.</param>
    /// <param name="titleBarStays">False for a window that hides its title bar while full screen (the video's).</param>
    public FullScreen(Window window, Button button, Path icon, bool titleBarStays = true)
    {
        _window = window;
        _button = button;
        _icon = icon;
        _titleBarStays = titleBarStays;
        _button.Click += (_, _) => Toggle();
        _window.StateChanged += OnStateChanged;
        _window.MouseLeftButtonDown += OnMouseLeftButtonDown;
        _window.MouseMove += OnMouseMove;
        _window.MouseLeftButtonUp += (_, _) => _titlePressedAt = null;
        UpdateButton();
    }

    public bool IsOn { get; private set; }

    /// <summary>Raised after the window goes full screen or comes back.</summary>
    public event EventHandler? Changed;

    /// <summary>F11, the key that toggles it.</summary>
    public static bool IsToggleKey(KeyEventArgs e) => e.Key == Key.F11 && Keyboard.Modifiers == ModifierKeys.None;

    public void Toggle()
    {
        _switching = true;
        IsOn = !IsOn;
        if (IsOn)
        {
            // A panel of the single window is a window of its own while it's full screen.
            _takenOut = SingleWindow.TakeOut(_window);
            _stateBefore = _window.WindowState == WindowState.Minimized ? WindowState.Normal : _window.WindowState;
            _boundsBefore = _window.RestoreBounds;
            // Leave Maximized first so Windows recalculates the size without the frame, and the
            // window covers the taskbar too.
            _window.WindowState = WindowState.Normal;
            CaptionRepaint.Change(_window, () =>
            {
                _window.WindowStyle = WindowStyle.None;
                _window.ResizeMode = ResizeMode.NoResize;
            });
            FullScreenChrome.TakeOff(_window);
            _window.WindowState = WindowState.Maximized;
            _window.Activate();
        }
        else
        {
            FullScreenChrome.PutBack(_window);
            CaptionRepaint.Change(_window, () =>
            {
                _window.WindowStyle = WindowStyle.SingleBorderWindow;
                _window.ResizeMode = ResizeMode.CanResize;
            });
            _window.WindowState = _stateBefore;
            if (_stateBefore == WindowState.Normal && !_boundsBefore.IsEmpty)
            {
                _window.Left = _boundsBefore.Left;
                _window.Top = _boundsBefore.Top;
                _window.Width = _boundsBefore.Width;
                _window.Height = _boundsBefore.Height;
            }
        }

        _switching = false;
        UpdateButton();
        PutBackIfTakenOut();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Back in the single window, once out of full screen, if it was a panel there before.</summary>
    private void PutBackIfTakenOut()
    {
        if (!IsOn && _takenOut)
        {
            _takenOut = false;
            SingleWindow.PutBack(_window);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Something else restoring the window (Windows key + Down) ends full screen.
        if (IsOn && !_switching && _window.WindowState != WindowState.Maximized)
        {
            IsOn = false;
            FullScreenChrome.PutBack(_window);
            CaptionRepaint.Change(_window, () =>
            {
                _window.WindowStyle = WindowStyle.SingleBorderWindow;
                _window.ResizeMode = ResizeMode.CanResize;
            });
            UpdateButton();
            PutBackIfTakenOut();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// The title bar while full screen: without the window's chrome it's no longer Windows' caption, so double-clicking
    /// it or dragging it is done here, as Windows does for a maximized window: double-clicking ends full screen, and
    /// dragging ends it and carries on dragging the window at its own size, under the mouse where it was picked up.
    /// </summary>
    private bool InTitleBar(MouseEventArgs e) =>
        _titleBarStays && IsOn && e.GetPosition(_window).Y < FullScreenChrome.CaptionHeight(_window);

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _titlePressedAt = null;
        if (e.Handled || !InTitleBar(e))
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            Toggle();
            e.Handled = true;
            return;
        }

        _titlePressedAt = e.GetPosition(_window);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_titlePressedAt is not { } pressed || e.LeftButton != MouseButtonState.Pressed || !IsOn)
        {
            _titlePressedAt = null;
            return;
        }

        var now = e.GetPosition(_window);
        if (Math.Abs(now.X - pressed.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - pressed.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _titlePressedAt = null;
        var across = _window.ActualWidth > 0 ? pressed.X / _window.ActualWidth : 0.5;
        var mouse = _window.PointToScreen(now);
        if (PresentationSource.FromVisual(_window)?.CompositionTarget is { } target)
        {
            mouse = target.TransformFromDevice.Transform(mouse);
        }

        Toggle();
        if (_window.WindowState == WindowState.Normal)
        {
            _window.Left = mouse.X - _window.Width * across;
            _window.Top = mouse.Y - pressed.Y;
            _window.DragMove();
        }
    }

    private void UpdateButton()
    {
        _icon.Data = (Geometry)_window.FindResource(IsOn ? "ExitFullScreenCaptionIcon" : "FullScreenCaptionIcon");
        _button.ToolTip = IsOn ? "Exit full screen (F11 or Esc)" : "Full screen (F11)";
    }
}

/// <summary>
/// WPF's window chrome (Lucent's own title bars) clips a maximized window to its screen's work area, the screen less
/// the taskbar, so a window filling the screen with it on left the taskbar showing across the bottom (2026-11-17,
/// reported from the portable Windows build). A full screen window goes without its chrome until it comes back. A skin
/// changing the chrome meanwhile (<see cref="Get"/>, <see cref="Set"/>) changes the one it gets back.
/// </summary>
public static class FullScreenChrome
{
    private sealed class Kept
    {
        public WindowChrome? Chrome;
    }

    private static readonly ConditionalWeakTable<Window, Kept> TakenOff = new();

    /// <summary>Takes the window's chrome off while it's full screen. Safe to call more than once.</summary>
    public static void TakeOff(Window window)
    {
        if (TakenOff.TryGetValue(window, out _))
        {
            return;
        }

        TakenOff.Add(window, new Kept { Chrome = WindowChrome.GetWindowChrome(window) });
        WindowChrome.SetWindowChrome(window, null);
    }

    /// <summary>Puts back the chrome taken off by <see cref="TakeOff"/>, if it was.</summary>
    public static void PutBack(Window window)
    {
        if (TakenOff.TryGetValue(window, out var kept))
        {
            TakenOff.Remove(window);
            WindowChrome.SetWindowChrome(window, kept.Chrome);
        }
    }

    /// <summary>True while the window's chrome is off for full screen.</summary>
    public static bool IsOff(Window window) => TakenOff.TryGetValue(window, out _);

    /// <summary>The window's chrome: the one it gets back after full screen, if it's full screen now.</summary>
    public static WindowChrome? Get(Window window) =>
        TakenOff.TryGetValue(window, out var kept) ? kept.Chrome : WindowChrome.GetWindowChrome(window);

    /// <summary>Sets the window's chrome, or the one it gets back after full screen if it's full screen now.</summary>
    public static void Set(Window window, WindowChrome? chrome)
    {
        if (TakenOff.TryGetValue(window, out var kept))
        {
            kept.Chrome = chrome;
        }
        else
        {
            WindowChrome.SetWindowChrome(window, chrome);
        }
    }

    /// <summary>How tall the window's title bar is, from its chrome, whether that's on or off for full screen.</summary>
    public static double CaptionHeight(Window window) => Get(window)?.CaptionHeight ?? 0;
}
