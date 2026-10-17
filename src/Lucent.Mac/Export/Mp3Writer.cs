using System.Runtime.InteropServices;
using NAudio.Wave;
using WinampInspiredVisualizer.Export;

namespace Lucent.Mac.Export;

/// <summary>
/// Encodes MP3 with LAME (libmp3lame.dylib, shipped next to Lucent in the app; tools/build-lame-mac.sh makes it).
/// macOS has no MP3 encoder of its own, so unlike Windows there's nothing to fall back on: without LAME,
/// MP3 can't be written, and the export window says so.
/// </summary>
public static class Mp3Writer
{
    private const string LibraryName = "libmp3lame.dylib";

    private static readonly object LoadLock = new();
    private static Lame? _lame;
    private static bool _loadTried;

    /// <summary>True when LAME's library is present and loads.</summary>
    public static bool IsLameAvailable => Load() is not null;

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
    /// Returns a note for the user if the quality had to change (it never does with LAME).
    /// </summary>
    public static string? Encode(ISampleProvider source, string path, Mp3Quality quality, Action<long> framesWritten,
        CancellationToken cancellation)
    {
        var lame = Load() ?? throw new IOException(
            "MP3 files can't be made because Lucent's MP3 encoder (LAME) is missing. Reinstalling Lucent puts it back; " +
            "until then, FLAC, WAV and AIFF work.");
        lame.Encode(source, path, quality, framesWritten, cancellation);
        return null;
    }

    private static Lame? Load()
    {
        lock (LoadLock)
        {
            if (!_loadTried)
            {
                _loadTried = true;
                _lame = Lame.TryLoad(Path.Combine(AppContext.BaseDirectory, LibraryName));
            }

            return _lame;
        }
    }

    /// <summary>The parts of LAME's C API that encoding a file needs, looked up in the library once it's loaded.</summary>
    private sealed unsafe class Lame
    {
        // LAME's presets for its VBR quality levels (lame.h: V0 = 500, V2 = 480).
        private const int PresetV0 = 500;
        private const int PresetV2 = 480;
        private const int VbrOff = 0;
        // vbr_mtrh, LAME's own default VBR mode (what its -V switch uses). The V presets only tune VBR;
        // they don't switch it on.
        private const int VbrDefault = 4;

        private readonly delegate* unmanaged<IntPtr> _init;
        private readonly delegate* unmanaged<IntPtr, int> _close;
        private readonly delegate* unmanaged<IntPtr, int, int> _setInSampleRate;
        private readonly delegate* unmanaged<IntPtr, int, int> _setNumChannels;
        private readonly delegate* unmanaged<IntPtr, int, int> _setBitrate;
        private readonly delegate* unmanaged<IntPtr, int, int> _setVbr;
        private readonly delegate* unmanaged<IntPtr, int, int> _setPreset;
        private readonly delegate* unmanaged<IntPtr, int, int> _setWriteVbrTag;
        private readonly delegate* unmanaged<IntPtr, int, void> _setWriteId3TagAutomatic;
        private readonly delegate* unmanaged<IntPtr, int> _initParams;
        private readonly delegate* unmanaged<IntPtr, float*, float*, int, byte*, int, int> _encodeFloat;
        private readonly delegate* unmanaged<IntPtr, byte*, int, int> _flush;
        private readonly delegate* unmanaged<IntPtr, byte*, nuint, nuint> _getLameTagFrame;

        private Lame(IntPtr library)
        {
            IntPtr Export(string name) => NativeLibrary.GetExport(library, name);
            _init = (delegate* unmanaged<IntPtr>)Export("lame_init");
            _close = (delegate* unmanaged<IntPtr, int>)Export("lame_close");
            _setInSampleRate = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_in_samplerate");
            _setNumChannels = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_num_channels");
            _setBitrate = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_brate");
            _setVbr = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_VBR");
            _setPreset = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_preset");
            _setWriteVbrTag = (delegate* unmanaged<IntPtr, int, int>)Export("lame_set_bWriteVbrTag");
            _setWriteId3TagAutomatic = (delegate* unmanaged<IntPtr, int, void>)Export("lame_set_write_id3tag_automatic");
            _initParams = (delegate* unmanaged<IntPtr, int>)Export("lame_init_params");
            _encodeFloat = (delegate* unmanaged<IntPtr, float*, float*, int, byte*, int, int>)Export("lame_encode_buffer_ieee_float");
            _flush = (delegate* unmanaged<IntPtr, byte*, int, int>)Export("lame_encode_flush");
            _getLameTagFrame = (delegate* unmanaged<IntPtr, byte*, nuint, nuint>)Export("lame_get_lametag_frame");
        }

