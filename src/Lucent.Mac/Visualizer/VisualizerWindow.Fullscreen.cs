using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>Fullscreen, and the × in the top-right corner that leaves it. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    // The × shows when the mouse moves and fades once the mouse has been still this long. It shows
    // for longer on entering fullscreen, so people learn it's there.
    private static readonly TimeSpan ExitButtonLinger = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan ExitButtonFirstShow = TimeSpan.FromSeconds(4);
    private const double ExitButtonSize = 44;
    private const double ExitButtonMargin = 16;

    // The visuals are a native Metal view that Avalonia can't draw over, so the × sits in a popup
    // (its own small window) above them.
    private Popup? _exitPopup;
    private Border? _exitPanel;
    private DispatcherTimer? _exitTimer;
    private DispatcherTimer? _exitFadeTimer;
    private bool _exitShowing;
    private Point? _lastPointer;
    private DateTime _exitButtonUntil;
    private bool _takenOutForFullscreen;

    private void OnToggleFullscreen(object? sender, RoutedEventArgs e) => ToggleFullscreen();

    /// <summary>
    /// Fills the screen with the visuals (Esc, double-click or the × in the corner to leave). Like the
    /// Windows app, the window itself grows to cover the whole screen, Dock and menu bar (and the strip beside a
    /// MacBook's notch) too, rather
    /// than moving to a fullscreen Space of its own: it belongs to the player window, and stays with it.
    /// </summary>
    public void ToggleFullscreen()
    {
        if (!_isFullscreen && !IsVisible)
        {
            ShowWindow();
        }

        // A panel of the single window is a window of its own while it's full screen.
        if (!_isFullscreen)
        {
            _takenOutForFullscreen = SingleWindow.TakeOut(this);
        }

        _isFullscreen = !_isFullscreen;
        TitleBar.IsVisible = !_isFullscreen;
        BottomBar.IsVisible = !_isFullscreen;
        UpdateElsewhereBar();
        Frame.BorderThickness = new Thickness(_isFullscreen ? 0 : 1);
        PresetPopup.IsOpen = false;

        if (_isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            if (WindowState == WindowState.Normal)
            {
                _positionBeforeFullscreen = Position;
                _sizeBeforeFullscreen = new Size(Width, Height);
            }

            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            WindowState = WindowState.Normal;
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            if (screen is not null)
            {
                Position = screen.Bounds.Position;
                Width = screen.Bounds.Width / screen.Scaling;
                Height = screen.Bounds.Height / screen.Scaling;
            }

            AppKit.AutoHideDockAndMenuBar(true);
            FullScreen.CoverScreen(this, () => _isFullscreen);
            Activate();
        }
        else
        {
            AppKit.AutoHideDockAndMenuBar(false);
            AppKit.CoverScreen(this, false);
            Chrome.UseLucentFrame(this);
            CanResize = true;
            WindowState = _stateBeforeFullscreen;
            if (_stateBeforeFullscreen == WindowState.Normal && _sizeBeforeFullscreen.Width > 0)
            {
                Position = _positionBeforeFullscreen;
                Width = _sizeBeforeFullscreen.Width;
                Height = _sizeBeforeFullscreen.Height;
            }
        }

        OnWindowStateChanged();
        if (_isFullscreen)
        {
            StartExitButton();
        }
        else
        {
            StopExitButton();
            if (_takenOutForFullscreen)
            {
                _takenOutForFullscreen = false;
                SingleWindow.PutBack(this);
            }
        }
    }

    private void StartExitButton()
    {
        _exitPopup ??= MakeExitPopup();
        if (_exitTimer is null)
        {
            _exitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _exitTimer.Tick += (_, _) => UpdateExitButton();
            AddHandler(PointerMovedEvent, OnPointerMovedInFullscreen, RoutingStrategies.Tunnel, handledEventsToo: true);
        }

        _lastPointer = null;
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

    /// <summary>The mouse moving over the visuals brings the × back for a while.</summary>
    private void OnPointerMovedInFullscreen(object? sender, PointerEventArgs e)
    {
        if (!_isFullscreen)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (_lastPointer is { } last && last == point)
        {
            return;
        }

        _lastPointer = point;
        _exitButtonUntil = DateTime.UtcNow + ExitButtonLinger;
    }

    private void UpdateExitButton()
    {
        if (!_isFullscreen)
        {
            StopExitButton();
            return;
        }

        // A popup stays above the other windows, so it goes as soon as Lucent isn't the active window.
        if (!IsVisible || !IsActive)
        {
            HideExitButton(fade: false);
        }
        else if (DateTime.UtcNow < _exitButtonUntil || _exitPanel!.IsPointerOver)
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
        if (_exitShowing)
        {
            return;
        }

        _exitShowing = true;
        _exitFadeTimer?.Stop();
        if (!popup.IsOpen)
        {
            SetExitOpacity(0, TimeSpan.Zero);
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
            _exitFadeTimer?.Stop();
            SetExitOpacity(0, TimeSpan.Zero);
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
        var seconds = ReduceMotion() ? 0 : (to > 0 ? 0.15 : 0.4);
        SetExitOpacity(to, TimeSpan.FromSeconds(seconds));
        _exitFadeTimer?.Stop();
        if (done is not null)
        {
            _exitFadeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(seconds, 0.01)) };
            _exitFadeTimer.Tick += (_, _) =>
            {
                _exitFadeTimer?.Stop();
                done();
            };
            _exitFadeTimer.Start();
        }
    }

    /// <summary>Fades the × to an opacity over the given time (at once for zero).</summary>
    private void SetExitOpacity(double opacity, TimeSpan duration)
    {
        var panel = _exitPanel!;
        panel.Transitions = duration > TimeSpan.Zero
            ? new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = duration, Easing = new CubicEaseOut() } }
            : null;
        panel.Opacity = opacity;
    }

    /// <summary>Whether "Reduce motion" is on in the Mac's accessibility settings, when fades are skipped.</summary>
    private static bool ReduceMotion()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), "sharedWorkspace");
            return ObjC.SendBool(workspace, "accessibilityDisplayShouldReduceMotion");
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }

    private Popup MakeExitPopup()
    {
        var button = new Button
        {
            Theme = (ControlTheme?)this.FindResource("CloseCaptionButton"),
            Width = ExitButtonSize,
            Height = ExitButtonSize,
            Focusable = false,
            Foreground = Brushes.White,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Theme = (ControlTheme?)this.FindResource("CaptionIcon"),
                Data = (Geometry?)this.FindResource("CloseIcon"),
                StrokeThickness = 1.5,
            },
        };
        ToolTip.SetTip(button, "Leave fullscreen (Esc)");
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
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Child = button,
        };
        var popup = new Popup
        {
            PlacementTarget = VisualizerFrame,
            // Its top-right corner a little in from the visuals' top-right corner.
            Placement = PlacementMode.AnchorAndGravity,
            PlacementAnchor = PopupAnchor.TopRight,
            PlacementGravity = PopupGravity.BottomLeft,
            HorizontalOffset = -ExitButtonMargin,
            VerticalOffset = ExitButtonMargin,
            IsLightDismissEnabled = false,
            Focusable = false,
            Child = _exitPanel,
        };
        WindowRoot.Children.Add(popup);
        return popup;
    }
}
