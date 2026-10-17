using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>The parts of Core Audio's AudioToolbox (and Core Foundation) that decode files and play sound.</summary>
internal static unsafe partial class CoreAudio
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint LinearPcm = 0x6C70636D;              // 'lpcm'
    public const uint FloatPackedFlags = 0x1 | 0x8;         // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked
    public const uint FileDataFormatProperty = 0x66666D74;  // 'ffmt'
    public const uint ClientDataFormatProperty = 0x63666D74; // 'cfmt'
    public const uint FileLengthFramesProperty = 0x2366726D; // '#frm'

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double SampleRate;
        public uint FormatId;
        public uint FormatFlags;
        public uint BytesPerPacket;
        public uint FramesPerPacket;
        public uint BytesPerFrame;
        public uint ChannelsPerFrame;
        public uint BitsPerChannel;
        public uint Reserved;

        /// <summary>Interleaved 32-bit float, the format NAudio's float sample providers use.</summary>
        public static AudioStreamBasicDescription Float(double sampleRate, int channels) => new()
        {
            SampleRate = sampleRate,
            FormatId = LinearPcm,
            FormatFlags = FloatPackedFlags,
            BytesPerPacket = (uint)(4 * channels),
            FramesPerPacket = 1,
            BytesPerFrame = (uint)(4 * channels),
            ChannelsPerFrame = (uint)channels,
            BitsPerChannel = 32,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBuffer
    {
        public uint NumberChannels;
        public uint DataByteSize;
        public void* Data;
    }

    /// <summary>An AudioBufferList with room for one (interleaved) buffer.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioBufferList
    {
        public uint NumberBuffers;
        public AudioBuffer Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioQueueBuffer
    {
        public uint AudioDataBytesCapacity;
        public void* AudioData;
        public uint AudioDataByteSize;
        public void* UserData;
        public uint PacketDescriptionCapacity;
        public void* PacketDescriptions;
        public uint PacketDescriptionCount;
    }

    public static void Check(int status, string doing)
    {
        if (status != 0)
        {
            throw new IOException($"Couldn't {doing} (Core Audio error {FourCharacters(status)}).");
        }
    }

    /// <summary>Core Audio errors are often four letters packed into a number; shows them as letters when they are.</summary>
    private static string FourCharacters(int status)
    {
        var bytes = BitConverter.GetBytes(status);
        Array.Reverse(bytes);
        return bytes.All(b => b is >= 32 and < 127) ? $"'{System.Text.Encoding.ASCII.GetString(bytes)}'" : status.ToString();
    }

    // ---- Core Foundation ----

    [LibraryImport(CoreFoundation)]
    public static partial IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr allocator, byte* buffer, nint length, byte isDirectory);

    [LibraryImport(CoreFoundation)]
    public static partial void CFRelease(IntPtr item);

    // ---- Extended Audio File: decodes anything macOS can (MP3, AAC, ALAC, FLAC, WAV, AIFF...) ----

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileOpenURL(IntPtr url, out IntPtr file);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileDispose(IntPtr file);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileGetProperty(IntPtr file, uint propertyId, ref uint size, void* data);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileSetProperty(IntPtr file, uint propertyId, uint size, void* data);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileRead(IntPtr file, ref uint frames, AudioBufferList* data);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileSeek(IntPtr file, long frame);

    [LibraryImport(AudioToolbox)]
    public static partial int ExtAudioFileTell(IntPtr file, out long frame);

    // ---- Audio Queue: plays buffers that a callback fills ----

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueNewOutput(
        AudioStreamBasicDescription* format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, void> callback,
        IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueAllocateBuffer(IntPtr queue, uint byteSize, out AudioQueueBuffer* buffer);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueEnqueueBuffer(IntPtr queue, AudioQueueBuffer* buffer, uint packetDescriptions, IntPtr descriptions);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStart(IntPtr queue, IntPtr startTime);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueuePause(IntPtr queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueStop(IntPtr queue, byte immediate);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueFlush(IntPtr queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueDispose(IntPtr queue, byte immediate);
}
