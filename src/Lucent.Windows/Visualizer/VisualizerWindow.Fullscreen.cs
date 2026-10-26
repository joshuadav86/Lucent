using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace WinampInspiredVisualizer;

/// <summary>The × in the top-right corner that leaves fullscreen. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    // The × shows when the mouse moves and fades once the mouse has been still this long. It shows
    // for longer on entering fullscreen, so people learn it's there.
    private static readonly TimeSpan ExitButtonLinger = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ExitButtonFirstShow = TimeSpan.FromSeconds(4);
    private const double ExitButtonSize = 44;
    private const double ExitButtonMargin = 16;

    // The visuals are a Direct3D window that WPF can't draw over, so the × sits in a popup (its own
    // small window) above them.
    private Popup? _exitPopup;
    private Border? _exitPanel;
    private DispatcherTimer? _exitTimer;
    private bool _exitShowing;
    private CursorPoint _lastCursor;
    private DateTime _exitButtonUntil;

    private void StartExitButton()
    {
        _exitPopup ??= MakeExitPopup();
        if (_exitTimer is null)
        {
            _exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _exitTimer.Tick += (_, _) => UpdateExitButton();
        }

        GetCursorPos(out _lastCursor);
        _exitButtonUntil = DateTime.UtcNow + ExitButtonFirstShow;
        // The first tick comes after the window has grown to fill the screen, so the × lands in the
        // right corner straight away.
        _exitTimer.Start();
    }

    private void StopExitButton()
    {
        _exitTimer?.Stop();
        HideExitButton(fade: false);
    }

    private void UpdateExitButton()
    {
        if (!_isFullscreen)
        {
            StopExitButton();
            return;
        }

        GetCursorPos(out var cursor);
        if (cursor.X != _lastCursor.X || cursor.Y != _lastCursor.Y)
        {
            _lastCursor = cursor;
            if (IsCursorOverWindow(cursor))
            {
                _exitButtonUntil = DateTime.UtcNow + ExitButtonLinger;
            }
        }

        // A popup stays above every other app, so it goes as soon as Lucent isn't the active window.
        if (!IsVisible || !IsActive)
        {
            HideExitButton(fade: false);
        }
        else if (DateTime.UtcNow < _exitButtonUntil || _exitPanel!.IsMouseOver)
        {
            ShowExitButton();
        }
        else
        {
            HideExitButton(fade: true);
        }
    }

    private void ShowExitButton()
    {
        var popup = _exitPopup!;
        popup.HorizontalOffset = Math.Max(0, VisualizerFrame.ActualWidth - ExitButtonSize - ExitButtonMargin);
        popup.VerticalOffset = ExitButtonMargin;
        if (_exitShowing)
        {
            return;
        }

        _exitShowing = true;
        if (!popup.IsOpen)
        {
            _exitPanel!.Opacity = 0;
            popup.IsOpen = true;
        }

        FadeExitButton(1, null);
    }

    private void HideExitButton(bool fade)
    {
        if (_exitPopup is null || !_exitPopup.IsOpen)
        {
            _exitShowing = false;
            return;
        }

        if (!fade)
        {
            _exitShowing = false;
            _exitPanel!.BeginAnimation(OpacityProperty, null);
            _exitPopup.IsOpen = false;
            return;
        }

        if (!_exitShowing)
        {
            return;
        }

        _exitShowing = false;
        FadeExitButton(0, () =>
        {
            // Close it once faded, unless the mouse moved and brought it back meanwhile.
            if (!_exitShowing)
            {
                _exitPopup.IsOpen = false;
            }
        });
    }

    private void FadeExitButton(double to, Action? done)
    {
        var seconds = SystemParameters.ClientAreaAnimation ? (to > 0 ? 0.15 : 0.4) : 0;
        var fade = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (done is not null)
        {
            fade.Completed += (_, _) => done();
        }

        _exitPanel!.BeginAnimation(OpacityProperty, fade);
    }

    private Popup MakeExitPopup()
    {
        var button = new Button
        {
            Style = (Style)FindResource("CloseCaptionButton"),
            Width = ExitButtonSize,
            Height = ExitButtonSize,
            Focusable = false,
            Foreground = Brushes.White,
            ToolTip = "Leave fullscreen (Esc)",
            Content = new Path
            {
                Style = (Style)FindResource("CaptionIcon"),
                Data = (Geometry)FindResource("CloseIcon"),
                StrokeThickness = 1.5,
            },
        };
        button.Click += (_, _) =>
        {
            if (_isFullscreen)
            {
                ToggleFullscreen();
            }
        };

        _exitPanel = new Border
        {
            Width = ExitButtonSize,
            Height = ExitButtonSize,
            Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            Clip = new RectangleGeometry(new Rect(0, 0, ExitButtonSize, ExitButtonSize), 8, 8),
            Child = button,
        };
        return new Popup
        {
            PlacementTarget = VisualizerFrame,
            Placement = PlacementMode.Relative,
            AllowsTransparency = true,
            StaysOpen = true,
            Focusable = false,
            Child = _exitPanel,
        };
    }

    private bool IsCursorOverWindow(CursorPoint cursor)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var bounds)
            && cursor.X >= bounds.Left && cursor.X < bounds.Right && cursor.Y >= bounds.Top && cursor.Y < bounds.Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowBounds
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out WindowBounds bounds);
}
