using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// The outline shown over a window while another is dragged onto it, saying that dropping there swaps
/// the two (<see cref="WindowSnapper"/>). It sits on top of everything, in the accent colour, and the
/// mouse goes straight through it.
/// </summary>
public sealed class SwapHighlight : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    private readonly TextBlock _label;

    public SwapHighlight()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = 1;
        Height = 1;

        var fill = new Border { Opacity = 0.22 };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        _label = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        var badge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _label,
        };
        badge.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var outline = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6) };
        outline.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        Content = new Grid { Children = { fill, outline, badge } };

        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            SetWindowLong(handle, GWL_EXSTYLE, GetWindowLong(handle, GWL_EXSTYLE) | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };
    }

    /// <summary>Shows the outline over this rectangle of the screen (in pixels), naming the window it's over.</summary>
    public void ShowAt(int left, int top, int right, int bottom, string? name)
    {
        _label.Text = string.IsNullOrEmpty(name) ? "Swap" : $"Swap with {name}";
        if (!IsVisible)
        {
            Show();
        }

        SetWindowPos(new WindowInteropHelper(this).Handle, IntPtr.Zero, left, top, right - left, bottom - top, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
