using System.Diagnostics;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;

namespace Lucent.Mac.Interop;

/// <summary>Making Lucent the app that opens music and video files, from Options > Updates.</summary>
internal static partial class LaunchServices
{
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const uint AllRoles = 0xFFFFFFFF;

    // A person takes far longer to answer a prompt than Launch Services takes to turn a type down, so a type
    // that didn't take after this long was one they clicked Keep on.
    private static readonly TimeSpan Answered = TimeSpan.FromMilliseconds(300);

    // The file types Lucent's Info.plist says it opens (tools/mac/package-mac.sh lists the same; its videos are
    // everything that's a public.movie), each with the extension the Default player row names it by. macOS 26.4
    // and later asks people to confirm each type's change of default app, one prompt per type, so the button
    // asks about the music most people have, and Every file type asks about the rest as well.
    private static readonly (string Type, string Extension)[] Music =
    [
        ("public.mp3", ".mp3"), ("com.apple.m4a-audio", ".m4a"), ("org.xiph.flac", ".flac"),
        ("com.microsoft.waveform-audio", ".wav"), ("public.aac-audio", ".aac"), ("public.aiff-audio", ".aiff"),
        ("org.xiph.ogg-audio", ".ogg"), ("org.xiph.opus", ".opus"),
    ];

    private static readonly (string Type, string Extension)[] Others =
    [
        ("public.mpeg-4-audio", ".m4a"), ("com.microsoft.windows-media-wma", ".wma"), ("public.aifc-audio", ".aifc"),
        ("com.apple.coreaudio-format", ".caf"), ("public.ac3-audio", ".ac3"), ("public.enhanced-ac3-audio", ".eac3"),
        ("com.wavpack.audio", ".wv"), ("com.monkeysaudio.ape", ".ape"),
        ("public.mpeg-4", ".mp4"), ("com.apple.quicktime-movie", ".mov"), ("com.apple.m4v-video", ".m4v"),
        ("org.matroska.mkv", ".mkv"), ("org.webmproject.webm", ".webm"), ("public.avi", ".avi"),
        ("com.microsoft.windows-media-wmv", ".wmv"), ("public.mpeg", ".mpg"), ("public.mpeg-2-transport-stream", ".ts"),
        ("public.3gpp", ".3gp"),
    ];

    /// <summary>How making Lucent the default went.</summary>
    /// <param name="Any">Whether any type opens in Lucent now.</param>
    /// <param name="Kept">The extension of the type the person clicked Keep on, after which Lucent stopped asking.</param>
    public readonly record struct Change(bool Any, string? Kept);

    /// <summary>
    /// Makes Lucent the app that opens the common music types, or every music and video type it plays. Asks
    /// one type at a time, skipping those that open in Lucent already, and stops once the person keeps their
    /// current app for one. Waits for each of macOS's prompts, so call it off the UI thread.
    /// </summary>
    public static Change MakeDefault(bool everyType)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return new(false, null);
        }

        using var pool = AutoreleasePool.Begin();
        var bundle = BundleIdentifier();
        if (bundle == IntPtr.Zero)
        {
            return new(false, null);
        }

        var lucent = StringHelper.Read(new NSString(bundle));
        var any = false;
        foreach (var (type, extension) in TypesFor(everyType))
        {
            var contentType = StringHelper.Create(type).NativePtr;
            if (OpensIn(contentType, lucent))
            {
                any = true;
                continue;
            }

            var asked = Stopwatch.StartNew();
            if (LSSetDefaultRoleHandlerForContentType(contentType, AllRoles, bundle) == 0 && OpensIn(contentType, lucent))
            {
                any = true;
            }
            else if (asked.Elapsed >= Answered)
            {
                return new(any, extension);
            }
        }

        return new(any, null);
    }

    /// <summary>
    /// Each music type the button sets (or each music and video type) that opens in another app when it's
    /// double-clicked, with that app's name (null if macOS doesn't say). Asks Launch Services, so it's true
    /// whatever was chosen, here or in the Finder.
    /// </summary>
    public static IReadOnlyList<(string Extension, string? App)> TypesElsewhere(bool everyType)
    {
        var elsewhere = new List<(string Extension, string? App)>();
        if (!OperatingSystem.IsMacOS())
        {
            return elsewhere;
        }

        using var pool = AutoreleasePool.Begin();
        var bundle = BundleIdentifier();
        var lucent = bundle == IntPtr.Zero ? null : StringHelper.Read(new NSString(bundle));
        foreach (var (type, extension) in TypesFor(everyType))
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

    private static IEnumerable<(string Type, string Extension)> TypesFor(bool everyType) =>
        everyType ? Music.Concat(Others) : Music;

    // Whether double-clicking the type opens Lucent now.
    private static bool OpensIn(IntPtr contentType, string? lucent)
    {
        var handler = LSCopyDefaultRoleHandlerForContentType(contentType, AllRoles);
        if (handler == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return string.Equals(StringHelper.Read(new NSString(handler)), lucent, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CFRelease(handler);
        }
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
