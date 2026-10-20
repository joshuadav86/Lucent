using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer.Audio;

/// <summary>Picks the right decoder for a music file based on its extension.</summary>
public static class AudioReaderFactory
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".m4a", ".aac", ".mp4", ".wma",
        ".ogg", ".oga", ".opus", ".aif", ".aiff", ".aifc",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Opens the formats that need the operating system's decoders (MP3, WAV, AIFF, FLAC, AAC/M4A,
    /// ALAC, WMA). Each app sets this when it starts; Ogg and Opus are decoded here on every platform.
    /// </summary>
    public static Func<string, WaveStream>? OpenWithSystemDecoder { get; set; }

    /// <summary>
    /// Opens a file for the player. A video (see <see cref="VideoFiles"/>) opens as a <see cref="VideoStream"/>: its
    /// sound, played as it's shown, with its pictures alongside. Anything else opens with <see cref="Open"/>.
    /// </summary>
    public static WaveStream OpenForPlaying(string path) => VideoFiles.IsVideo(path) ? new VideoStream(path) : Open(path);

    /// <summary>
    /// Opens a music file, to be read as fast as it decodes (for the editor, converting and finding beats too). A
    /// video's sound opens this way only where the system's decoders read it (an .mp4's, say).
    /// </summary>
    public static WaveStream Open(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" or ".oga" => OpenOgg(path),
            ".opus" => new OpusWaveStream(path),
            _ => OpenWithSystemDecoder?.Invoke(path)
                ?? throw new NotSupportedException($"No decoder for {Path.GetExtension(path)} files on this platform."),
        };
    }

    private static WaveStream OpenOgg(string path)
    {
        // .ogg files usually hold Vorbis audio but can also hold Opus.
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
