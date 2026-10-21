using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using Path = Avalonia.Controls.Shapes.Path;

namespace Lucent.Mac.Windows;

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
    }

    protected override Type StyleKeyOverride => typeof(ToggleButton);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (this.TryFindResource("LineIcon", ActualThemeVariant, out var theme) && theme is ControlTheme lineIcon)
        {
            _icon.Theme = lineIcon;
        }

        _window = TopLevel.GetTopLevel(this) as Window;
        WindowLocks.Changed += OnLockChanged;
        if (_window is not null)
        {
            WindowLocks.Follow(_window);
        }

        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        WindowLocks.Changed -= OnLockChanged;
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

    // Locked another way (the player's menu, say): the button follows.
    private void OnLockChanged(object? sender, Window window)
    {
        if (window == _window)
        {
            Show();
        }
    }

    private void Show()
    {
        var locked = WindowLocks.IsLocked(_window);
        IsChecked = locked;
        if (this.TryFindResource(locked ? "LockIcon" : "UnlockIcon", ActualThemeVariant, out var icon) && icon is Geometry geometry)
        {
            _icon.Data = geometry;
        }

        ToolTip.SetTip(this, locked
            ? "Locked in place: it can't be moved or resized, and stays put when the player moves or the windows are docked. Click to unlock."
            : "Lock this window in place, so it can't be moved or resized, and stays put when the player moves or the windows are docked");
    }
}
