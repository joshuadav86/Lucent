using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using WinampInspiredVisualizer.Recording;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Audio;

/// <summary>An audio interface, mixer, microphone or other input that can be recorded from.</summary>
/// <param name="Id">"wasapi:" and Windows' id for the device, or "asio:" and the ASIO driver's name.</param>
public sealed record RecordingInput(string Id, string Name)
{
    /// <summary>Recorded through its ASIO driver, which talks to the interface directly.</summary>
    public bool IsAsio => Id.StartsWith("asio:", StringComparison.Ordinal);
}

/// <summary>What an input can do, for the Record window's choices.</summary>
/// <param name="Channels">How many input channels it has.</param>
/// <param name="SampleRates">The rates it records at itself. Others are offered too; Lucent converts to them.</param>
/// <param name="NativeRate">The one rate it runs at, when it can't be changed from Lucent (Windows sets it), or null.</param>
public sealed record InputDetails(int Channels, IReadOnlyList<int> SampleRates, int? NativeRate);

/// <summary>
/// The inputs Lucent can record from: every recording device Windows lists (through WASAPI), and every
/// ASIO driver installed, which is how most audio interfaces and DJ mixers are recorded with the lowest
/// delay and at the rate chosen. The Mac lists its Core Audio inputs instead (the Mac's Audio/LiveInputs.cs).
/// </summary>
public static class LiveInputs
{
    /// <summary>Every input that's plugged in and switched on, Windows' first, then the ASIO drivers.</summary>
    public static IReadOnlyList<RecordingInput> List()
    {
        var inputs = new List<RecordingInput>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            inputs.AddRange(enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device =>
                {
                    using (device)
                    {
                        return new RecordingInput("wasapi:" + device.ID, device.FriendlyName);
                    }
                })
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Audio inputs", ex);
        }

        try
        {
            if (AsioOut.isSupported())
            {
                inputs.AddRange(AsioOut.GetDriverNames().Select(name => new RecordingInput("asio:" + name, $"{name} (ASIO)")));
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("ASIO drivers", ex);
        }

        return inputs;
    }

    /// <summary>The input with this id, or Windows' default recording device when it's gone or none was chosen.</summary>
    public static RecordingInput? Find(string? id, IReadOnlyList<RecordingInput> inputs)
    {
        if (id is not null && inputs.FirstOrDefault(input => input.Id == id) is { } chosen)
        {
            return chosen;
        }

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
            return inputs.FirstOrDefault(input => input.Id == "wasapi:" + device.ID) ?? inputs.FirstOrDefault();
        }
        catch (Exception)
        {
            return inputs.FirstOrDefault();
        }
    }

    /// <summary>
    /// Asks the input how many channels it has and which rates it runs at. An ASIO driver is opened for a
    /// moment to ask; drivers expect the thread that starts them, so this is called on the Record window's.
    /// </summary>
    public static InputDetails Describe(RecordingInput input)
    {
        if (input.IsAsio)
        {
            using var asio = new AsioOut(input.Id["asio:".Length..]);
            var rates = RecordingSettings.SampleRates.Where(rate => Supports(asio, rate)).ToList();
            return new InputDetails(Math.Max(1, asio.DriverInputChannelCount), rates, null);
        }

        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDevice(input.Id["wasapi:".Length..]);
        var format = device.AudioClient.MixFormat;
        return new InputDetails(Math.Max(1, format.Channels), new[] { format.SampleRate }, format.SampleRate);
    }

    private static bool Supports(AsioOut asio, int rate)
    {
        try
        {
            return asio.IsSampleRateSupported(rate);
        }
        catch (Exception)
        {
            return false;
        }
    }
}

/// <summary>
/// One input being listened to, handing its sound to a <see cref="LiveRecorder"/>. Through ASIO the
/// interface runs at the chosen rate and only the chosen channels arrive; through WASAPI, Windows hands
/// over every channel at the rate set in its Sound settings, and the recorder converts the rate if needed.
/// </summary>
public sealed class LiveInput : IDisposable
{
    private readonly LiveRecorder _recorder;
    private readonly int _firstChannel;
    private WasapiCapture? _wasapi;
    private AsioOut? _asio;
    private float[] _samples = Array.Empty<float>();

    private LiveInput(LiveRecorder recorder, int firstChannel)
    {
        _recorder = recorder;
        _firstChannel = firstChannel;
    }

