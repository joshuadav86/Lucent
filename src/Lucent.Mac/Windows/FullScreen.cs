using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Lucent.Mac.Interop;
using Shape = Avalonia.Controls.Shapes.Path;

namespace Lucent.Mac.Windows;

/// <summary>
/// Full screen for a window with Lucent's own title bar (the audio editor and the analyzer): the
/// window covers its whole screen, and everything in it grows to fill the space. The Dock and the
/// menu bar hide, sliding back in if the mouse goes to them, and the window covers the menu bar's strip beside
/// a MacBook's notch too. The title bar stays, so the same button
/// takes it back. ⌃⌘F (or F11, as on Windows) or the button toggles it; Esc leaves it.
/// </summary>
/// <remarks>
/// Like the visuals' fullscreen, the window grows in place rather than moving to a fullscreen Space
/// of its own: it belongs to the player window, and stays with it.
/// </remarks>
public sealed class FullScreen
{
    private readonly Window _window;
    private readonly Button _button;
    private readonly Shape _icon;
    private WindowState _stateBefore;
    private PixelPoint _positionBefore;
    private Size _sizeBefore;
    private bool _takenOut;

    /// <param name="button">The title bar button that toggles it, whose icon and tooltip follow it.</param>
    public FullScreen(Window window, Button button, Shape icon)
    {
        _window = window;
        _button = button;
        _icon = icon;
        _button.Click += (_, _) => Toggle();
        UpdateButton();
        CoverScreenWhile(window, () => IsOn);
    }

    public bool IsOn { get; private set; }

    /// <summary>
    /// Whether a window is filling the screen, this way or as the visuals do: only then does a Lucent
    /// window drop its frame. It can't be dragged or snapped to while it does.
    /// </summary>
    public static bool Fills(Window window) => window.SystemDecorations == SystemDecorations.None;

    /// <summary>Raised after the window goes full screen or comes back.</summary>
    public event EventHandler? Changed;

    /// <summary>⌃⌘F, the Mac's full screen keys, or F11 as on Windows.</summary>
    public static bool IsToggleKey(KeyEventArgs e) =>
        e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Meta | KeyModifiers.Control)
        || e.Key == Key.F11 && e.KeyModifiers == KeyModifiers.None;

    public void Toggle()
    {
        IsOn = !IsOn;
        if (IsOn)
        {
            // A panel of the single window is a window of its own while it's full screen.
            _takenOut = SingleWindow.TakeOut(_window);
            _stateBefore = _window.WindowState == WindowState.Minimized ? WindowState.Normal : _window.WindowState;
            if (_window.WindowState == WindowState.Normal)
            {
                _positionBefore = _window.Position;
                _sizeBefore = new Size(_window.Width, _window.Height);
            }

            var screen = _window.Screens.ScreenFromWindow(_window) ?? _window.Screens.Primary;
            _window.WindowState = WindowState.Normal;
            _window.SystemDecorations = SystemDecorations.None;
            _window.CanResize = false;
            if (screen is not null)
            {
                _window.Position = screen.Bounds.Position;
                _window.Width = screen.Bounds.Width / screen.Scaling;
                _window.Height = screen.Bounds.Height / screen.Scaling;
            }

            AppKit.AutoHideDockAndMenuBar(true);
            CoverScreen(_window, () => IsOn);
            _window.Activate();
        }
        else
        {
            AppKit.AutoHideDockAndMenuBar(false);
            AppKit.CoverScreen(_window, false);
            Chrome.UseLucentFrame(_window);
            _window.CanResize = true;
            _window.WindowState = _stateBefore;
            if (_stateBefore == WindowState.Normal && _sizeBefore.Width > 0)
            {
                _window.Position = _positionBefore;
                _window.Width = _sizeBefore.Width;
                _window.Height = _sizeBefore.Height;
            }
        }

        UpdateButton();
        if (!IsOn && _takenOut)
        {
            _takenOut = false;
            SingleWindow.PutBack(_window);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// While <paramref name="isOn"/>, keeps <paramref name="window"/> over its whole screen whenever it's the window
    /// in front. A dialog or another app taking over puts it back at an ordinary window's level, so they show.
    /// </summary>
    public static void CoverScreenWhile(Window window, Func<bool> isOn)
    {
        window.Activated += (_, _) =>
        {
            if (isOn())
            {
                CoverScreen(window, isOn);
            }
        };
        window.Deactivated += (_, _) =>
        {
            if (isOn())
            {
                AppKit.CoverScreen(window, false);
            }
        };
    }

    /// <summary>
    /// Covers the whole screen, the menu bar's strip beside the notch included: now, and again once the window's
    /// new size has been applied, which would otherwise leave it short of the top.
    /// </summary>
    public static void CoverScreen(Window window, Func<bool> isOn)
    {
        AppKit.CoverScreen(window, true);
        Dispatcher.UIThread.Post(() =>
        {
            if (isOn() && window.IsActive)
            {
                AppKit.CoverScreen(window, true);
            }
        }, DispatcherPriority.Background);
    }

    private void UpdateButton()
    {
        if (_window.TryFindResource(IsOn ? "ExitFullScreenCaptionIcon" : "FullScreenCaptionIcon", _window.ActualThemeVariant, out var icon))
        {
            _icon.Data = icon as Geometry;
        }

        ToolTip.SetTip(_button, IsOn ? "Exit full screen (⌃⌘F or Esc)" : "Full screen (⌃⌘F)");
    }
}
