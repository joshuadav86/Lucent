using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using WinampInspiredVisualizer.Audio;
using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Listens to everything the Mac is playing (Spotify, Apple Music, a browser, a game) through a Core Audio
/// process tap, and feeds it to an <see cref="AudioCapture"/>, so the visuals, meter, equalizer's spectrum
/// and analyzer react to any app. It only listens: the sound itself is untouched. Lucent's own music is
/// part of what the Mac plays, so it still shows too.
/// </summary>
/// <remarks>
/// macOS asks once whether Lucent may record the Mac's sound ("System Audio Recording Only" in Privacy &amp;
/// Security). Until it's allowed the tap hears silence. Windows does the same with WASAPI loopback (see
/// Windows' Audio/SystemAudio.cs). Taps need macOS 14.2 or later.
/// </remarks>
public sealed unsafe class SystemAudio : IDisposable
{
    private readonly AudioCapture _capture;
    private GCHandle _self;
    private uint _tap;
    private uint _device;
    private IntPtr _procId;
    private IntPtr _description;
    private bool _listening;
    private int _channels = 2;
    private bool _interleaved = true;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    private SystemAudio(AudioCapture capture) => _capture = capture;

    /// <summary>
    /// Raised (on a background thread) when the Mac switches to another output (headphones plugged in, say),
    /// since the tap is clocked by the one it started on. Start again to follow it.
    /// </summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>True on a macOS that can listen to other apps (14.2 or later).</summary>
    public static bool IsSupported => AudioTap.IsSupported;

