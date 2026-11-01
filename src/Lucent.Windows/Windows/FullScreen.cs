using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lucent.Windows;

/// <summary>
/// Full screen for a window with Lucent's own title bar (the audio editor and the analyzer): the
/// window covers its whole screen, taskbar too, and everything in it grows to fill the space. The
/// title bar stays, so the same button takes it back. F11 or the button toggles it; Esc leaves it.
/// </summary>
public sealed class FullScreen
{
    private readonly Window _window;
    private readonly Button _button;
    private readonly Path _icon;
    private WindowState _stateBefore;
    private Rect _boundsBefore;
    private bool _switching;

    /// <param name="button">The title bar button that toggles it, whose icon and tooltip follow it.</param>
    public FullScreen(Window window, Button button, Path icon)
    {
        _window = window;
        _button = button;
        _icon = icon;
        _button.Click += (_, _) => Toggle();
        _window.StateChanged += OnStateChanged;
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
            _window.WindowState = WindowState.Maximized;
            _window.Activate();
        }
        else
        {
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
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Double-clicking or dragging the title bar restores the window, which ends full screen.
        if (IsOn && !_switching && _window.WindowState != WindowState.Maximized)
        {
            IsOn = false;
            CaptionRepaint.Change(_window, () =>
            {
                _window.WindowStyle = WindowStyle.SingleBorderWindow;
                _window.ResizeMode = ResizeMode.CanResize;
            });
            UpdateButton();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateButton()
    {
        _icon.Data = (Geometry)_window.FindResource(IsOn ? "ExitFullScreenCaptionIcon" : "FullScreenCaptionIcon");
        _button.ToolTip = IsOn ? "Exit full screen (F11 or Esc)" : "Full screen (F11)";
    }
}
