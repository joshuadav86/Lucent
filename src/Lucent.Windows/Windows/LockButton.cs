using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace Lucent.Windows;

/// <summary>
/// The lock in a window's title bar. Locked, the window stays exactly where it is: it can't be moved
/// or resized, it doesn't move with the player, and docking or laying out the windows leaves it be.
/// The padlock shuts and lights up while it's locked, and is dimmed while it's open. See <see cref="WindowLocks"/>.
/// </summary>
public sealed class LockButton : ToggleButton
{
    private readonly Path _icon = new();
    private Window? _window;

    public LockButton()
    {
        Content = _icon;
        WindowChrome.SetIsHitTestVisibleInChrome(this, true);
        Loaded += (_, _) =>
        {
            _window = Window.GetWindow(this);
            WindowLocks.Changed += OnLockChanged;
            Show();
        };
        Unloaded += (_, _) => WindowLocks.Changed -= OnLockChanged;
    }

    // Locked another way (the player's menu, say): the button follows.
    private void OnLockChanged(object? sender, Window window)
    {
        if (window == _window)
        {
            Show();
        }
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        _icon.Style = (Style)FindResource("LineIcon");
        Show();
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_window is not null)
        {
            WindowLocks.SetLocked(_window, IsChecked == true);
        }

        Show();
    }

    protected override void OnMouseEnter(MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        Fade();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Fade();
    }

    // An open padlock is dimmed, so the locked windows, with their shut padlock lit, stand out.
    // Pointed at, it lights up again.
    private void Fade() => _icon.Opacity = IsChecked == true || IsMouseOver ? 1 : 0.4;

    private void Show()
    {
        var locked = WindowLocks.IsLocked(_window);
        IsChecked = locked;
        Fade();
        _icon.Data = (Geometry)FindResource(locked ? "LockIcon" : "UnlockIcon");
        ToolTip = locked
            ? "Locked in place: it can't be moved or resized, and stays put when the player moves or the windows are docked. Click to unlock."
            : "Lock this window in place, so it can't be moved or resized, and stays put when the player moves or the windows are docked";
    }
}
