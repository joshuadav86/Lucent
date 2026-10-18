using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using SharpMetal.Foundation;
using static Lucent.Mac.Interop.CoreMedia;

namespace Lucent.Mac.Interop;

/// <summary>
/// The parts of AVFoundation that write a video (AVAssetWriter) and read one back for the self-test
/// (AVURLAsset), and the Foundation objects their settings are made of. Each objc_msgSend here is declared
/// with the method's exact argument types, since Objective-C methods aren't variadic: on arm64 especially,
/// every argument has to arrive in the register (or stack slot) the method reads it from, so a CMTime goes
/// by value as a struct, a double as a double and a BOOL as a byte.
/// </summary>
/// <remarks>
/// Settings keys such as AVVideoCodecKey are NSString constants exported by the frameworks; they're read
/// from there (see <see cref="Constant"/>) rather than spelled out, as they're not promised to equal their names.
/// </remarks>
internal static unsafe partial class AVFoundation
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Framework = "/System/Library/Frameworks/AVFoundation.framework/AVFoundation";

    // AVAssetWriterStatus.
    public const long Writing = 1;
    public const long Completed = 2;

    // Where each settings key and value may live: AVFAudio has the sound settings since macOS 11.
    private static readonly string[] ConstantFrameworks =
    {
        Framework,
        "/System/Library/Frameworks/AVFAudio.framework/AVFAudio",
        "/System/Library/Frameworks/CoreVideo.framework/CoreVideo",
        "/System/Library/Frameworks/VideoToolbox.framework/VideoToolbox",
    };

    private static readonly ConcurrentDictionary<string, IntPtr> Constants = new();
    private static readonly Lazy<IntPtr> DoNothing = new(MakeDoNothingBlock);

    static AVFoundation()
    {
        // The framework has to be loaded before its classes can be looked up.
        NativeLibrary.TryLoad(Framework, out _);
    }

    /// <summary>One of the frameworks' NSString constants (AVVideoCodecKey, kCVPixelBufferWidthKey...), by its name.</summary>
    public static IntPtr Constant(string name) => Constants.GetOrAdd(name, static key =>
    {
        foreach (var path in ConstantFrameworks)
        {
            if (NativeLibrary.TryLoad(path, out var library) && NativeLibrary.TryGetExport(library, key, out var address))
            {
                return Marshal.ReadIntPtr(address);
            }
        }

        throw new EntryPointNotFoundException($"This version of macOS has no {key}.");
    });

    // ---- Foundation objects for settings (all autoreleased) ----

    public static IntPtr Number(long value) => IntPtr_objc_msgSend_Long(ObjC.Class("NSNumber"), ObjC.Selector("numberWithLongLong:"), value);

    public static IntPtr Number(double value) => IntPtr_objc_msgSend_Double(ObjC.Class("NSNumber"), ObjC.Selector("numberWithDouble:"), value);

    public static IntPtr Number(bool value) =>
        IntPtr_objc_msgSend_Byte(ObjC.Class("NSNumber"), ObjC.Selector("numberWithBool:"), value ? (byte)1 : (byte)0);

    public static IntPtr Dictionary(params (IntPtr Key, IntPtr Value)[] entries)
    {
        var dictionary = ObjC.Send(ObjC.Class("NSMutableDictionary"), "dictionary");
        foreach (var (key, value) in entries)
        {
            AddEntry(dictionary, key, value);
        }

        return dictionary;
    }

    /// <summary>Adds to (or replaces in) a dictionary made by <see cref="Dictionary"/>.</summary>
    public static void AddEntry(IntPtr dictionary, IntPtr key, IntPtr value) =>
        void_objc_msgSend_IntPtr_IntPtr(dictionary, ObjC.Selector("setObject:forKey:"), value, key);

    public static IntPtr FileUrl(string path) =>
        ObjC.Send(ObjC.Class("NSURL"), "fileURLWithPath:", StringHelper.Create(path).NativePtr);

    /// <summary>An NSError's description, for the user, or null for none.</summary>
    public static string? Describe(IntPtr error) =>
        error == IntPtr.Zero ? null : StringHelper.Read(new NSError(error).LocalizedDescription);

    // ---- AVAssetWriter ----

    /// <summary>A new (retained) AVAssetWriter making an MP4 file at <paramref name="path"/>, or zero with the reason why not.</summary>
    public static IntPtr NewMp4Writer(string path, out string? problem)
    {
        var error = IntPtr.Zero;
        var writer = IntPtr_objc_msgSend_IntPtr_IntPtr_Error(ObjC.Send(ObjC.Class("AVAssetWriter"), "alloc"),
            ObjC.Selector("initWithURL:fileType:error:"), FileUrl(path), Constant("AVFileTypeMPEG4"), &error);
        problem = writer == IntPtr.Zero ? Describe(error) ?? "macOS gave no reason." : null;
        return writer;
    }

    /// <summary>
    /// True if the writer can encode <paramref name="mediaType"/> (AVMediaTypeVideo or AVMediaTypeAudio) with
    /// these settings. Asked first, because making an input with settings it can't use throws an Objective-C
    /// exception, which would take the app down.
    /// </summary>
    public static bool CanApply(IntPtr writer, IntPtr settings, IntPtr mediaType) =>
        byte_objc_msgSend_IntPtr_IntPtr(writer, ObjC.Selector("canApplyOutputSettings:forMediaType:"), settings, mediaType) != 0;

    /// <summary>A new (retained) AVAssetWriterInput for <paramref name="mediaType"/>, encoding with <paramref name="settings"/>.</summary>
    public static IntPtr NewInput(IntPtr mediaType, IntPtr settings) =>
        ObjC.Send(IntPtr_objc_msgSend_IntPtr_IntPtr(ObjC.Class("AVAssetWriterInput"),
            ObjC.Selector("assetWriterInputWithMediaType:outputSettings:"), mediaType, settings), "retain");

    /// <summary>A new (retained) pixel buffer adaptor for a video input, whose pool hands out pixel buffers with <paramref name="attributes"/>.</summary>
    public static IntPtr NewPixelBufferAdaptor(IntPtr input, IntPtr attributes) =>
        ObjC.Send(IntPtr_objc_msgSend_IntPtr_IntPtr(ObjC.Class("AVAssetWriterInputPixelBufferAdaptor"),
            ObjC.Selector("assetWriterInputPixelBufferAdaptorWithAssetWriterInput:sourcePixelBufferAttributes:"), input, attributes), "retain");

    public static void StartSession(IntPtr writer, CMTime time) =>
        void_objc_msgSend_CMTime(writer, ObjC.Selector("startSessionAtSourceTime:"), time);

    public static void EndSession(IntPtr writer, CMTime time) =>
        void_objc_msgSend_CMTime(writer, ObjC.Selector("endSessionAtSourceTime:"), time);

    public static bool AppendPixelBuffer(IntPtr adaptor, IntPtr pixelBuffer, CMTime time) =>
        byte_objc_msgSend_IntPtr_CMTime(adaptor, ObjC.Selector("appendPixelBuffer:withPresentationTime:"), pixelBuffer, time) != 0;

    public static long Status(IntPtr writer) => (long)ObjC.Send(writer, "status");

    /// <summary>
    /// Asks the writer to finish the file. It finishes in the background; <see cref="Status"/> says when it's done.
    /// The completion handler it needs is a block that does nothing, so it can be one made once and kept forever:
    /// nothing has to be kept alive for a callback, and the handler can never run after its memory has gone.
    /// </summary>
    public static void FinishWriting(IntPtr writer) =>
        ObjC.SendVoid(writer, "finishWritingWithCompletionHandler:", DoNothing.Value);

    // ---- Reading a video back (for the self-test) ----

    /// <summary>An (autoreleased) AVURLAsset for the file at <paramref name="path"/>.</summary>
    public static IntPtr OpenAsset(string path) =>
        IntPtr_objc_msgSend_IntPtr_IntPtr(ObjC.Class("AVURLAsset"), ObjC.Selector("URLAssetWithURL:options:"), FileUrl(path), IntPtr.Zero);

    /// <summary>The asset's tracks of a kind (AVMediaTypeVideo or AVMediaTypeAudio): how many, and the first or zero.</summary>
    public static (int Count, IntPtr First) Tracks(IntPtr asset, string mediaType)
    {
        var tracks = ObjC.Send(asset, "tracksWithMediaType:", Constant(mediaType));
        return tracks == IntPtr.Zero ? (0, IntPtr.Zero) : ((int)ObjC.Send(tracks, "count"), ObjC.Send(tracks, "firstObject"));
    }

    /// <summary>An asset's length, in seconds. Read through key-value coding, so it needs no struct-returning call.</summary>
    public static double AssetDuration(IntPtr asset) => ObjC.ReadValue<CMTime>(asset, "duration").Seconds;

    /// <summary>A track's length, in seconds, from its time range (a track has no duration of its own to ask for).</summary>
    public static double TrackDuration(IntPtr track) => ObjC.ReadValue<CMTimeRange>(track, "timeRange").Duration.Seconds;

    /// <summary>A numeric property (a float, say) read through key-value coding, or NaN.</summary>
    public static double Number(IntPtr receiver, string key)
    {
        var value = ObjC.Send(receiver, "valueForKey:", StringHelper.Create(key).NativePtr);
        return value == IntPtr.Zero ? double.NaN : ObjC.SendDouble(value, "doubleValue");
    }

    /// <summary>The video's picture at <paramref name="time"/>, as a CGImage to release with CGImageRelease, or zero with the reason why not.</summary>
    public static IntPtr CopyPicture(IntPtr asset, CMTime time, out string? problem)
    {
        var generator = ObjC.Send(ObjC.Class("AVAssetImageGenerator"), "assetImageGeneratorWithAsset:", asset);
        var actual = default(CMTime);
        var error = IntPtr.Zero;
        var image = IntPtr_objc_msgSend_CMTime_Time_Error(generator, ObjC.Selector("copyCGImageAtTime:actualTime:error:"), time, &actual, &error);
        problem = image == IntPtr.Zero ? Describe(error) ?? "macOS gave no reason." : null;
        return image;
    }

    // ---- Blocks ----

    // The start of every Objective-C block, and its descriptor (see Clang's "Block Implementation Specification").
    [StructLayout(LayoutKind.Sequential)]
    private struct BlockLiteral
    {
        public IntPtr Isa;
        public int Flags;
        public int Reserved;
        public IntPtr Invoke;
        public IntPtr Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlockDescriptor
    {
        public nuint Reserved;
        public nuint Size;
    }

    // BLOCK_IS_GLOBAL: copying or releasing the block does nothing, as for a block written at file scope in C.
    private const int BlockIsGlobal = 1 << 28;

    /// <summary>A global block of type void (^)(void) that does nothing, never freed.</summary>
    private static IntPtr MakeDoNothingBlock()
    {
        var system = NativeLibrary.Load("/usr/lib/libSystem.B.dylib");
        if (!NativeLibrary.TryGetExport(system, "_NSConcreteGlobalBlock", out var isa))
        {
            isa = NativeLibrary.GetExport(NativeLibrary.Load("/usr/lib/system/libsystem_blocks.dylib"), "_NSConcreteGlobalBlock");
        }

        var descriptor = (BlockDescriptor*)NativeMemory.AllocZeroed((nuint)sizeof(BlockDescriptor));
        descriptor->Size = (nuint)sizeof(BlockLiteral);
        var block = (BlockLiteral*)NativeMemory.AllocZeroed((nuint)sizeof(BlockLiteral));
        block->Isa = isa;
        block->Flags = BlockIsGlobal;
        block->Invoke = (IntPtr)(delegate* unmanaged<IntPtr, void>)&InvokeDoNothing;
        block->Descriptor = (IntPtr)descriptor;
        return (IntPtr)block;
    }

    [UnmanagedCallersOnly]
    private static void InvokeDoNothing(IntPtr block)
    {
    }

    // ---- objc_msgSend, one declaration per argument list ----

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_Long(IntPtr receiver, IntPtr selector, long argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_Double(IntPtr receiver, IntPtr selector, double argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_Byte(IntPtr receiver, IntPtr selector, byte argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_IntPtr_IntPtr_Error(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second,
        IntPtr* error);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte byte_objc_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_objc_msgSend_CMTime(IntPtr receiver, IntPtr selector, CMTime time);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial byte byte_objc_msgSend_IntPtr_CMTime(IntPtr receiver, IntPtr selector, IntPtr argument, CMTime time);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_objc_msgSend_CMTime_Time_Error(IntPtr receiver, IntPtr selector, CMTime time, CMTime* actualTime,
        IntPtr* error);
}
