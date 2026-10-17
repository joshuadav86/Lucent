using System.Runtime.InteropServices;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Editor;

/// <summary>
/// Audio on the Mac's clipboard (the general NSPasteboard), the stand-in for WPF's Clipboard.SetAudio and
/// GetAudioStream. Audio goes on as a WAV, which other audio apps take; a WAV or AIFF put there by another
/// app can be read back. Every call fails quietly (returns false or null) if the clipboard can't be used.
/// </summary>
internal static partial class AudioClipboard
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    /// <summary>The type identifier of WAV audio.</summary>
    public const string WaveType = "com.microsoft.waveform-audio";

    /// <summary>The type identifier of AIFF audio, which some Mac apps copy instead.</summary>
    public const string AiffType = "public.aiff-audio";

    /// <summary>
    /// The clipboard's change count, which goes up whenever anything is put on it: what Lucent put there is
    /// still there if the count is the same as just after it did. Null when the clipboard can't be read.
    /// </summary>
    public static long? ChangeCount()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            return (long)ObjC.Send(Pasteboard(), "changeCount");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>True when the clipboard holds a WAV or AIFF.</summary>
    public static bool ContainsAudio() => AvailableType() is not null;

    /// <summary>Puts a WAV on the clipboard, replacing what's there. Returns the change count after, or null if it failed.</summary>
    public static unsafe long? SetWave(byte[] wave)
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            using var pool = AutoreleasePool.Begin();
            var pasteboard = Pasteboard();
            ObjC.Send(pasteboard, "clearContents");
            IntPtr data;
            fixed (byte* bytes = wave)
            {
                data = Send_IntPtr_NUInt(ObjC.Class("NSData"), ObjC.Selector("dataWithBytes:length:"), (IntPtr)bytes, (nuint)wave.Length);
            }

            if (data == IntPtr.Zero || SendBool_IntPtr_IntPtr(pasteboard, ObjC.Selector("setData:forType:"), data, NSString(WaveType)) == 0)
            {
                return null;
            }

            return (long)ObjC.Send(pasteboard, "changeCount");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The audio on the clipboard and its type (<see cref="WaveType"/> or <see cref="AiffType"/>), or null.</summary>
    public static (byte[] Bytes, string Type)? GetAudio()
    {
        if (AvailableType() is not { } type)
        {
            return null;
        }

        try
        {
            using var pool = AutoreleasePool.Begin();
            var data = ObjC.Send(Pasteboard(), "dataForType:", NSString(type));
            if (data == IntPtr.Zero)
            {
                return null;
            }

            var length = (long)ObjC.Send(data, "length");
            var pointer = ObjC.Send(data, "bytes");
            if (length <= 0 || pointer == IntPtr.Zero || length > int.MaxValue)
            {
                return null;
            }

            var bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, (int)length);
            return (bytes, type);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? AvailableType()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return null;
        }

        try
        {
            using var pool = AutoreleasePool.Begin();
            var types = ObjC.Send(Pasteboard(), "types");
            if (types == IntPtr.Zero)
            {
                return null;
            }

            foreach (var type in new[] { WaveType, AiffType })
            {
                if (SendBool_IntPtr(types, ObjC.Selector("containsObject:"), NSString(type)) != 0)
                {
                    return type;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IntPtr Pasteboard() => ObjC.Send(ObjC.Class("NSPasteboard"), "generalPasteboard");

    /// <summary>An autoreleased NSString.</summary>
    private static unsafe IntPtr NSString(string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text + "\0");
        fixed (byte* utf8 = bytes)
        {
            return ObjC.Send(ObjC.Class("NSString"), "stringWithUTF8String:", (IntPtr)utf8);
        }
    }

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send_IntPtr_NUInt(IntPtr receiver, IntPtr selector, IntPtr pointer, nuint length);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte SendBool_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte SendBool_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);
}
