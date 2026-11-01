using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>
/// Listens to everything Windows is playing (Spotify, Apple Music, a browser, a game) through WASAPI
/// loopback on the default output, and feeds it to an <see cref="AudioCapture"/>, so the visuals, meter,
/// equalizer's spectrum and analyzer react to any app. It only listens: the sound itself is untouched.
/// Lucent's own music is part of what Windows plays, so it still shows too. <see cref="StartForApp"/>
/// listens to one app instead (the Spotify or browser Lucent controls), through Windows' process loopback.
/// </summary>
/// <remarks>
/// The Mac does the same with a Core Audio process tap (see the Mac's Audio/SystemAudio.cs). Music Lucent
/// plays in exclusive mode skips Windows' mixer, so loopback can't hear it.
/// </remarks>
public sealed class SystemAudio : IDisposable
{
    private readonly AudioCapture _capture;
    private WasapiLoopbackCapture? _loopback;

    // Told when Windows switches its default output (headphones plugged in, say). Loopback stays on the output it
    // started on and hears nothing once Windows plays through another, so it has to start again on the new one.
    private MMDeviceEnumerator? _devices;
    private OutputWatch? _outputWatch;

    // Listening to one app: the thread that reads it (and owns Windows' audio client for it), whether it has
    // started handing sound on, and whether it's been told to stop.
    private Thread? _appThread;
    private volatile bool _listeningToApp;
    private volatile bool _stopping;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    private SystemAudio(AudioCapture capture) => _capture = capture;

    /// <summary>
    /// Raised (on a background thread) when Windows stops the listening, say because the output went away, or when it
    /// switches to another output. Start again to follow it.
    /// </summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>
    /// Starts listening. Throws when Windows has no output to listen to. Dispose to go back to the player's own sound.
    /// </summary>
    public static SystemAudio Start(AudioCapture capture)
    {
        var listener = new SystemAudio(capture);
        var loopback = new WasapiLoopbackCapture();
        try
        {
            var format = loopback.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.IeeeFloat
                && !(format is WaveFormatExtensible extensible && extensible.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT))
            {
                throw new NotSupportedException($"Windows' mixer is using a format Lucent can't listen to ({format}).");
            }

            capture.BeginExternal(format.SampleRate);
            loopback.DataAvailable += listener.OnData;
            loopback.RecordingStopped += (_, e) => listener.Stopped?.Invoke(listener, e.Exception);
            loopback.StartRecording();
        }
        catch
        {
            loopback.Dispose();
            capture.EndExternal();
            throw;
        }

