using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// Just enough of the Objective-C runtime to make the view the visuals draw into and to drive its
/// Core Animation Metal layer. Metal itself goes through SharpMetal.
/// </summary>
internal static partial class ObjC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";

    [StructLayout(LayoutKind.Sequential)]
    public struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CGRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    static ObjC()
    {
        // AppKit, QuartzCore and Metal have to be loaded before their classes can be looked up.
        foreach (var framework in new[] { "AppKit", "QuartzCore", "Metal" })
        {
            NativeLibrary.TryLoad($"/System/Library/Frameworks/{framework}.framework/{framework}", out _);
        }
    }

    public static IntPtr Class(string name) => objc_getClass(name);

    public static IntPtr Selector(string name) => sel_registerName(name);

    public static IntPtr Send(IntPtr receiver, string selector) => IntPtr_objc_msgSend(receiver, Selector(selector));

    public static IntPtr Send(IntPtr receiver, string selector, IntPtr argument) =>
        IntPtr_objc_msgSend_IntPtr(receiver, Selector(selector), argument);

    public static IntPtr Send(IntPtr receiver, string selector, CGRect argument) =>
        IntPtr_objc_msgSend_CGRect(receiver, Selector(selector), argument);

    public static void SendVoid(IntPtr receiver, string selector) => void_objc_msgSend(receiver, Selector(selector));

    public static void SendVoid(IntPtr receiver, string selector, IntPtr argument) =>
        void_objc_msgSend_IntPtr(receiver, Selector(selector), argument);

    public static void SendVoid(IntPtr receiver, string selector, bool argument) =>
        void_objc_msgSend_Byte(receiver, Selector(selector), argument ? (byte)1 : (byte)0);

    public static void SendVoid(IntPtr receiver, string selector, ulong argument) =>
        void_objc_msgSend_ULong(receiver, Selector(selector), argument);

    public static void SendVoid(IntPtr receiver, string selector, double argument) =>
        void_objc_msgSend_Double(receiver, Selector(selector), argument);

    public static void SendVoid(IntPtr receiver, string selector, CGSize argument) =>
        void_objc_msgSend_CGSize(receiver, Selector(selector), argument);

    public static double SendDouble(IntPtr receiver, string selector) => double_objc_msgSend(receiver, Selector(selector));

    public static bool SendBool(IntPtr receiver, string selector) => byte_objc_msgSend(receiver, Selector(selector)) != 0;

    public static bool SendBool(IntPtr receiver, string selector, IntPtr argument) =>
        byte_objc_msgSend_IntPtr(receiver, Selector(selector), argument) != 0;

    /// <summary>
    /// Makes (once) a subclass of an AppKit class, with some of its methods replaced by C# functions
    /// (each an [UnmanagedCallersOnly] function pointer and its Objective-C type string).
    /// </summary>
    public static IntPtr Subclass(string name, string superclass, params (string Selector, IntPtr Function, string Types)[] methods)
    {
        var existing = objc_getClass(name);
        if (existing != IntPtr.Zero)
        {
            return existing;
        }

        var subclass = objc_allocateClassPair(Class(superclass), name, 0);
        foreach (var (selector, function, types) in methods)
        {
            class_addMethod(subclass, Selector(selector), function, types);
        }

        objc_registerClassPair(subclass);
        return subclass;
    }

    /// <summary>[[Class alloc] init].</summary>
    public static IntPtr New(string className) => Send(Send(Class(className), "alloc"), "init");

    public static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            SendVoid(instance, "release");
        }
    }

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr sel_registerName(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr objc_allocateClassPair(IntPtr superclass, string name, nuint extraBytes);

    [LibraryImport(Runtime)]
    private static partial void objc_registerClassPair(IntPtr subclass);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static partial bool class_addMethod(IntPtr subclass, IntPtr selector, IntPtr function, string types);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial double double_objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte byte_objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte byte_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_CGRect(IntPtr receiver, IntPtr selector, CGRect argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_Byte(IntPtr receiver, IntPtr selector, byte argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_ULong(IntPtr receiver, IntPtr selector, ulong argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_Double(IntPtr receiver, IntPtr selector, double argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_CGSize(IntPtr receiver, IntPtr selector, CGSize argument);
}

/// <summary>An autorelease pool for one pass of a background loop, so the objects Metal hands back autoreleased are freed.</summary>
internal readonly struct AutoreleasePool : IDisposable
{
    private readonly IntPtr _pool;

    private AutoreleasePool(IntPtr pool) => _pool = pool;

    public static AutoreleasePool Begin() => new(ObjC.New("NSAutoreleasePool"));

    public void Dispose() => ObjC.SendVoid(_pool, "drain");
}
