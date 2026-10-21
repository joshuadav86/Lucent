using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Shell;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// The lock in a window's title bar. Locked, the window stays exactly where it is: it can't be moved
/// or resized, it doesn't move with the player, and docking or laying out the windows leaves it be.
/// The padlock shuts while it's locked. See <see cref="WindowLocks"/>.
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

    private void Show()
    {
        var locked = WindowLocks.IsLocked(_window);
        IsChecked = locked;
        _icon.Data = (Geometry)FindResource(locked ? "LockIcon" : "UnlockIcon");
        ToolTip = locked
            ? "Locked in place: it can't be moved or resized, and stays put when the player moves or the windows are docked. Click to unlock."
            : "Lock this window in place, so it can't be moved or resized, and stays put when the player moves or the windows are docked";
    }
}
