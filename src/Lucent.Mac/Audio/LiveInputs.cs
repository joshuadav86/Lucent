using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using Lucent.Audio;
using Lucent.Recording;
using Lucent.Settings;
using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>An audio interface, mixer, microphone or other input that can be recorded from.</summary>
/// <param name="Id">The device's UID, which stays the same from one start to the next.</param>
public sealed record RecordingInput(string Id, string Name)
{
    /// <summary>Core Audio's number for the device while it's connected.</summary>
    internal uint ObjectId { get; init; }
}

/// <summary>What an input can do, for the Record window's choices.</summary>
/// <param name="Channels">How many input channels it has.</param>
/// <param name="SampleRates">The rates it records at itself. Others are offered too; macOS converts to them.</param>
/// <param name="NativeRate">The one rate it runs at, when it can't be changed from Lucent, or null.</param>
public sealed record InputDetails(int Channels, IReadOnlyList<int> SampleRates, int? NativeRate);

/// <summary>
/// The inputs Lucent can record from: every Core Audio device with input channels (audio interfaces, DJ
/// mixers, microphones, aggregate devices). Core Audio already talks to interfaces directly with a low
/// delay, so there's no need for ASIO, which Windows uses for that (the Windows app's Audio/LiveInputs.cs).
/// </summary>
public static unsafe class LiveInputs
{
    /// <summary>Every input that's connected, by name.</summary>
    public static IReadOnlyList<RecordingInput> List()
    {
        try
        {
            var address = new AudioObjectPropertyAddress(DevicesProperty);
            Check(AudioObjectGetPropertyDataSize(SystemObject, &address, 0, null, out var size), "list the sound devices");
            var ids = new uint[size / sizeof(uint)];
            if (ids.Length > 0)
            {
                fixed (uint* data = ids)
                {
                    Check(AudioObjectGetPropertyData(SystemObject, &address, 0, null, ref size, data), "list the sound devices");
                }
            }

            return ids.Take((int)(size / sizeof(uint)))
                .Where(device => InputChannels(device) > 0)
                .Select(Describe)
                .OfType<RecordingInput>()
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Audio inputs", ex);
            return Array.Empty<RecordingInput>();
        }
    }

    /// <summary>The inputs the visuals can follow: every input, as for recording (Core Audio lets several apps share one).</summary>
    public static IReadOnlyList<RecordingInput> ListForVisuals() => List();

    /// <summary>The input with this id, or macOS's default input when it's gone or none was chosen.</summary>
    public static RecordingInput? Find(string? id, IReadOnlyList<RecordingInput> inputs)
    {
        if (id is not null && inputs.FirstOrDefault(input => input.Id == id) is { } chosen)
        {
            return chosen;
        }

        var address = new AudioObjectPropertyAddress(DefaultInputDeviceProperty);
        uint device = 0;
        var size = (uint)sizeof(uint);
        if (AudioObjectGetPropertyData(SystemObject, &address, 0, null, ref size, &device) == 0 && device != 0)
        {
            var found = device;
            return inputs.FirstOrDefault(input => input.ObjectId == found) ?? inputs.FirstOrDefault();
        }

        return inputs.FirstOrDefault();
    }

    /// <summary>Asks the input how many channels it has and which rates it runs at.</summary>
    public static InputDetails Describe(RecordingInput input)
    {
        var ranges = ExclusiveDevice.AvailableRates(input.ObjectId);
        var rates = RecordingSettings.SampleRates
            .Where(rate => ranges.Any(range => rate >= range.Minimum - 1 && rate <= range.Maximum + 1))
            .ToList();
        var now = (int)Math.Round(ExclusiveDevice.ReadRate(input.ObjectId));
        if (rates.Count == 0 && now > 0)
        {
            rates.Add(now);
        }

        return new InputDetails(Math.Max(1, InputChannels(input.ObjectId)), rates, rates.Count <= 1 && now > 0 ? now : null);
    }

