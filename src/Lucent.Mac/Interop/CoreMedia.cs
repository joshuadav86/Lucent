using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// Core Media's times and sample buffers, and Core Video's pixel buffers: what AVFoundation's
/// AVAssetWriter takes pictures and sound in (see Export/Mp4Writer.cs).
/// </summary>
internal static unsafe partial class CoreMedia
{
    private const string Framework = "/System/Library/Frameworks/CoreMedia.framework/CoreMedia";
    private const string CoreVideo = "/System/Library/Frameworks/CoreVideo.framework/CoreVideo";

    /// <summary>kCMBlockBufferAssureMemoryNowFlag: allocate a block buffer's memory straight away.</summary>
    public const uint AssureMemoryNow = 1;

    /// <summary>
    /// A moment or a length of time, as a whole number of steps (<see cref="Timescale"/> a second). It's
    /// 24 bytes, so it's passed to C functions and Objective-C methods by value as a struct, exactly as
    /// declared: .NET follows the platform's rules for that on both arm64 (a pointer to a copy) and x64
    /// (on the stack).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CMTime
    {
        private const uint Valid = 1; // kCMTimeFlags_Valid

        public long Value;
        public int Timescale;
        public uint Flags;
        public long Epoch;

        /// <summary><paramref name="value"/> steps of 1/<paramref name="timescale"/> of a second.</summary>
        public static CMTime Make(long value, int timescale) => new() { Value = value, Timescale = timescale, Flags = Valid };

        /// <summary>In seconds, or NaN if it isn't a valid time.</summary>
        public readonly double Seconds => (Flags & Valid) != 0 && Timescale > 0 ? (double)Value / Timescale : double.NaN;
    }

    /// <summary>A stretch of time: where it starts and how long it lasts.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CMTimeRange
    {
        public CMTime Start;
        public CMTime Duration;
    }

    [LibraryImport(Framework)]
    public static partial int CMAudioFormatDescriptionCreate(IntPtr allocator, CoreAudio.AudioStreamBasicDescription* format,
        nuint layoutSize, IntPtr layout, nuint magicCookieSize, IntPtr magicCookie, IntPtr extensions, out IntPtr description);

    [LibraryImport(Framework)]
    public static partial int CMBlockBufferCreateWithMemoryBlock(IntPtr structureAllocator, IntPtr memoryBlock, nuint blockLength,
        IntPtr blockAllocator, IntPtr customBlockSource, nuint offsetToData, nuint dataLength, uint flags, out IntPtr blockBuffer);

    [LibraryImport(Framework)]
    public static partial int CMBlockBufferReplaceDataBytes(void* source, IntPtr destination, nuint offsetIntoDestination, nuint dataLength);

    /// <summary>A sample buffer of sound already in <paramref name="dataBuffer"/>; plain PCM needs no packet descriptions.</summary>
    [LibraryImport(Framework)]
    public static partial int CMAudioSampleBufferCreateReadyWithPacketDescriptions(IntPtr allocator, IntPtr dataBuffer,
        IntPtr formatDescription, nint sampleCount, CMTime presentationTime, IntPtr packetDescriptions, out IntPtr sampleBuffer);

    [LibraryImport(CoreVideo)]
    public static partial int CVPixelBufferPoolCreatePixelBuffer(IntPtr allocator, IntPtr pool, out IntPtr pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial int CVPixelBufferLockBaseAddress(IntPtr pixelBuffer, ulong lockFlags);

    [LibraryImport(CoreVideo)]
    public static partial int CVPixelBufferUnlockBaseAddress(IntPtr pixelBuffer, ulong unlockFlags);

    [LibraryImport(CoreVideo)]
    public static partial IntPtr CVPixelBufferGetBaseAddress(IntPtr pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial nuint CVPixelBufferGetBytesPerRow(IntPtr pixelBuffer);

    [LibraryImport(CoreVideo)]
    public static partial void CVPixelBufferRelease(IntPtr pixelBuffer);
}