        public static Lame? TryLoad(string path)
        {
            if (!File.Exists(path) || !NativeLibrary.TryLoad(path, out var library))
            {
                return null;
            }

            try
            {
                return new Lame(library);
            }
            catch (EntryPointNotFoundException)
            {
                // Too old a LAME (before 3.99) to take float samples.
                NativeLibrary.Free(library);
                return null;
            }
        }

        public void Encode(ISampleProvider source, string path, Mp3Quality quality, Action<long> framesWritten,
            CancellationToken cancellation)
        {
            var format = source.WaveFormat;
            var channels = format.Channels;
            if (channels is < 1 or > 2)
            {
                throw new ArgumentException("MP3 holds one or two channels.", nameof(source));
            }

            var encoder = _init();
            if (encoder == IntPtr.Zero)
            {
                throw new OutOfMemoryException();
            }

            try
            {
                _setInSampleRate(encoder, format.SampleRate);
                _setNumChannels(encoder, channels);
                // The tags are copied in afterwards (TagCopier); LAME adds none of its own.
                _setWriteId3TagAutomatic(encoder, 0);
                // The Xing/LAME header lets players seek accurately and know the length of a VBR file.
                _setWriteVbrTag(encoder, 1);
                if (ConstantBitrate(quality) is { } bitrate)
                {
                    _setVbr(encoder, VbrOff);
                    _setBitrate(encoder, bitrate);
                }
                else
                {
                    _setVbr(encoder, VbrDefault);
                    _setPreset(encoder, quality == Mp3Quality.VbrV0 ? PresetV0 : PresetV2);
                }

                if (_initParams(encoder) < 0)
                {
                    throw new IOException($"The MP3 encoder can't write {format.SampleRate} Hz audio.");
                }

                const int block = 8192;
                var buffer = new float[block * channels];
                var left = new float[block];
                var right = new float[block];
                var mp3 = new byte[block * 5 / 4 + 7200];

                // Read and write access, so the header can be filled in at the end.
                using var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    var frames = read / channels;
                    for (var i = 0; i < frames; i++)
                    {
                        left[i] = buffer[i * channels];
                        right[i] = buffer[i * channels + channels - 1];
                    }

                    int bytes;
                    fixed (float* l = left, r = right)
                    fixed (byte* output = mp3)
                    {
                        bytes = _encodeFloat(encoder, l, r, frames, output, mp3.Length);
                    }

                    Check(bytes);
                    stream.Write(mp3, 0, bytes);
                    framesWritten(frames);
                }

                int flushed;
                fixed (byte* output = mp3)
                {
                    flushed = _flush(encoder, output, mp3.Length);
                }

                Check(flushed);
                stream.Write(mp3, 0, flushed);

                // The first frame was left blank for the header; now that the whole file is known, fill it in.
                nuint size;
                fixed (byte* output = mp3)
                {
                    size = _getLameTagFrame(encoder, output, (nuint)mp3.Length);
                }

                if (size > 0 && size <= (nuint)mp3.Length)
                {
                    stream.Position = 0;
                    stream.Write(mp3, 0, (int)size);
                }
            }
            finally
            {
                _close(encoder);
            }
        }

        private static void Check(int result)
        {
            if (result < 0)
            {
                throw new IOException(result == -1
                    ? "The MP3 encoder ran out of room for its output."
                    : $"The MP3 encoder failed (LAME error {result}).");
            }
        }
    }
}
