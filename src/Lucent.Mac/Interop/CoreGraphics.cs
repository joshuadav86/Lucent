using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>The colour spaces the visuals' Metal layer draws in.</summary>
internal static partial class CoreGraphics
{
    private const string Framework = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    private static readonly Lazy<IntPtr> ExtendedLinearSrgbSpace = new(() => Create("kCGColorSpaceExtendedLinearSRGB"));
    private static readonly Lazy<IntPtr> SrgbSpace = new(() => Create("kCGColorSpaceSRGB"));

    /// <summary>
    /// Linear light with sRGB's primaries, where 1.0 is the screen's normal white and brighter values go
    /// beyond it (EDR): the Mac's match for the scRGB the Windows app draws in for HDR.
    /// </summary>
    public static IntPtr ExtendedLinearSrgb => ExtendedLinearSrgbSpace.Value;

    public static IntPtr Srgb => SrgbSpace.Value;

    /// <summary>A colour space by its name constant (kCGColorSpace...), kept for the life of the app.</summary>
    private static IntPtr Create(string constant)
    {
        var library = NativeLibrary.Load(Framework);
        var name = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, constant));
        return CGColorSpaceCreateWithName(name);
    }

    [LibraryImport(Framework)]
    private static partial IntPtr CGColorSpaceCreateWithName(IntPtr name);
}
