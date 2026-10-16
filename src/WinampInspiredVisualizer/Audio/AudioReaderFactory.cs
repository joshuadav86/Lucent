using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

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

    public static WaveStream Open(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".ogg" or ".oga" => OpenOgg(path),
            ".opus" => new OpusWaveStream(path),
            // MP3, WAV and AIFF use NAudio's built-in readers; everything else
            // (FLAC, AAC/M4A, ALAC, WMA) goes through Windows Media Foundation.
            _ => new AudioFileReader(path),
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
