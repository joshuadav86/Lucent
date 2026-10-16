using System.IO;

namespace WinampInspiredVisualizer.Playlist;

/// <summary>A file in the playlist, with whatever tag info could be read from it.</summary>
public sealed class Track
{
    private Track(string path, string title, string artist, TimeSpan duration)
    {
        Path = path;
        Title = title;
        Artist = artist;
        Duration = duration;
    }

    public string Path { get; }

    public string Title { get; }

    public string Artist { get; }

    public TimeSpan Duration { get; }

    public string DurationText => Duration > TimeSpan.Zero ? TimeFormat.Format(Duration) : string.Empty;

    public static Track FromFile(string path)
    {
        var fallbackTitle = System.IO.Path.GetFileNameWithoutExtension(path);
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? fallbackTitle : file.Tag.Title;
            var artist = file.Tag.FirstPerformer ?? string.Empty;
            return new Track(path, title, artist, file.Properties?.Duration ?? TimeSpan.Zero);
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