        listener._loopback = loopback;
        listener.WatchOutput();
        return listener;
    }

    // Without the watch it still listens; it just won't follow a change of output by itself.
    private void WatchOutput()
    {
        try
        {
            _devices = new MMDeviceEnumerator();
            _outputWatch = new OutputWatch(this);
            _devices.RegisterEndpointNotificationCallback(_outputWatch);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't watch for a change of output: {ex.Message}");
            _outputWatch = null;
            _devices?.Dispose();
            _devices = null;
        }
    }

    private void StopWatchingOutput()
    {
        if (_devices is { } devices && _outputWatch is { } watch)
        {
            try
            {
                devices.UnregisterEndpointNotificationCallback(watch);
            }
            catch (Exception)
            {
                // Going anyway.
            }
        }

        _outputWatch = null;
        _devices?.Dispose();
        _devices = null;
    }

    // Windows calls these from its own thread, and mustn't be held up, so each just passes the news on.
    private sealed class OutputWatch(SystemAudio listener) : IMMNotificationClient
    {
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // Loopback listens to the multimedia default (Windows reports each role's change separately).
            if (flow == DataFlow.Render && role == Role.Multimedia && listener._loopback is not null)
            {
                ThreadPool.QueueUserWorkItem(_ => listener.Stopped?.Invoke(listener, null));
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }

        public void OnDeviceRemoved(string deviceId)
        {
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }

    /// <summary>The name of the output being listened to, for the menu's tooltip.</summary>
    public static string? DefaultOutputName()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return device.FriendlyName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Windows' mixer works in 32-bit floating point, so loopback hands over floats, interleaved.
    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_loopback is not { } loopback || e.BytesRecorded == 0)
        {
            return;
        }

        Deliver(MemoryMarshalFloats(e.Buffer, e.BytesRecorded), Math.Max(1, loopback.WaveFormat.Channels));
    }

    private void Deliver(ReadOnlySpan<float> samples, int channels)
    {
        var frames = samples.Length / channels;
        if (_mono.Length < frames)
        {
            _mono = new float[frames];
            _left = new float[frames];
            _right = new float[frames];
        }

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

        _capture.WriteExternal(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
    }

    private static ReadOnlySpan<float> MemoryMarshalFloats(byte[] buffer, int bytes) =>
        System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, bytes - bytes % sizeof(float)));

    /// <summary>True when Windows can listen to a single app (Windows 10 version 2004 and later).</summary>
    public static bool CanListenToOneApp => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);

    // What process loopback hands over: Windows converts the app's sound to this.
    private const int AppSampleRate = 48000;
    private const int AppChannels = 2;

    /// <summary>
    /// Starts listening to just one app: the process <paramref name="processId"/> and the processes it started
    /// (a browser's audio, or Spotify's, plays from a helper process it starts). Throws when Windows can't.
    /// </summary>
    public static SystemAudio StartForApp(AudioCapture capture, int processId)
    {
        var listener = new SystemAudio(capture);
        // Windows' audio client lives on the thread that reads it, which is in the multithreaded apartment, so it's
        // made there rather than on the UI thread (a single-threaded apartment) and never crosses between the two.
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        listener._appThread = new Thread(() => listener.ReadApp(processId, started)) { IsBackground = true, Name = "Lucent app listening" };
        listener._appThread.SetApartmentState(ApartmentState.MTA);
        listener._appThread.Start();
        if (!((IAsyncResult)started.Task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5)))
        {
            listener._stopping = true;
            throw new TimeoutException("Windows didn't start listening to the app.");
        }

        // Throws what stopped it starting, if anything did.
        started.Task.GetAwaiter().GetResult();
        capture.BeginExternal(AppSampleRate);
        listener._listeningToApp = true;
        return listener;
    }

    // Starts Windows listening to the app, then waits for each block of its sound and hands it on, until disposed.
    private void ReadApp(int processId, TaskCompletionSource started)
    {
        AudioClient client;
        try
        {
            client = new AudioClient(ProcessLoopback.Activate(processId));
            client.Initialize(AudioClientShareMode.Shared,
                AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | AudioClientStreamFlags.AutoConvertPcm
                    | AudioClientStreamFlags.SrcDefaultQuality,
                200 * 10_000, 0, WaveFormat.CreateIeeeFloatWaveFormat(AppSampleRate, AppChannels), Guid.Empty);
        }
        catch (Exception ex)
        {
            started.SetException(ex);
            return;
        }

        using var ready = new AutoResetEvent(false);
        var buffer = Array.Empty<float>();
        try
        {
            client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            client.Start();
            started.SetResult();
            var reader = client.AudioCaptureClient;
            while (!_stopping)
            {
                // Asked even when no sound has come for a while: a client Windows has dropped (its audio service
                // restarted, say) only says so when asked, and otherwise waits here hearing nothing, for good.
                ready.WaitOne(200);
                for (var packet = reader.GetNextPacketSize(); packet > 0 && !_stopping; packet = reader.GetNextPacketSize())
                {
                    var data = reader.GetBuffer(out var frames, out var flags);
                    var count = frames * AppChannels;
                    if (buffer.Length < count)
                    {
                        buffer = new float[count];
                    }

                    if ((flags & AudioClientBufferFlags.Silent) != 0)
                    {
                        Array.Clear(buffer, 0, count);
                    }
                    else
                    {
                        Marshal.Copy(data, buffer, 0, count);
                    }

                    reader.ReleaseBuffer(frames);
                    if (_listeningToApp)
                    {
                        Deliver(buffer.AsSpan(0, count), AppChannels);
                    }
                }
            }

            client.Stop();
        }
        catch (Exception ex) when (!_stopping)
        {
            started.TrySetException(ex);
            Stopped?.Invoke(this, ex);
        }
        catch (Exception)
        {
            // Stopping anyway.
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>
    /// The process to listen to for the app called <paramref name="processName"/> (as Task Manager's Details tab
    /// names it, without .exe): the one the others were started from, when several run. Null when it isn't running.
    /// </summary>
    public static Process? FindApp(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        Process? found = null;
        try
        {
            // The app's window belongs to the first process it starts. Without a window (minimised to the tray),
            // the oldest one is that first process.
            found = processes.FirstOrDefault(process => SafeHandle(process) != IntPtr.Zero)
                ?? processes.OrderBy(StartTime).FirstOrDefault();
            return found;
        }
        finally
        {
            foreach (var process in processes.Where(process => process != found))
            {
                process.Dispose();
            }
        }

        static IntPtr SafeHandle(Process process)
        {
            try
            {
                return process.MainWindowHandle;
            }
            catch (InvalidOperationException)
            {
                return IntPtr.Zero;
            }
        }

        static DateTime StartTime(Process process)
        {
            try
            {
                return process.StartTime;
            }
            catch (Exception)
            {
                return DateTime.MaxValue;
            }
        }
    }

    public void Dispose()
    {
        if (_appThread is { } thread)
        {
            _appThread = null;
            _stopping = true;
            thread.Join(1000);
            if (_listeningToApp)
            {
                _listeningToApp = false;
                _capture.EndExternal();
            }

            return;
        }

        StopWatchingOutput();
        var loopback = _loopback;
        _loopback = null;
        if (loopback is null)
        {
            return;
        }

        loopback.DataAvailable -= OnData;
        try
        {
            loopback.StopRecording();
        }
        catch (Exception)
        {
            // Already stopped by Windows.
        }

        loopback.Dispose();
        _capture.EndExternal();
    }
}

/// <summary>
/// Windows' process loopback: an audio client that hears one process (and those it started) rather than a
/// whole output. It comes from ActivateAudioInterfaceAsync with a "virtual device", which NAudio doesn't wrap.
/// </summary>
internal static class ProcessLoopback
{
    private const string Device = @"VAD\Process_Loopback";
    private const ushort BlobVariant = 65; // VT_BLOB
    private const int ProcessLoopbackType = 1; // AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK
    private const int IncludeTree = 0; // PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE
    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    public static IAudioClient Activate(int processId)
    {
        // AUDIOCLIENT_ACTIVATION_PARAMS, inside a PROPVARIANT holding it as a blob.
        var parameters = Marshal.AllocHGlobal(12);
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.WriteInt32(parameters, 0, ProcessLoopbackType);
            Marshal.WriteInt32(parameters, 4, processId);
            Marshal.WriteInt32(parameters, 8, IncludeTree);
            for (var offset = 0; offset < 24; offset += 4)
            {
                Marshal.WriteInt32(variant, offset, 0);
            }

            Marshal.WriteInt16(variant, 0, (short)BlobVariant);
            Marshal.WriteInt32(variant, 8, 12);
            // The blob's pointer follows its size, lined up to the pointer's own size.
            Marshal.WriteIntPtr(variant, IntPtr.Size == 8 ? 16 : 12, parameters);

            var done = new Completion();
            ActivateAudioInterfaceAsync(Device, AudioClientId, variant, done, out _);
            if (!done.Finished.Wait(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Windows didn't start listening to the app.");
            }

            done.Operation!.GetActivateResult(out var result, out var activated);
            Marshal.ThrowExceptionForHR(result);
            return (IAudioClient)activated;
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync([MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, IntPtr activationParams, IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    // Windows calls this back from its own thread, so it has to say it's fine to call from any (IAgileObject).
    private sealed class Completion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public ManualResetEventSlim Finished { get; } = new();

        public IActivateAudioInterfaceAsyncOperation? Operation { get; private set; }

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation)
        {
            Operation = activateOperation;
            Finished.Set();
        }
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAgileObject
    {
    }
}
