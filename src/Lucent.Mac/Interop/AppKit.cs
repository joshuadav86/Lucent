using Avalonia.Controls;
using Avalonia.Platform;

namespace Lucent.Mac.Interop;

/// <summary>
/// The few bits of AppKit the player needs that Avalonia has no API for: whether ⌘ or a mouse button is
/// held right now (for resizing windows together), hiding the app from its menu, and hiding the Dock
/// and the menu bar in fullscreen.
/// </summary>
internal static class AppKit
{
    private const long CommandKeyMask = 1L << 20;

    /// <summary>True while ⌘ is held. Always false when not on a Mac.</summary>
    public static bool IsCommandDown =>
        OperatingSystem.IsMacOS() && ((long)ObjC.Send(ObjC.Class("NSEvent"), "modifierFlags") & CommandKeyMask) != 0;

    /// <summary>True while the left mouse button is held. Always false when not on a Mac.</summary>
    public static bool IsMouseDown =>
        OperatingSystem.IsMacOS() && ((long)ObjC.Send(ObjC.Class("NSEvent"), "pressedMouseButtons") & 1) != 0;

    /// <summary>The standard About panel, with the app's name, icon and version.</summary>
    public static void ShowAboutPanel() => SendToApp("orderFrontStandardAboutPanel:");

    /// <summary>Hide Lucent (⌘H).</summary>
    public static void HideApp() => SendToApp("hide:");

    /// <summary>Hide every other app (⌥⌘H).</summary>
    public static void HideOtherApps() => SendToApp("hideOtherApplications:");

    /// <summary>Show every app again.</summary>
    public static void ShowAllApps() => SendToApp("unhideAllApplications:");

    // NSApplicationPresentationOptions: the Dock and the menu bar hide, and slide back in if the
    // mouse goes to them, as in any Mac app's fullscreen.
    private const ulong AutoHideDockAndMenuBarOptions = 1UL << 0 | 1UL << 2;

    /// <summary>Hides (or brings back) the Dock and the menu bar while a window fills the screen.</summary>
    public static void AutoHideDockAndMenuBar(bool hide)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var application = ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication");
            ObjC.SendVoid(application, "setPresentationOptions:", hide ? AutoHideDockAndMenuBarOptions : 0UL);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not on a Mac (the screenshot tool): nothing to hide.
        }
    }

    // NSMainMenuWindowLevel + 1. On a MacBook with a notch the strip beside it, where the menu bar is, is kept
    // clear of ordinary windows even while the menu bar is hidden; a window above the menu bar's level isn't.
    private const ulong AboveMenuBarLevel = 25;
    private const ulong NormalLevel = 0;
    private const ulong FloatingLevel = 3;

    /// <summary>
    /// Stretches a window that's filling its screen over the whole of it, the menu bar's strip beside the notch
    /// too, by lifting it above the menu bar. <paramref name="cover"/> false puts it back at its usual level
    /// (the window's frame is left to the caller), so a dialog or another app can come in front of it.
    /// </summary>
    public static void CoverScreen(Window window, bool cover)
    {
        if (!OperatingSystem.IsMacOS()
            || window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle { NSWindow: var native }
            || native == IntPtr.Zero)
        {
            return;
        }

        try
        {
            if (!cover)
            {
                ObjC.SendVoid(native, "setLevel:", window.Topmost ? FloatingLevel : NormalLevel);
                return;
            }

            var screen = ObjC.Send(native, "screen");
            if (screen == IntPtr.Zero)
            {
                return;
            }

            ObjC.SendVoid(native, "setLevel:", AboveMenuBarLevel);
            ObjC.SendVoid(native, "setFrame:display:", ObjC.ReadValue<ObjC.CGRect>(screen, "frame"), true);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not on a Mac (the screenshot tool).
        }
    }

    private static void SendToApp(string selector)
    {
        if (OperatingSystem.IsMacOS())
        {
            ObjC.SendVoid(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), selector, IntPtr.Zero);
        }
    }
}