    /// <summary>
    /// False when the user has said no to Lucent using inputs (System Settings, Privacy &amp; Security,
    /// Microphone). Until they've been asked, it's true: macOS asks the first time an input is opened.
    /// </summary>
    public static bool IsAllowed()
    {
        try
        {
            var audio = AVFoundation.Constant("AVMediaTypeAudio");
            var status = (long)ObjC.Send(ObjC.Class("AVCaptureDevice"), "authorizationStatusForMediaType:", audio);
            // AVAuthorizationStatusRestricted (1) or Denied (2).
            return status is not (1 or 2);
        }
        catch (Exception)
        {
            return true;
        }
    }

    // How many input channels a device has, from its input streams' layout.
    internal static int InputChannels(uint device)
    {
        var address = new AudioObjectPropertyAddress(StreamConfigurationProperty, ScopeInput);
        if (AudioObjectGetPropertyDataSize(device, &address, 0, null, out var size) != 0 || size < 8)
        {
            return 0;
        }

        var memory = Marshal.AllocHGlobal((int)size);
        try
        {
            if (AudioObjectGetPropertyData(device, &address, 0, null, ref size, (void*)memory) != 0)
            {
                return 0;
            }

            // An AudioBufferList: a count, then that many AudioBuffers (channels, size, pointer), 8-byte aligned.
            var buffers = (uint)Marshal.ReadInt32(memory);
            var channels = 0;
            for (var i = 0; i < buffers && 8 + (i + 1) * sizeof(CoreAudio.AudioBuffer) <= size; i++)
            {
                channels += Marshal.ReadInt32(memory, 8 + i * sizeof(CoreAudio.AudioBuffer));
            }

            return channels;
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
        }
    }

    private static RecordingInput? Describe(uint device)
    {
        var id = ReadText(device, DeviceUidProperty);
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return new RecordingInput(id, ReadText(device, NameProperty) is { Length: > 0 } name ? name : id) { ObjectId = device };
    }

    private static string? ReadText(uint device, uint property)
    {
        var address = new AudioObjectPropertyAddress(property);
        var text = IntPtr.Zero;
        var size = (uint)sizeof(IntPtr);
        if (AudioObjectGetPropertyData(device, &address, 0, null, ref size, &text) != 0 || text == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ReadString(text);
        }
        finally
        {
            CFRelease(text);
        }
    }
}

/// <summary>
/// One input being listened to, handing its sound to a <see cref="LiveRecorder"/> (or, for the visuals, an
/// <see cref="InputFeed"/>), through an Audio Queue (the recording side of what AudioQueueOutput plays with).
/// For a recording, the device is switched to the recording's sample rate when it can run at it, and back
/// afterwards; otherwise macOS converts the rate.
/// </summary>
public sealed unsafe class LiveInput : IDisposable
{
    private const int BufferCount = 4;
    private const int BufferMs = 20;

    private readonly ILiveInputSink _recorder;
    private readonly int _firstChannel;
    private readonly object _lock = new();
    private GCHandle _self;
    private IntPtr _queue;
    private int _channels;
    private uint _device;
    private double _previousRate;
    private float[] _samples = Array.Empty<float>();

    private LiveInput(ILiveInputSink recorder, int firstChannel)
    {
        _recorder = recorder;
        _firstChannel = firstChannel;
    }

    /// <summary>The rate the sound arrives at (the recording's: macOS converts it if the device runs at another).</summary>
    public int InputRate { get; private set; }

    /// <summary>Starts listening, with the recorder's channels starting at <paramref name="firstChannel"/>.</summary>
    public static LiveInput Start(RecordingInput input, ILiveInputSink recorder, int firstChannel)
    {
        var live = new LiveInput(recorder, firstChannel);
        try
        {
            live.Open(input);
        }
        catch
        {
            live.Dispose();
            throw;
        }

        return live;
    }

