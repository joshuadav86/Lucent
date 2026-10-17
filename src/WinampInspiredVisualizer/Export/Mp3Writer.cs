using System.IO;
using NAudio.Lame;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Encodes MP3 with LAME (libmp3lame.64.dll, shipped next to Lucid.exe by the NAudio.Lame package).
/// If LAME can't be loaded, constant-bitrate files fall back to the MP3 encoder built into Windows
/// (Media Foundation, Windows 8 and later), which has no VBR mode.
/// </summary>
public static class Mp3Writer
{
    private static readonly object LoadLock = new();
    private static bool? _lameLoaded;

    /// <summary>True when LAME's DLL is present and loads.</summary>
    public static bool IsLameAvailable
    {
        get
        {
            lock (LoadLock)
            {
                if (_lameLoaded is null)
                {
                    try
                    {
                        // NAudio.Lame searches the app folder itself; naming it first keeps that search short.
                        _lameLoaded = !OperatingSystem.IsWindows() || LameDLL.LoadNativeDLL(AppContext.BaseDirectory);
                    }
                    catch (Exception)
                    {
                        _lameLoaded = false;
                    }
                }

                return _lameLoaded.Value;
            }
        }
    }

    /// <summary>The bitrate of a constant-bitrate quality, or null for VBR.</summary>
    public static int? ConstantBitrate(Mp3Quality quality) => quality switch
    {
        Mp3Quality.Cbr128 => 128,
        Mp3Quality.Cbr192 => 192,
        Mp3Quality.Cbr256 => 256,
        Mp3Quality.Cbr320 => 320,
        _ => null,
    };

    /// <summary>
    /// Encodes float audio (one or two channels) from <paramref name="source"/> into an MP3 file.
    /// Returns a note for the user if the quality had to change.
    /// </summary>
    public static string? Encode(ISampleProvider source, string path, Mp3Quality quality, Action<long> framesWritten,
        CancellationToken cancellation)
    {
        if (IsLameAvailable)
        {
            EncodeWithLame(source, path, quality, framesWritten, cancellation);
            return null;
        }

        // Windows' own encoder only does constant bitrates; V0 and V2 get the nearest one.
        var bitrate = ConstantBitrate(quality) ?? (quality == Mp3Quality.VbrV0 ? 256 : 192);
        var counting = new CountingSampleProvider(source, framesWritten, cancellation);
        MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(counting), path, bitrate * 1000);
        return ConstantBitrate(quality) is null
            ? $"The LAME encoder couldn't be loaded, so Windows' MP3 encoder was used at {bitrate} kbps instead of VBR."
            : null;
    }

    private static void EncodeWithLame(ISampleProvider source, string path, Mp3Quality quality, Action<long> framesWritten,
        CancellationToken cancellation)
    {
        var format = source.WaveFormat;
        var config = new LameConfig { WriteVBRTag = true };
        if (ConstantBitrate(quality) is { } bitrate)
        {
            config.BitRate = bitrate;
        }
        else
        {
            config.Preset = quality == Mp3Quality.VbrV0 ? LAMEPreset.V0 : LAMEPreset.V2;
        }

        // Read and write access, so LAME can go back and fill in the header that lets players seek accurately.
        using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var writer = new LameMP3FileWriter(stream, WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels), config);
        var buffer = new float[8192 * format.Channels];
        var bytes = new byte[buffer.Length * sizeof(float)];
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            Buffer.BlockCopy(buffer, 0, bytes, 0, read * sizeof(float));
            writer.Write(bytes, 0, read * sizeof(float));
            framesWritten(read / format.Channels);
        }

        writer.Flush();
    }

    /// <summary>Passes samples through, counting them for progress and stopping when cancelled.</summary>
    private sealed class CountingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly Action<long> _framesWritten;
        private readonly CancellationToken _cancellation;

        public CountingSampleProvider(ISampleProvider source, Action<long> framesWritten, CancellationToken cancellation)
        {
            _source = source;
            _framesWritten = framesWritten;
            _cancellation = cancellation;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            _cancellation.ThrowIfCancellationRequested();
            var read = _source.Read(buffer, offset, count);
            _framesWritten(read / WaveFormat.Channels);
            return read;
        }
    }
}
