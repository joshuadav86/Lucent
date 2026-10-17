using System.Runtime.InteropServices;
using System.Text;
using SharpMetal.Foundation;

namespace Lucent.Mac.Interop;

internal static class StringHelper
{
    /// <summary>An autoreleased NSString with the same text, for Metal calls that take one.</summary>
    public static unsafe NSString Create(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* utf8 = bytes)
        {
            return new NSString(ObjC.Send(ObjC.Class("NSString"), "stringWithUTF8String:", (IntPtr)utf8));
        }
    }

    /// <summary>The text of an NSString, or null for none.</summary>
    public static string? Read(NSString text) =>
        text.NativePtr == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(ObjC.Send(text.NativePtr, "UTF8String"));
}
