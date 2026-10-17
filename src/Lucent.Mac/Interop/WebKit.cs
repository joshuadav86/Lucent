using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// What the Bandcamp sign-in needs beyond <see cref="ObjC"/>: WebKit's classes, a few more message shapes,
/// NSStrings, Objective-C classes made at run time (for WebKit's delegates) and blocks (for its
/// completion handlers).
/// </summary>
internal static unsafe partial class WebKit
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    // Block_layout flags: the block lives forever (never copied or freed), and carries its type signature.
    private const int BlockIsGlobal = 1 << 28;
    private const int BlockHasSignature = 1 << 30;

    private static readonly IntPtr GlobalBlockClass;

    static WebKit()
    {
        // Touching ObjC loads AppKit (and Foundation with it) first.
        ObjC.Class("NSObject");
        NativeLibrary.TryLoad("/System/Library/Frameworks/WebKit.framework/WebKit", out _);
        if (NativeLibrary.TryLoad("/usr/lib/libSystem.B.dylib", out var system) &&
            NativeLibrary.TryGetExport(system, "_NSConcreteGlobalBlock", out var globalBlock))
        {
            GlobalBlockClass = globalBlock;
        }
        else if (NativeLibrary.TryLoad("/usr/lib/system/libsystem_blocks.dylib", out var blocks) &&
                 NativeLibrary.TryGetExport(blocks, "_NSConcreteGlobalBlock", out globalBlock))
        {
            GlobalBlockClass = globalBlock;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
        public IntPtr Signature;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    /// <summary>
    /// A block that calls <paramref name="invoke"/> (whose first argument is the block itself). It's a
    /// global block, so WebKit's copies and releases leave it alone; it's made once and kept for good.
    /// </summary>
    /// <param name="signature">The block's Objective-C type encoding, like "v16@?0@8" for void (^)(id).</param>
    public static IntPtr GlobalBlock(IntPtr invoke, string signature)
    {
        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        descriptor->Signature = Marshal.StringToCoTaskMemUTF8(signature);
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = GlobalBlockClass;
        block->Flags = BlockIsGlobal | BlockHasSignature;
        block->Invoke = invoke;
        block->Descriptor = (IntPtr)descriptor;
        return (IntPtr)block;
    }

    /// <summary>Makes (once) an NSObject subclass with these methods, for a delegate.</summary>
    public static IntPtr DefineClass(string name, params (string Selector, IntPtr Implementation, string Types)[] methods)
    {
        var existing = ObjC.Class(name);
        if (existing != IntPtr.Zero)
        {
            return existing;
        }

        var type = objc_allocateClassPair(ObjC.Class("NSObject"), name, 0);
        foreach (var (selector, implementation, types) in methods)
        {
            class_addMethod(type, ObjC.Selector(selector), implementation, types);
        }

        objc_registerClassPair(type);
        return type;
    }

    /// <summary>Adds a protocol to a class made with <see cref="DefineClass"/>, so WebKit sees it as conforming.</summary>
    public static void AddProtocol(IntPtr type, string protocol)
    {
        var found = objc_getProtocol(protocol);
        if (found != IntPtr.Zero)
        {
            class_addProtocol(type, found);
        }
    }

    /// <summary>An autoreleased NSString.</summary>
    public static IntPtr String(string text)
    {
        var utf8 = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return ObjC.Send(ObjC.Class("NSString"), "stringWithUTF8String:", utf8);
        }
        finally
        {
            Marshal.FreeCoTaskMem(utf8);
        }
    }

    /// <summary>The text of an NSString, or an empty string for nil.</summary>
    public static string ReadString(IntPtr text) =>
        text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(ObjC.Send(text, "UTF8String")) ?? string.Empty;

    public static nuint Count(IntPtr array) => array == IntPtr.Zero ? 0 : (nuint)ObjC.Send(array, "count");

    public static IntPtr ObjectAt(IntPtr array, nuint index) => IntPtr_msgSend_nuint(array, ObjC.Selector("objectAtIndex:"), index);

    public static IntPtr Send(IntPtr receiver, string selector, ObjC.CGRect rect, IntPtr argument) =>
        IntPtr_msgSend_CGRect_IntPtr(receiver, ObjC.Selector(selector), rect, argument);

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr first, IntPtr second, IntPtr third) =>
        IntPtr_msgSend_IntPtr3(receiver, ObjC.Selector(selector), first, second, third);

    public static void SendVoid(IntPtr receiver, string selector, IntPtr first, IntPtr second, IntPtr third) =>
        void_msgSend_IntPtr3(receiver, ObjC.Selector(selector), first, second, third);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [LibraryImport(Runtime)]
    private static partial void objc_registerClassPair(IntPtr type);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(IntPtr type, IntPtr selector, IntPtr implementation, string types);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getProtocol(string name);

    [LibraryImport(Runtime)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addProtocol(IntPtr type, IntPtr protocol);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_CGRect_IntPtr(IntPtr receiver, IntPtr selector, ObjC.CGRect rect, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_IntPtr3(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second, IntPtr third);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_msgSend_IntPtr3(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second, IntPtr third);
}
