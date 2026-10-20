using System.Runtime.InteropServices;

namespace Lucent.Mac.Interop;

/// <summary>
/// The parts of Core Audio's AudioToolbox (and Core Foundation) that decode files, play and record sound,
/// and of the Core Audio framework itself that list the outputs and inputs and set them up.
/// </summary>
internal static unsafe partial class CoreAudio
{
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";
    private const string CoreAudioFramework = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    public const uint LinearPcm = 0x6C70636D;              // 'lpcm'
    public const uint FloatPackedFlags = 0x1 | 0x8;         // kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked
    public const uint FileDataFormatProperty = 0x66666D74;  // 'ffmt'
    public const uint ClientDataFormatProperty = 0x63666D74; // 'cfmt'
    public const uint FileLengthFramesProperty = 0x2366726D; // '#frm'
    public const uint QueueCurrentDeviceProperty = 0x61716364; // 'aqcd': the UID of the device a queue plays to

    // The objects and properties that describe the outputs.
    public const uint SystemObject = 1;                      // kAudioObjectSystemObject
    public const uint ScopeGlobal = 0x676C6F62;              // 'glob'
    public const uint ScopeOutput = 0x6F757470;              // 'outp'
    public const uint ScopeInput = 0x696E7074;               // 'inpt'
    public const uint ElementMain = 0;                       // kAudioObjectPropertyElementMain
    public const uint DevicesProperty = 0x64657623;          // 'dev#': every device
    public const uint DefaultOutputDeviceProperty = 0x644F7574; // 'dOut'
    public const uint DefaultInputDeviceProperty = 0x64496E20;  // 'dIn '
    public const uint StreamConfigurationProperty = 0x736C6179; // 'slay': a device's channels, as an AudioBufferList
    public const uint NameProperty = 0x6C6E616D;             // 'lnam'
    public const uint DeviceUidProperty = 0x75696420;        // 'uid '
    public const uint StreamsProperty = 0x73746D23;          // 'stm#'
    public const uint HogModeProperty = 0x6F696E6B;          // 'oink': the process that has the device to itself, or -1
    public const uint NominalSampleRateProperty = 0x6E737274; // 'nsrt'
    public const uint AvailableSampleRatesProperty = 0x6E737223; // 'nsr#'

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

    /// <summary>Which property of a Core Audio object to read or set.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioObjectPropertyAddress
    {
        public uint Selector;
        public uint Scope;
        public uint Element;

        public AudioObjectPropertyAddress(uint selector, uint scope = ScopeGlobal)
        {
            Selector = selector;
            Scope = scope;
            Element = ElementMain;
        }
    }

    /// <summary>Sample rates a device runs at: often a single rate, with both ends the same.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AudioValueRange
    {
        public double Minimum;
        public double Maximum;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CFRange
    {
        public nint Location;
        public nint Length;
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

    [LibraryImport(CoreFoundation)]
    public static partial nint CFStringGetLength(IntPtr text);

    [LibraryImport(CoreFoundation)]
    public static partial void CFStringGetCharacters(IntPtr text, CFRange range, ushort* buffer);

    [LibraryImport(CoreFoundation)]
    public static partial IntPtr CFStringCreateWithCharacters(IntPtr allocator, ushort* characters, nint length);

    /// <summary>The text of a CFString, or null for none.</summary>
    public static string? ReadString(IntPtr text)
    {
        if (text == IntPtr.Zero)
        {
            return null;
        }

        var characters = new char[CFStringGetLength(text)];
        fixed (char* buffer = characters)
        {
            CFStringGetCharacters(text, new CFRange { Location = 0, Length = characters.Length }, (ushort*)buffer);
        }

        return new string(characters);
    }

    /// <summary>A new CFString with the same text, for the caller to CFRelease.</summary>
    public static IntPtr CreateString(string text)
    {
        fixed (char* characters = text)
        {
            return CFStringCreateWithCharacters(IntPtr.Zero, (ushort*)characters, text.Length);
        }
    }

    // ---- Audio objects: the outputs, their names and sample rates, and hog mode ----

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioObjectGetPropertyDataSize(
        uint objectId, AudioObjectPropertyAddress* address, uint qualifierSize, void* qualifier, out uint size);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioObjectGetPropertyData(
        uint objectId, AudioObjectPropertyAddress* address, uint qualifierSize, void* qualifier, ref uint size, void* data);

    [LibraryImport(CoreAudioFramework)]
    public static partial int AudioObjectSetPropertyData(
        uint objectId, AudioObjectPropertyAddress* address, uint qualifierSize, void* qualifier, uint size, void* data);

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

    /// <summary>Records into buffers, calling back with each one as it fills.</summary>
    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueNewInput(
        AudioStreamBasicDescription* format,
        delegate* unmanaged<IntPtr, IntPtr, AudioQueueBuffer*, IntPtr, uint, IntPtr, void> callback,
        IntPtr userData, IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr queue);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueAllocateBuffer(IntPtr queue, uint byteSize, out AudioQueueBuffer* buffer);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueEnqueueBuffer(IntPtr queue, AudioQueueBuffer* buffer, uint packetDescriptions, IntPtr descriptions);

    [LibraryImport(AudioToolbox)]
    public static partial int AudioQueueSetProperty(IntPtr queue, uint propertyId, void* data, uint size);

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
