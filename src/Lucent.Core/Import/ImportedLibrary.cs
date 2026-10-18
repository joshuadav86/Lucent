using System.IO;

namespace WinampInspiredVisualizer.Import;

/// <summary>The players and services Lucent can bring a library over from.</summary>
public enum ImportSource
{
    AppleMusic,
    MusicBee,
    Foobar2000,
    PlaylistFiles,
    FileTags,
    Plex,
    YouTubeMusic,
}

public static class ImportSources
{
    /// <summary>The name a source goes by in the import window and in imported playlists' names.</summary>
    public static string Label(ImportSource source) => source switch
    {
        ImportSource.AppleMusic => "Apple Music",
        ImportSource.MusicBee => "MusicBee",
        ImportSource.Foobar2000 => "foobar2000",
        ImportSource.PlaylistFiles => "Playlist files",
        ImportSource.FileTags => "File tags",
        ImportSource.Plex => "Plex",
        _ => "YouTube Music",
    };
}

/// <summary>
/// A song as another player knows it. Some sources know where its file is; others (YouTube Music, a
/// Plex server on another computer) only know what it's called, and it's found in the library by name.
/// </summary>
public sealed record ImportedTrack
{
    /// <summary>The file, as the other player has it. It may be on another drive or computer.</summary>
    public string? Path { get; init; }

    public string? Title { get; init; }

    public string? Artist { get; init; }

    public string? Album { get; init; }

    public TimeSpan Duration { get; init; }

    /// <summary>How many times the other player played it.</summary>
    public int Plays { get; init; }

    /// <summary>Stars, 1 to 5, or 0 for none.</summary>
    public int Rating { get; init; }

    /// <summary>When the other player last played it, in UTC.</summary>
    public DateTime? LastPlayed { get; init; }

    /// <summary>Loved or favorited in the other player. These become Lucent favorites.</summary>
    public bool Loved { get; init; }

    public bool HasStats => Plays > 0 || Rating > 0 || LastPlayed is not null;

    /// <summary>"Artist – Title", or the file name, for the list of songs that weren't found.</summary>
    public string Label =>
        !string.IsNullOrWhiteSpace(Title)
            ? string.IsNullOrWhiteSpace(Artist) ? Title! : $"{Artist} – {Title}"
            : Path is { Length: > 0 } path ? System.IO.Path.GetFileName(path.Replace('\\', '/').TrimEnd('/')) : "Unknown song";

    /// <summary>Stars from a 0–100 rating (Apple Music and MusicBee), rounding half stars up.</summary>
    public static int StarsFromPercent(double percent) => percent <= 0 ? 0 : Math.Clamp((int)Math.Ceiling(percent / 20 - 0.01), 1, 5);
}

/// <summary>A playlist from another player, its songs in order.</summary>
public sealed record ImportedPlaylist(string Name, IReadOnlyList<ImportedTrack> Tracks);

/// <summary>Everything read from one source: its songs (with their play counts and ratings) and its playlists.</summary>
public sealed class ImportedLibrary
{
    public ImportedLibrary(ImportSource source)
    {
        Source = source;
    }

    public ImportSource Source { get; }

    public List<ImportedTrack> Tracks { get; } = new();

    public List<ImportedPlaylist> Playlists { get; } = new();
}

/// <summary>Turns the file:// addresses other players save into paths.</summary>
public static class FileUri
{
    /// <summary>
    /// "file://localhost/C:/Music/a%20b.mp3" or "file:///Users/me/a.mp3" (Apple Music, XSPF), and
    /// "file://C:\Music\a b.mp3" (foobar2000, which doesn't escape anything), as paths. Null for web addresses.
    /// </summary>
    public static string? ToPath(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        text = text.Trim();
        if (!text.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            return text.Contains("://", StringComparison.Ordinal) ? null : text;
        }

        var rest = text[5..];
        var escaped = !rest.Contains('\\');
        bool local;
        if (rest.StartsWith("///", StringComparison.Ordinal))
        {
            rest = rest[3..];
            local = true;
        }
        else if (rest.StartsWith("//localhost/", StringComparison.OrdinalIgnoreCase))
        {
            rest = rest["//localhost/".Length..];
            local = true;
        }
        else if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            rest = rest[2..];
            local = false;
        }
        else
        {
            rest = rest.TrimStart('/');
            local = true;
        }

        if (escaped)
        {
            try
            {
                rest = Uri.UnescapeDataString(rest);
            }
            catch (UriFormatException)
            {
                // Left as written.
            }
        }

        // "C:/Music" is a Windows drive. "file:///Users/me" is a Mac or Linux path, and anything else after
        // "file://" is a network share, "file://server/share/song.mp3".
        if (rest.Length >= 2 && char.IsLetter(rest[0]) && rest[1] == ':')
        {
            return rest.Replace('/', '\\');
        }

        if (local)
        {
            return OperatingSystem.IsWindows() ? "\\" + rest.Replace('/', '\\') : "/" + rest;
        }

        return OperatingSystem.IsWindows() ? @"\\" + rest.Replace('/', '\\') : "//" + rest;
    }

    /// <summary>A path from a playlist file, which may be relative to the playlist's folder.</summary>
    public static string? Resolve(string? text, string playlistFolder)
    {
        if (ToPath(text) is not { } path)
        {
            return null;
        }

        try
        {
            if (IsWindowsAbsolute(path) || path.StartsWith('/') || Path.IsPathRooted(path))
            {
                return path;
            }

            return Path.GetFullPath(Path.Combine(playlistFolder, path.Replace('\\', Path.DirectorySeparatorChar)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static bool IsWindowsAbsolute(string path) =>
        (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/') || path.StartsWith(@"\\", StringComparison.Ordinal);
}
