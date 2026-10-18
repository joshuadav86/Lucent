using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Listens to everything Windows is playing (Spotify, Apple Music, a browser, a game) through WASAPI
/// loopback on the default output, and feeds it to an <see cref="AudioCapture"/>, so the visuals, meter,
/// equalizer's spectrum and analyzer react to any app. It only listens: the sound itself is untouched.
/// Lucent's own music is part of what Windows plays, so it still shows too.
/// </summary>
/// <remarks>
/// The Mac does the same with a Core Audio process tap (see the Mac's Audio/SystemAudio.cs). Music Lucent
/// plays in exclusive mode skips Windows' mixer, so loopback can't hear it.
/// </remarks>
public sealed class SystemAudio : IDisposable
{
    private readonly AudioCapture _capture;
    private WasapiLoopbackCapture? _loopback;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    private SystemAudio(AudioCapture capture) => _capture = capture;

    /// <summary>Raised (on a background thread) when Windows stops the listening, say because the output went away.</summary>
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
        return listener;
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

        var channels = Math.Max(1, loopback.WaveFormat.Channels);
        var samples = MemoryMarshalFloats(e.Buffer, e.BytesRecorded);
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

    public void Dispose()
    {
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
