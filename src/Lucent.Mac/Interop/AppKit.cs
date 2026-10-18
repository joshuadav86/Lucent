namespace Lucent.Mac.Interop;

/// <summary>
/// The few bits of AppKit the player needs that Avalonia has no API for: whether ⌘ or a mouse button is
/// held right now (for resizing windows together), and hiding the app from its menu.
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

    private static void SendToApp(string selector)
    {
        if (OperatingSystem.IsMacOS())
        {
            ObjC.SendVoid(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), selector, IntPtr.Zero);
        }
    }
}
