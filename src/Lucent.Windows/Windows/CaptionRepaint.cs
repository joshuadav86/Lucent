using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Lucent.Windows;

/// <summary>
/// Changes a window's frame style (its ResizeMode or WindowStyle) without Windows painting its own old-style
/// minimize and maximize buttons over Lucent's title bar.
/// </summary>
/// <remarks>
/// Lucent's windows draw their own title bars (WindowChrome), but Windows still thinks they have a caption.
/// When the style changes on a visible window, Windows paints its default caption buttons straight onto the
/// window, and they stay there until something redraws that corner. Switching between a classic skin and
/// Lucent's own player does exactly that. WPF already hides the window's visible flag while the title or icon
/// changes for the same reason; this does the same around a style change, then redraws the whole window.
/// </remarks>
public static class CaptionRepaint
{
    private const int GWL_STYLE = -16;
    private const int WS_VISIBLE = 0x10000000;
    private const uint RDW_INVALIDATE = 0x0001;
    private const uint RDW_ALLCHILDREN = 0x0080;
    private const uint RDW_UPDATENOW = 0x0100;
    private const uint RDW_FRAME = 0x0400;

    /// <summary>Runs <paramref name="change"/> (which sets the window's ResizeMode or WindowStyle) without stray caption buttons.</summary>
    public static void Change(Window window, Action change)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !window.IsVisible)
        {
            change();
            return;
        }

        var style = GetWindowLong(handle, GWL_STYLE);
        var hidden = (style & WS_VISIBLE) != 0;
        if (hidden)
        {
            SetWindowLong(handle, GWL_STYLE, style & ~WS_VISIBLE);
        }

        try
        {
            change();
        }
        finally
        {
            if (hidden)
            {
                SetWindowLong(handle, GWL_STYLE, GetWindowLong(handle, GWL_STYLE) | WS_VISIBLE);
            }

            RedrawWindow(handle, IntPtr.Zero, IntPtr.Zero, RDW_INVALIDATE | RDW_FRAME | RDW_ALLCHILDREN | RDW_UPDATENOW);
        }
    }

    /// <summary>Sets the window's ResizeMode, if it's different, without stray caption buttons.</summary>
    public static void SetResizeMode(Window window, ResizeMode mode)
    {
        if (window.ResizeMode != mode)
        {
            Change(window, () => window.ResizeMode = mode);
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}
