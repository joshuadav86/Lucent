using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using Lucent.Audio;
using NAudio.Wave;
using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Listens to everything the Mac is playing (Spotify, Apple Music, a browser, a game) through a Core Audio
/// process tap, and feeds it to an <see cref="AudioCapture"/>, so the visuals, meter, equalizer's spectrum
/// and analyzer react to any app. It only listens: the sound itself is untouched. Lucent's own music is
/// part of what the Mac plays, so it still shows too. <see cref="StartForApp"/> listens to one app instead
/// (the Spotify or browser Lucent controls), with a tap of just that app's processes. Given an equalizer, it also
/// plays that app's sound through it: macOS quietens the app, and the equalized sound goes out in its place.
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

    // While the app plays through the equalizer: a copy of the player's equalizer kept in step with it (the player's
    // own stays plugged into the paused track), the tapped sound going into it, and its output.
    private Equalizer? _leader;
    private Equalizer? _shaping;
    private BlockSource? _block;
    private ISampleProvider? _equalized;
    private float[] _stereo = Array.Empty<float>();

    // The app's own sound is only quietened once the tap has heard it, so a tap that hears nothing (macOS not yet
    // allowed to record) can never leave the app silent. Until then, the app plays as usual and Lucent plays nothing.
    private volatile bool _muted;
    private int _muting;
    private readonly object _tapGate = new();

    private SystemAudio(AudioCapture capture) => _capture = capture;

    /// <summary>
    /// The equalizer shaping the app's sound, once the app is playing through it (null until then, and when there's
    /// no equalizer). For the equalizer window's headroom readout.
    /// </summary>
    public Equalizer? Shaping => _muted ? _shaping : null;

    /// <summary>
    /// Raised (on a background thread) when the Mac switches to another output (headphones plugged in, say),
    /// since the tap is clocked by the one it started on. Start again to follow it.
    /// </summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>True on a macOS that can listen to other apps (14.2 or later).</summary>
    public static bool IsSupported => AudioTap.IsSupported;

    /// <summary>Starts listening. Throws when macOS won't. Dispose to go back to the player's own sound.</summary>
    public static SystemAudio Start(AudioCapture capture) => Start(capture, null, null);

    /// <summary>
    /// Starts listening to just these processes (Core Audio's objects for them, from <see cref="FindApp"/>).
    /// Throws when macOS won't.
    /// </summary>
    public static SystemAudio StartForApp(AudioCapture capture, IReadOnlyList<uint> processes) => Start(capture, processes, null);

    /// <summary>
    /// Starts listening to just these processes and plays their sound through an equalizer that follows
    /// <paramref name="equalizer"/>'s settings, quietening the app's own sound once the tap hears it. Throws when macOS won't.
    /// </summary>
    public static SystemAudio StartForApp(AudioCapture capture, IReadOnlyList<uint> processes, Equalizer equalizer) =>
        Start(capture, processes, equalizer);

    /// <summary>
    /// Core Audio's objects for the app whose bundle IDs start with <paramref name="bundlePrefixes"/> (with its helper
    /// processes, which is where a browser's sound comes from), sorted. Empty when it isn't running or hasn't used sound yet.
    /// </summary>
    public static List<uint> FindApp(IReadOnlyList<string> bundlePrefixes) =>
        IsSupported ? AudioTap.ProcessObjects(bundlePrefixes) : new List<uint>();

    private static SystemAudio Start(AudioCapture capture, IReadOnlyList<uint>? processes, Equalizer? equalizer)
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("Listening to other apps needs macOS 14.2 or later.");
        }

        var listener = new SystemAudio(capture);
        try
        {
            listener.Open(processes, equalizer);
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

    private void Open(IReadOnlyList<uint>? processes, Equalizer? equalizer)
    {
        using var pool = AutoreleasePool.Begin();
        var output = OutputDevices.Open(null) ?? throw new IOException("The Mac has no sound output to listen to.");

        var name = processes is null ? "Lucent: every app's sound" : "Lucent: one app's sound";
        var (description, uuid) = processes is null ? AudioTap.DescribeGlobalTap(name) : AudioTap.DescribeAppTap(name, processes);
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
        if (equalizer is not null)
        {
            _leader = equalizer;
            _shaping = new Equalizer();
            _shaping.CopyFrom(equalizer);
            equalizer.Changed += OnLeaderChanged;
            _block = new BlockSource((int)Math.Round(format.SampleRate));
            _equalized = _shaping.Attach(_block);
        }

        var aggregate = AudioTap.DescribeAggregate($"net.lucentaudio.system-audio.{Guid.NewGuid():N}", name,
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
                listener.Take(input, output);
            }
        }
        catch (Exception)
        {
            // Never let an exception out into Core Audio's thread; the next slice tries again.
        }

        return 0;
    }

    private void Take(AudioBufferList* input, AudioBufferList* output)
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

        if (_equalized is not null)
        {
            Equalize(frames, output);
        }

        _capture.WriteExternal(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
    }

    // Core Audio's IO thread: plays the slice just heard through the equalizer, out of the device the tap is clocked
    // by (so the two never drift apart), and leaves the equalized sound in place for the visuals.
    private void Equalize(int frames, AudioBufferList* output)
    {
        if (!_muted)
        {
            Silence(output);
            if (Volatile.Read(ref _muting) == 0 && HeardSound(frames))
            {
                Interlocked.Exchange(ref _muting, 1);
                // Changing the tap blocks, which Core Audio's IO thread mustn't.
                ThreadPool.QueueUserWorkItem(_ => Mute());
            }

            return;
        }

        if (_stereo.Length < frames * 2)
        {
            _stereo = new float[frames * 2];
        }

        for (var frame = 0; frame < frames; frame++)
        {
            _stereo[frame * 2] = _left[frame];
            _stereo[frame * 2 + 1] = _right[frame];
        }

        _block!.Fill(_stereo, frames * 2);
        _equalized!.Read(_stereo, 0, frames * 2);
        for (var frame = 0; frame < frames; frame++)
        {
            _left[frame] = _stereo[frame * 2];
            _right[frame] = _stereo[frame * 2 + 1];
            _mono[frame] = (_left[frame] + _right[frame]) / 2;
        }

        Play(frames, output);
    }

    private bool HeardSound(int frames)
    {
        for (var frame = 0; frame < frames; frame++)
        {
            if (Math.Abs(_left[frame]) > 1e-4f || Math.Abs(_right[frame]) > 1e-4f)
            {
                return true;
            }
        }

        return false;
    }

    private void Mute()
    {
        try
        {
            lock (_tapGate)
            {
                if (_tap != 0)
                {
                    AudioTap.MuteWhileTapped(_tap, _description);
                    _muted = true;
                }
            }
        }
        catch (Exception ex)
        {
            // The app keeps playing as usual, unequalized.
            System.Diagnostics.Trace.WriteLine($"Couldn't play the app through the equalizer: {ex.Message}");
        }
    }

    // Left and right into the output's first two channels (both mixed into a lone one), and silence in any others.
    private void Play(int frames, AudioBufferList* output)
    {
        if (output is null)
        {
            return;
        }

        var buffers = &output->Buffer;
        var mono = output->NumberBuffers == 1 && buffers[0].NumberChannels == 1;
        var channel = 0;
        for (var index = 0; index < output->NumberBuffers; index++)
        {
            var buffer = buffers[index];
            var channels = (int)Math.Max(1, buffer.NumberChannels);
            var samples = (float*)buffer.Data;
            var room = (int)(buffer.DataByteSize / sizeof(float)) / channels;
            for (var frame = 0; frame < room; frame++)
            {
                for (var within = 0; within < channels; within++)
                {
                    var which = channel + within;
                    samples[frame * channels + within] = frame >= frames || which > 1 ? 0f
                        : mono ? _mono[frame]
                        : which == 0 ? _left[frame] : _right[frame];
                }
            }

            channel += channels;
        }
    }

    private static void Silence(AudioBufferList* output)
    {
        if (output is null)
        {
            return;
        }

        var buffers = &output->Buffer;
        for (var index = 0; index < output->NumberBuffers; index++)
        {
            new Span<byte>(buffers[index].Data, (int)buffers[index].DataByteSize).Clear();
        }
    }

    // The UI thread: the player's equalizer changed, so the app's sound follows it.
    private void OnLeaderChanged(object? sender, EventArgs e)
    {
        if (_leader is { } leader)
        {
            _shaping?.CopyFrom(leader);
        }
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

        lock (_tapGate)
        {
            if (_tap != 0)
            {
                AudioTap.AudioHardwareDestroyProcessTap(_tap);
                _tap = 0;
            }
        }

        if (_leader is not null)
        {
            _leader.Changed -= OnLeaderChanged;
            _leader = null;
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

    /// <summary>Hands the equalizer the slice of sound Core Audio just gave, as if it were reading a track.</summary>
    private sealed class BlockSource : ISampleProvider
    {
        private float[] _samples = Array.Empty<float>();
        private int _count;

        public BlockSource(int sampleRate) => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        public WaveFormat WaveFormat { get; }

        public void Fill(float[] samples, int count)
        {
            _samples = samples;
            _count = count;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = Math.Min(count, _count);
            if (!ReferenceEquals(buffer, _samples) || offset != 0)
            {
                Array.Copy(_samples, 0, buffer, offset, read);
            }

            _count -= read;
            return read;
        }
    }
}
