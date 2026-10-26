using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using Lucent.Video;

namespace Lucent.Audio;

/// <summary>
/// Picks the right decoder for a music file. Each is tried in turn, so a file any of them reads opens everywhere: in
/// the player, the editor, converting, finding beats and the recorder alike.
/// </summary>
/// <remarks>
/// <list type="number">
/// <item>Uncompressed files (WAV, RF64, Wave64, AIFF, AIFF-C, CAF): <see cref="PcmFileReader"/>, the same on both systems.</item>
/// <item>Ogg Vorbis and Opus: managed decoders.</item>
/// <item>The system's decoders (<see cref="OpenWithSystemDecoder"/>): MP3, AAC, ALAC, FLAC, WMA on Windows, and so on.</item>
/// <item>libVLC, for everything else: <see cref="LibVlcAudioDecoder"/> for decoding, <see cref="VideoStream"/> for playing.</item>
/// </list>
/// </remarks>
public static class AudioReaderFactory
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".mp4", ".wma",
        ".ogg", ".oga", ".opus", ".aif", ".aiff", ".aifc",
        // Read by Lucent's own PCM reader.
        ".wave", ".bwf", ".rf64", ".w64", ".caf",
        // Read by the system's decoders where they can, otherwise by libVLC.
        ".m4b", ".mp2", ".ac3", ".eac3", ".ec3", ".dts", ".mka", ".weba", ".tta", ".spx",
        // Read by Lucent's own WavPack and Monkey's Audio decoders.
        ".wv", ".ape",
    };

    /// <summary>Extensions <see cref="PcmFileReader"/> is tried on first.</summary>
    private static readonly IReadOnlySet<string> PcmExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".wave", ".bwf", ".rf64", ".w64", ".aif", ".aiff", ".aifc", ".caf",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Opens the formats that need the operating system's decoders (MP3, WAV, AIFF, FLAC, AAC/M4A,
    /// ALAC, WMA). Each app sets this when it starts; Ogg and Opus are decoded here on every platform.
    /// </summary>
    public static Func<string, WaveStream>? OpenWithSystemDecoder { get; set; }

    /// <summary>
    /// Opens a file for the player. A video (see <see cref="VideoFiles"/>) opens as a <see cref="VideoStream"/>: its
    /// sound, played as it's shown, with its pictures alongside. So does music only libVLC reads, which then plays as
    /// it decodes rather than after decoding it all. Anything else opens with <see cref="OpenQuickly"/>.
    /// </summary>
    public static WaveStream OpenForPlaying(string path)
    {
        if (VideoFiles.IsVideo(path))
        {
            return new VideoStream(path);
        }

        try
        {
            return OpenQuickly(path);
        }
        catch (Exception) when (VideoEngine.MayWork && File.Exists(path))
        {
            // libVLC's reasons are less use than the first decoder's, so that's the one passed on if libVLC fails too.
            // Raw Dolby Digital and FLAC in Ogg are decoded first instead (quickly), since libVLC plays only their start.
            WaveStream? stream = Ac3Matroska.IsRawAc3(path) || OggFlac.IsOggFlac(path) ? DecodeWithLibVlc(path) : PlayWithLibVlc(path);
            if (stream is null)
            {
                throw;
            }

            return stream;
        }
    }

    private static VideoStream? PlayWithLibVlc(string path)
    {
        try
        {
            var stream = new VideoStream(path);
            if (stream.HasSound)
            {
                return stream;
            }

            stream.Dispose();
        }
        catch (Exception)
        {
            // Not something libVLC plays either.
        }

        return null;
    }

    /// <summary>
    /// Opens a music file, to be read as fast as it decodes (for the editor, converting and finding beats too), with
    /// whichever decoder reads it: libVLC last, which decodes the whole file first (a second or two for a song). A
    /// video's sound opens this way too.
    /// </summary>
    public static WaveStream Open(string path)
    {
        try
        {
            return OpenQuickly(path);
        }
        catch (Exception) when (VideoEngine.MayWork && File.Exists(path))
        {
            var decoded = DecodeWithLibVlc(path);
            if (decoded is null)
            {
                throw;
            }

            return decoded;
        }
    }

    private static WaveStream? DecodeWithLibVlc(string path)
    {
        try
        {
            return LibVlcAudioDecoder.Open(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Opens a music file with a decoder that starts at once: Lucent's own or the system's, never libVLC. Throws when
    /// none of them reads it (the player lines up the next song this way, and leaves anything slower until it plays).
    /// </summary>
    public static WaveStream OpenQuickly(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (PcmExtensions.Contains(extension) && PcmFileReader.TryOpen(path) is { } pcm)
        {
            return pcm;
        }

        return extension switch
        {
            ".ogg" or ".oga" => OpenOgg(path),
            ".opus" => new OpusWaveStream(path),
            ".wv" => WavPackReader.TryOpen(path) ?? throw new InvalidDataException("This isn't a WavPack file."),
            ".ape" => ApeReader.TryOpen(path) ?? throw new InvalidDataException("This isn't a Monkey's Audio file."),
            _ => OpenWithSystemDecoder?.Invoke(path)
                ?? throw new NotSupportedException($"No decoder for {Path.GetExtension(path)} files on this platform."),
        };
    }

    private static WaveStream OpenOgg(string path)
    {
        // .ogg files usually hold Vorbis audio but can also hold Opus, or FLAC (read by the system's FLAC decoder).
        if (OggFlac.IsOggFlac(path))
        {
            return OggFlac.Open(path, OpenWithSystemDecoder
                ?? throw new NotSupportedException("No FLAC decoder on this platform."));
        }

        try
        {
            return new VorbisWaveReader(path);
        }
        catch (Exception)
        {
            return new OpusWaveStream(path);
        }
    }
}