    private void Open(RecordingInput input)
    {
        // An interface that can run at the recording's rate is set to it, so nothing is converted.
        // It's put back as it was when the window closes. The visuals never change it: a DAW may be using it.
        var rate = _recorder.SampleRate;
        var was = ExclusiveDevice.ReadRate(input.ObjectId);
        if (_recorder is LiveRecorder && Math.Abs(was - rate) > 1
            && ExclusiveDevice.AvailableRates(input.ObjectId).Any(range => rate >= range.Minimum - 1 && rate <= range.Maximum + 1)
            && ExclusiveDevice.WriteRate(input.ObjectId, rate))
        {
            _device = input.ObjectId;
            _previousRate = was;
        }

        InputRate = rate;
        _self = GCHandle.Alloc(this);
        // Every channel the device has, so any pair can be chosen; two if the queue won't take that many.
        var deviceChannels = Math.Clamp(LiveInputs.InputChannels(input.ObjectId), 1, 64);
        foreach (var channels in new[] { deviceChannels, Math.Min(2, deviceChannels) }.Distinct())
        {
            var format = AudioStreamBasicDescription.Float(rate, channels);
            if (AudioQueueNewInput(&format, &OnBufferFull, GCHandle.ToIntPtr(_self), IntPtr.Zero, IntPtr.Zero, 0, out _queue) == 0)
            {
                _channels = channels;
                break;
            }

            _queue = IntPtr.Zero;
        }

        if (_queue == IntPtr.Zero)
        {
            throw new IOException($"Couldn't open {input.Name} for recording.");
        }

        var uid = CreateString(input.Id);
        try
        {
            Check(AudioQueueSetProperty(_queue, QueueCurrentDeviceProperty, &uid, (uint)sizeof(IntPtr)), $"record from {input.Name}");
        }
        finally
        {
            CFRelease(uid);
        }

        var bytes = (uint)(rate * BufferMs / 1000 * _channels * sizeof(float));
        for (var i = 0; i < BufferCount; i++)
        {
            Check(AudioQueueAllocateBuffer(_queue, bytes, out var buffer), "make a recording buffer");
            Check(AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero), "start recording");
        }

        Check(AudioQueueStart(_queue, IntPtr.Zero), $"start recording from {input.Name}");
    }

    [UnmanagedCallersOnly]
    private static void OnBufferFull(IntPtr userData, IntPtr queue, AudioQueueBuffer* buffer, IntPtr startTime, uint packets, IntPtr descriptions)
    {
        if (GCHandle.FromIntPtr(userData).Target is not LiveInput input)
        {
            return;
        }

        lock (input._lock)
        {
            if (input._queue != queue)
            {
                return;
            }

            var count = (int)(buffer->AudioDataByteSize / sizeof(float));
            if (count > 0)
            {
                if (input._samples.Length < count)
                {
                    input._samples = new float[count];
                }

                new ReadOnlySpan<float>(buffer->AudioData, count).CopyTo(input._samples);
                try
                {
                    input._recorder.Write(input._samples.AsSpan(0, count), count / input._channels, input._channels, input._firstChannel, input.InputRate);
                }
                catch (Exception)
                {
                    // Never let an exception reach Core Audio's thread.
                }
            }

            AudioQueueEnqueueBuffer(queue, buffer, 0, IntPtr.Zero);
        }
    }

    public void Dispose()
    {
        IntPtr queue;
        lock (_lock)
        {
            queue = _queue;
            _queue = IntPtr.Zero;
        }

        if (queue != IntPtr.Zero)
        {
            AudioQueueStop(queue, 1);
            AudioQueueDispose(queue, 1);
        }

        if (_self.IsAllocated)
        {
            _self.Free();
        }

        if (_device != 0 && _previousRate > 0)
        {
            ExclusiveDevice.WriteRate(_device, _previousRate);
            _device = 0;
        }
    }
}
