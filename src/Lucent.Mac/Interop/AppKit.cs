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

    private static void SendToApp(string selector)
    {
        if (OperatingSystem.IsMacOS())
        {
            ObjC.SendVoid(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), selector, IntPtr.Zero);
        }
    }
}
