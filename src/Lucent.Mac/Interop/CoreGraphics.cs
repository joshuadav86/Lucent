using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>The colour spaces the visuals' Metal layer draws in, and drawing a picture into memory to look at it.</summary>
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

    // For reading back a picture (the self-test looks at a frame of a recorded video).

    /// <summary>kCGImageAlphaNoneSkipLast: four bytes a pixel, red, green and blue then one unused.</summary>
    public const uint RgbxBitmap = 5;

    [LibraryImport(Framework)]
    public static partial IntPtr CGBitmapContextCreate(IntPtr data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow,
        IntPtr colorSpace, uint bitmapInfo);

    /// <summary>Draws an image filling <paramref name="rect"/> (a struct of four doubles, passed by value).</summary>
    [LibraryImport(Framework)]
    public static partial void CGContextDrawImage(IntPtr context, ObjC.CGRect rect, IntPtr image);

    [LibraryImport(Framework)]
    public static partial void CGContextRelease(IntPtr context);

    [LibraryImport(Framework)]
    public static partial nuint CGImageGetWidth(IntPtr image);

    [LibraryImport(Framework)]
    public static partial nuint CGImageGetHeight(IntPtr image);

    [LibraryImport(Framework)]
    public static partial void CGImageRelease(IntPtr image);
}
