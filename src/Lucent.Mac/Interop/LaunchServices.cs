using System.Runtime.InteropServices;
using SharpMetal.Foundation;

namespace Lucent.Mac.Interop;

/// <summary>Making Lucent the app that opens music and video files, from Options > Updates.</summary>
internal static partial class LaunchServices
{
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint AllRoles = 0xFFFFFFFF;

    // The file types Lucent's Info.plist says it opens (tools/mac/package-mac.sh lists the same; its videos are
    // everything that's a public.movie), each with the extension the Default player row names it by.
    private static readonly (string Type, string Extension)[] Types =
    [
        ("public.mp3", ".mp3"), ("com.microsoft.waveform-audio", ".wav"), ("org.xiph.flac", ".flac"),
        ("public.mpeg-4-audio", ".m4a"), ("com.apple.m4a-audio", ".m4a"), ("public.aac-audio", ".aac"),
        ("com.microsoft.windows-media-wma", ".wma"), ("public.aiff-audio", ".aiff"), ("public.aifc-audio", ".aifc"),
        ("org.xiph.ogg-audio", ".ogg"), ("org.xiph.opus", ".opus"), ("com.apple.coreaudio-format", ".caf"),
        ("public.ac3-audio", ".ac3"), ("public.enhanced-ac3-audio", ".eac3"),
        ("com.wavpack.audio", ".wv"), ("com.monkeysaudio.ape", ".ape"),
        ("public.mpeg-4", ".mp4"), ("com.apple.quicktime-movie", ".mov"), ("com.apple.m4v-video", ".m4v"),
        ("org.matroska.mkv", ".mkv"), ("org.webmproject.webm", ".webm"), ("public.avi", ".avi"),
        ("com.microsoft.windows-media-wmv", ".wmv"), ("public.mpeg", ".mpg"), ("public.mpeg-2-transport-stream", ".ts"),
        ("public.3gpp", ".3gp"),
    ];

    /// <summary>
    /// Makes Lucent the app that opens every music and video type it plays. False if none could be set,
    /// such as when Lucent isn't running from Lucent.app.
    /// </summary>
    public static bool MakeDefault()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        var bundle = BundleIdentifier();
        if (bundle == IntPtr.Zero)
        {
            return false;
        }

        var set = 0;
        foreach (var (type, _) in Types)
        {
            if (LSSetDefaultRoleHandlerForContentType(StringHelper.Create(type).NativePtr, AllRoles, bundle) == 0)
            {
                set++;
            }
        }

        return set > 0;
    }

    /// <summary>
    /// Each music and video type that opens in another app when it's double-clicked, with that app's name
    /// (null if macOS doesn't say). Asks Launch Services, so it's true whatever was chosen, here or in the Finder.
    /// </summary>
    public static IReadOnlyList<(string Extension, string? App)> TypesElsewhere()
    {
        var elsewhere = new List<(string Extension, string? App)>();
        if (!OperatingSystem.IsMacOS())
        {
            return elsewhere;
        }

        var bundle = BundleIdentifier();
        var lucent = bundle == IntPtr.Zero ? null : StringHelper.Read(new NSString(bundle));
        foreach (var (type, extension) in Types)
        {
            // Nothing opens it yet: no other app to report.
            var handler = LSCopyDefaultRoleHandlerForContentType(StringHelper.Create(type).NativePtr, AllRoles);
            if (handler == IntPtr.Zero)
            {
                continue;
            }

            try
            {
                var id = StringHelper.Read(new NSString(handler));
                if (id is not null && !string.Equals(id, lucent, StringComparison.OrdinalIgnoreCase)
                    && elsewhere.All(other => other.Extension != extension))
                {
                    elsewhere.Add((extension, AppName(handler)));
                }
            }
            finally
            {
                CFRelease(handler);
            }
        }

        return elsewhere;
    }

    // Lucent's bundle identifier as an NSString, or zero when it isn't running from Lucent.app.
    private static IntPtr BundleIdentifier()
    {
        var bundle = ObjC.Send(ObjC.Send(ObjC.Class("NSBundle"), "mainBundle"), "bundleIdentifier");
        return bundle != IntPtr.Zero && StringHelper.Read(new NSString(bundle)) is not null ? bundle : IntPtr.Zero;
    }

    // "VLC" for org.videolan.vlc: the app's name in the Finder, without ".app".
    private static string? AppName(IntPtr bundleIdentifier)
    {
        var workspace = ObjC.Send(ObjC.Class("NSWorkspace"), "sharedWorkspace");
        var url = ObjC.Send(workspace, "URLForApplicationWithBundleIdentifier:", bundleIdentifier);
        if (url == IntPtr.Zero)
        {
            return null;
        }

        var name = StringHelper.Read(new NSString(ObjC.Send(url, "lastPathComponent")));
        return name is null ? null : Path.GetFileNameWithoutExtension(name);
    }

    [LibraryImport(CoreServices)]
    private static partial IntPtr LSCopyDefaultRoleHandlerForContentType(IntPtr contentType, uint role);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(IntPtr contentType, uint role, IntPtr handlerBundleId);

    [LibraryImport(CoreServices)]
    private static partial void CFRelease(IntPtr item);
}
