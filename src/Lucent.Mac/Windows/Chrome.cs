using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Lucent.Mac.Windows;

/// <summary>
/// Lucent's own window frames. Every Lucent window draws its own title bar and buttons, as on Windows
/// (where they're made with WindowChrome): the window asks for no system title bar with
/// <see cref="UseLucentFrame"/>, and marks the parts that drag the window with <see cref="IsCaptionProperty"/>,
/// which stands in for WPF's WindowChrome.CaptionHeight.
/// </summary>
public static class Chrome
{
    /// <summary>
    /// Set on a title bar (or any part of a window) to make pressing and dragging it move the window.
    /// Buttons, sliders and text boxes inside it keep working: only presses that land on nothing
    /// clickable move the window.
    /// </summary>
    public static readonly AttachedProperty<bool> IsCaptionProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsCaption", typeof(Chrome));

    static Chrome()
    {
        IsCaptionProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            if (e.NewValue is true)
            {
                control.AddHandler(InputElement.PointerPressedEvent, OnCaptionPressed, RoutingStrategies.Bubble, handledEventsToo: false);
            }
            else
            {
                control.RemoveHandler(InputElement.PointerPressedEvent, OnCaptionPressed);
            }
        });
    }

    public static bool GetIsCaption(Control control) => control.GetValue(IsCaptionProperty);

    public static void SetIsCaption(Control control, bool value) => control.SetValue(IsCaptionProperty, value);

    /// <summary>
    /// Takes away the system title bar and its traffic-light buttons, keeping the window's shadow and
    /// rounded frame, so the window's own title bar (drawn by Lucent) is the only one.
    /// </summary>
    public static void UseLucentFrame(Window window)
    {
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = 0;
        window.SystemDecorations = SystemDecorations.Full;
    }

    private static void OnCaptionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || sender is not Control caption || !e.GetCurrentPoint(caption).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // A press on a button (or anything else that takes clicks) is the button's, not the frame's.
        for (var element = e.Source as Visual; element is not null && element != caption; element = element.GetVisualParent())
        {
            if (element is Button or Slider or TextBox or ComboBox or ListBoxItem or Thumb)
            {
                return;
            }
        }

        if (caption.GetVisualRoot() is Window window)
        {
            window.BeginMoveDrag(e);
            e.Handled = true;
        }
    }
}
