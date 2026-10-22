using System.Runtime.InteropServices;
using SharpMetal.Foundation;

namespace Lucent.Mac.Interop;

/// <summary>Making Lucent the app that opens music files, from Options > Updates.</summary>
internal static partial class LaunchServices
{
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint AllRoles = 0xFFFFFFFF;

    // The music file types Lucent's Info.plist says it opens (tools/package-mac.sh lists the same).
    private static readonly string[] MusicTypes =
    [
        "public.mp3", "com.microsoft.waveform-audio", "org.xiph.flac", "public.mpeg-4-audio", "com.apple.m4a-audio",
        "public.aac-audio", "com.microsoft.windows-media-wma", "public.aiff-audio", "public.aifc-audio",
        "org.xiph.ogg-audio", "org.xiph.opus", "com.apple.coreaudio-format", "public.ac3-audio", "public.enhanced-ac3-audio",
    ];

    /// <summary>
    /// Makes Lucent the app that opens every music file type it plays. False if none could be set,
    /// such as when Lucent isn't running from Lucent.app.
    /// </summary>
    public static bool MakeDefaultForMusic()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var bundle = ObjC.Send(ObjC.Send(ObjC.Class("NSBundle"), "mainBundle"), "bundleIdentifier");
        if (bundle == IntPtr.Zero || StringHelper.Read(new NSString(bundle)) is null)
        {
            return false;
        }

        var set = 0;
        foreach (var type in MusicTypes)
        {
            if (LSSetDefaultRoleHandlerForContentType(StringHelper.Create(type).NativePtr, AllRoles, bundle) == 0)
            {
                set++;
            }
        }

        return set > 0;
    }

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(IntPtr contentType, uint role, IntPtr handlerBundleId);
}
