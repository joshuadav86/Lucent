using System.IO;

namespace WinampInspiredVisualizer.Playlist;

/// <summary>A file in the playlist, with whatever tag info could be read from it.</summary>
public sealed class Track
{
    private Track(string path, string title, string artist, TimeSpan duration, int bitrate = 0, int sampleRate = 0, int channels = 0,
        string album = "")
    {
        Path = path;
        Title = title;
        Artist = artist;
        Album = album;
        Duration = duration;
        Bitrate = bitrate;
        SampleRate = sampleRate;
        Channels = channels;
    }

    public string Path { get; }

    public string Title { get; }

    public string Artist { get; }

    /// <summary>The album name, or empty if the file doesn't say.</summary>
    public string Album { get; }

    public TimeSpan Duration { get; }

    /// <summary>Kilobits per second, or 0 if unknown.</summary>
    public int Bitrate { get; }

    /// <summary>Samples per second, or 0 if unknown.</summary>
    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>"320 kbps · 44.1 kHz · Stereo", leaving out whatever isn't known.</summary>
    public string AudioDetails
    {
        get
        {
            var parts = new List<string>();
            if (Bitrate > 0)
            {
                parts.Add($"{Bitrate} kbps");
            }

            if (SampleRate > 0)
            {
                parts.Add($"{SampleRate / 1000.0:0.#} kHz");
            }

            if (Channels > 0)
            {
                parts.Add(Channels switch { 1 => "Mono", 2 => "Stereo", _ => $"{Channels} channels" });
            }

            return string.Join("  ·  ", parts);
        }
    }

    public string DurationText => Duration > TimeSpan.Zero ? TimeFormat.Format(Duration) : string.Empty;

    /// <summary>
    /// The song on a radio station, for showing like a track (in the media controls, say). The station's address
    /// stands in for the path, and the station's name for the album.
    /// </summary>
    public static Track ForStation(string address, string title, string artist, string station, int bitrate) =>
        new(address, title, artist, TimeSpan.Zero, bitrate, album: station);

    public static Track FromFile(string path)
    {
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? fallbackTitle : file.Tag.Title;
            var artist = file.Tag.FirstPerformer ?? string.Empty;
            var properties = file.Properties;
            return new Track(path, title, artist, properties?.Duration ?? TimeSpan.Zero,
                properties?.AudioBitrate ?? 0, properties?.AudioSampleRate ?? 0, properties?.AudioChannels ?? 0,
                file.Tag.Album ?? string.Empty);
        }
        catch (Exception)
        {
            // Untagged or unusual files still play; they just show their file name.
            return new Track(path, fallbackTitle, string.Empty, TimeSpan.Zero);
        }
    }
}

public static class TimeFormat
{
    public static string Format(TimeSpan time) =>
        time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
}