    /// <summary>Starts listening. Throws when macOS won't. Dispose to go back to the player's own sound.</summary>
    public static SystemAudio Start(AudioCapture capture)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Listening to other apps needs macOS 14.2 or later.");
        }

        var listener = new SystemAudio(capture);
        try
        {
            listener.Open();
        }
        catch
        {
            listener.Dispose();
            throw;
        }

        return listener;
    }

    /// <summary>The name of the output being listened to, for the menu's tooltip.</summary>
    public static string? DefaultOutputName() => OutputDevices.Open(null)?.Name;

    private void Open()
    {
        using var pool = AutoreleasePool.Begin();
        var output = OutputDevices.Open(null) ?? throw new IOException("The Mac has no sound output to listen to.");

        var (description, uuid) = AudioTap.DescribeGlobalTap("Lucent: every app's sound");
        _description = description;
        Check(AudioTap.AudioHardwareCreateProcessTap(description, out _tap), "listen to the Mac's sound");

        var format = default(AudioStreamBasicDescription);
        var address = new AudioObjectPropertyAddress(AudioTap.TapFormatProperty);
        var size = (uint)sizeof(AudioStreamBasicDescription);
        Check(AudioObjectGetPropertyData(_tap, &address, 0, null, ref size, &format), "read the format of the Mac's sound");
        if (format.FormatId != LinearPcm || (format.FormatFlags & 0x1) == 0 || format.BitsPerChannel != 32)
        {
            throw new IOException("The Mac's sound is in a format Lucent can't listen to.");
        }

        _channels = (int)Math.Max(1, format.ChannelsPerFrame);
        _interleaved = (format.FormatFlags & AudioTap.NonInterleavedFlag) == 0;

        var aggregate = AudioTap.DescribeAggregate($"net.lucentaudio.system-audio.{Guid.NewGuid():N}", "Lucent: every app's sound",
            output.Id, uuid);
        Check(AudioTap.AudioHardwareCreateAggregateDevice(aggregate, out _device), "listen to the Mac's sound");

        _self = GCHandle.Alloc(this);
        Check(AudioTap.AudioDeviceCreateIOProcID(_device, &OnInput, GCHandle.ToIntPtr(_self), out _procId), "listen to the Mac's sound");
        _capture.BeginExternal((int)Math.Round(format.SampleRate));
        Check(AudioTap.AudioDeviceStart(_device, _procId), "start listening to the Mac's sound");

        var defaultOutput = new AudioObjectPropertyAddress(DefaultOutputDeviceProperty);
        _listening = AudioTap.AudioObjectAddPropertyListener(SystemObject, &defaultOutput, &OnOutputChanged, GCHandle.ToIntPtr(_self)) == 0;
    }

    [UnmanagedCallersOnly]
    private static int OnOutputChanged(uint objectId, uint count, AudioObjectPropertyAddress* addresses, IntPtr clientData)
    {
        try
        {
            if (GCHandle.FromIntPtr(clientData).Target is SystemAudio listener)
            {
                listener.Stopped?.Invoke(listener, null);
            }
        }
        catch (Exception)
        {
            // Never let an exception out into Core Audio's thread.
        }

        return 0;
    }

    // Core Audio's IO thread, with each slice of what the tap heard.
    [UnmanagedCallersOnly]
    private static int OnInput(uint device, IntPtr now, AudioBufferList* input, IntPtr inputTime, AudioBufferList* output, IntPtr outputTime,
        IntPtr clientData)
    {
        try
        {
            if (input is not null && GCHandle.FromIntPtr(clientData).Target is SystemAudio listener)
            {
                listener.Take(input);
            }
        }
        catch (Exception)
        {
            // Never let an exception out into Core Audio's thread; the next slice tries again.
        }

        return 0;
    }

    private void Take(AudioBufferList* input)
    {
        if (input->NumberBuffers == 0)
        {
            return;
        }

        var buffers = &input->Buffer;
        var first = buffers[0];
        int frames;
        if (_interleaved)
        {
            var channels = (int)Math.Max(1, first.NumberChannels);
            frames = (int)(first.DataByteSize / sizeof(float)) / channels;
            Grow(frames);
            var samples = (float*)first.Data;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += samples[frame * channels + channel];
                }

                _mono[frame] = sum / channels;
                _left[frame] = samples[frame * channels];
                _right[frame] = samples[frame * channels + Math.Min(1, channels - 1)];
            }
        }
        else
        {
            // One buffer per channel.
            var channels = (int)Math.Min(input->NumberBuffers, (uint)_channels);
            frames = (int)(first.DataByteSize / sizeof(float));
            Grow(frames);
            var left = (float*)first.Data;
            var right = (float*)buffers[Math.Min(1, channels - 1)].Data;
            for (var frame = 0; frame < frames; frame++)
            {
                var sum = 0f;
                for (var channel = 0; channel < channels; channel++)
                {
                    sum += ((float*)buffers[channel].Data)[frame];
                }

                _mono[frame] = sum / channels;
                _left[frame] = left[frame];
                _right[frame] = right[frame];
            }
        }

        _capture.WriteExternal(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
    }

    private void Grow(int frames)
    {
        if (_mono.Length < frames)
        {
            _mono = new float[frames];
            _left = new float[frames];
            _right = new float[frames];
        }
    }

    public void Dispose()
    {
        if (_listening)
        {
            var defaultOutput = new AudioObjectPropertyAddress(DefaultOutputDeviceProperty);
            AudioTap.AudioObjectRemovePropertyListener(SystemObject, &defaultOutput, &OnOutputChanged, GCHandle.ToIntPtr(_self));
            _listening = false;
        }

        if (_device != 0)
        {
            if (_procId != IntPtr.Zero)
            {
                AudioTap.AudioDeviceStop(_device, _procId);
                AudioTap.AudioDeviceDestroyIOProcID(_device, _procId);
                _procId = IntPtr.Zero;
            }

            AudioTap.AudioHardwareDestroyAggregateDevice(_device);
            _device = 0;
        }

        if (_tap != 0)
        {
            AudioTap.AudioHardwareDestroyProcessTap(_tap);
            _tap = 0;
        }

        ObjC.Release(_description);
        _description = IntPtr.Zero;
        if (_self.IsAllocated)
        {
            _self.Free();
        }

        if (_capture.IsExternal)
        {
            _capture.EndExternal();
        }
    }
}
