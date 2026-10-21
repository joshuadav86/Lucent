using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Video;

/// <summary>Which files are videos, played with libVLC (<see cref="VideoStream"/>) rather than the music decoders.</summary>
public static class VideoFiles
{
    /// <summary>The video files Lucent opens: everything common that VLC plays.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".webm", ".avi", ".mov", ".m4v", ".mp4", ".wmv", ".asf", ".flv", ".f4v", ".mpg", ".mpeg", ".m2v",
        ".ts", ".m2ts", ".mts", ".vob", ".3gp", ".3g2", ".ogv", ".divx", ".xvid", ".rm", ".rmvb", ".dv", ".mxf",
    };

    /// <summary>Extensions that hold music as often as video (an .mp4 can be either), so the file is looked inside.</summary>
    private static readonly IReadOnlySet<string> Either = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".asf" };

    /// <summary>True for a file Lucent can play: music, or a video.</summary>
    public static bool IsPlayable(string path) => AudioReaderFactory.IsSupported(path) || Extensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// True when <paramref name="path"/> is a video: it has a video extension and, for the ones music comes in
    /// too, a picture inside. Music in an .mp4 (no picture, or only album art) stays music.
    /// </summary>
    public static bool IsVideo(string path)
    {
        var extension = Path.GetExtension(path);
        if (!Extensions.Contains(extension))
        {
            return false;
        }

        if (!Either.Contains(extension))
        {
            return true;
        }

        try
        {
            using var file = TagLib.File.Create(path);
            return file.Properties is { } properties && properties.MediaTypes.HasFlag(TagLib.MediaTypes.Video)
                && properties.VideoWidth > 0 && properties.VideoHeight > 0;
        }
        catch (Exception)
        {
            // TagLib doesn't know it: let libVLC try, which plays almost anything.
            return true;
        }
    }
}