    /// <summary>Raised (on a background thread) when the input stops by itself: unplugged, say.</summary>
    public event EventHandler<Exception?>? Stopped;

    /// <summary>The rate the sound arrives at.</summary>
    public int InputRate { get; private set; }

    /// <summary>
    /// Starts listening, with the recorder's channels starting at <paramref name="firstChannel"/>. ASIO drivers
    /// must be started and stopped on the same thread (the Record window's).
    /// </summary>
    public static LiveInput Start(RecordingInput input, LiveRecorder recorder, int firstChannel)
    {
        var live = new LiveInput(recorder, firstChannel);
        try
        {
            if (input.IsAsio)
            {
                live.StartAsio(input);
            }
            else
            {
                live.StartWasapi(input);
            }
        }
        catch
        {
            live.Dispose();
            throw;
        }

        return live;
    }

    private void StartWasapi(RecordingInput input)
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(input.Id["wasapi:".Length..]);
        // 20 ms buffers in shared mode: the format is Windows' mix format, usually 32-bit float.
        var capture = new WasapiCapture(device, true, 20);
        InputRate = capture.WaveFormat.SampleRate;
        capture.DataAvailable += OnWasapiData;
        capture.RecordingStopped += (_, e) => Stopped?.Invoke(this, e.Exception);
        _wasapi = capture;
        capture.StartRecording();
    }

    private void OnWasapiData(object? sender, WaveInEventArgs e)
    {
        if (_wasapi is not { } capture || e.BytesRecorded == 0)
        {
            return;
        }

        var format = capture.WaveFormat;
        var channels = Math.Max(1, format.Channels);
        var bytesPerSample = format.BitsPerSample / 8;
        var count = e.BytesRecorded / bytesPerSample;
        if (_samples.Length < count)
        {
            _samples = new float[count];
        }

        var data = e.Buffer.AsSpan(0, count * bytesPerSample);
        var isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat
            || (format is WaveFormatExtensible extensible && extensible.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        if (isFloat && bytesPerSample == 4)
        {
            MemoryMarshal.Cast<byte, float>(data).CopyTo(_samples);
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var at = i * bytesPerSample;
                _samples[i] = bytesPerSample switch
                {
                    2 => BitConverter.ToInt16(data[at..]) / 32768f,
                    3 => ((data[at] << 8) | (data[at + 1] << 16) | (data[at + 2] << 24)) / 2147483648f,
                    4 => BitConverter.ToInt32(data[at..]) / 2147483648f,
                    _ => 0,
                };
            }
        }

        _recorder.Write(_samples.AsSpan(0, count), count / channels, channels, _firstChannel, InputRate);
    }

    private void StartAsio(RecordingInput input)
    {
        var asio = new AsioOut(input.Id["asio:".Length..]);
        _asio = asio;
        var channels = Math.Min(_recorder.Channels, Math.Max(1, asio.DriverInputChannelCount - _firstChannel));
        asio.InputChannelOffset = Math.Min(_firstChannel, Math.Max(0, asio.DriverInputChannelCount - 1));
        // Recording only: no sound goes out through the driver.
        asio.InitRecordAndPlayback(null, channels, _recorder.SampleRate);
        InputRate = _recorder.SampleRate;
        asio.AudioAvailable += (_, e) =>
        {
            var count = e.SamplesPerBuffer * e.InputBuffers.Length;
            if (_samples.Length < count)
            {
                _samples = new float[count];
            }

            var written = e.GetAsInterleavedSamples(_samples);
            _recorder.Write(_samples.AsSpan(0, written), written / channels, channels, 0, InputRate);
        };
        asio.PlaybackStopped += (_, e) => Stopped?.Invoke(this, e.Exception);
        asio.Play();
    }

    public void Dispose()
    {
        var wasapi = _wasapi;
        _wasapi = null;
        if (wasapi is not null)
        {
            wasapi.DataAvailable -= OnWasapiData;
            try
            {
                wasapi.StopRecording();
            }
            catch (Exception)
            {
                // Already stopped by Windows.
            }

            wasapi.Dispose();
        }

        var asio = _asio;
        _asio = null;
        if (asio is not null)
        {
            try
            {
                asio.Stop();
            }
            catch (Exception)
            {
                // The driver had already stopped.
            }

            asio.Dispose();
        }
    }
}
