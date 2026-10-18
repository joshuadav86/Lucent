using System.ComponentModel;
using WinampInspiredVisualizer.Playlist;

namespace WinampInspiredVisualizer.Library;

/// <summary>A music file found in a library folder. Shows its file name until its tags have been read.</summary>
public sealed class LibraryItem : INotifyPropertyChanged
{
    private string _title;
    private string _artist = string.Empty;
    private string _durationText = string.Empty;
    private string? _searchKey;

    public LibraryItem(string path, string folder, string directory, DateTime added, string? section = null,
        string? sectionDirectory = null)
    {
        Path = path;
        Folder = folder;
        Directory = directory;
        Section = section ?? folder;
        SectionDirectory = sectionDirectory ?? directory;
        Added = added;
        FileName = System.IO.Path.GetFileNameWithoutExtension(path);
        _title = FileName;
    }

    /// <summary>A row standing in for an empty playlist, so its heading still shows. It isn't a file.</summary>
    public static LibraryItem Placeholder(string folder, string text) =>
        new(string.Empty, folder, string.Empty, DateTime.MinValue) { _title = text };

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPlaceholder => Path.Length == 0;

    public string Path { get; }

    public string FileName { get; }

    /// <summary>The folder heading this file is listed under, relative to its library folder.</summary>
    public string Folder { get; }

    /// <summary>The full path of the folder the file is in.</summary>
    public string Directory { get; }

    /// <summary>
    /// The top heading the file is listed under: its <see cref="Folder"/>, or a store's one heading ("Bandcamp")
    /// that its folder is a subheading of.
    /// </summary>
    public string Section { get; }

    /// <summary>The full path of the folder behind <see cref="Section"/>.</summary>
    public string SectionDirectory { get; }

    /// <summary>Whether <see cref="Folder"/> is a subheading under <see cref="Section"/>, not a heading of its own.</summary>
    public bool IsInSection => !string.Equals(Section, Folder, StringComparison.Ordinal);

    /// <summary>The subheading's name, without the store's in front: "Artist / Album".</summary>
    public string SubFolder => IsInSection && Folder.StartsWith(Section + " / ", StringComparison.Ordinal)
        ? Folder[(Section.Length + 3)..]
        : Folder;

    /// <summary>When the file arrived: the later of its created and modified times.</summary>
    public DateTime Added { get; }

    public string Title => _title;

    public string Artist => _artist;

    public string Album => Track?.Album ?? string.Empty;

    public string DurationText => _durationText;

    public string Details => IsPlaceholder ? string.Empty : string.IsNullOrEmpty(_artist)
        ? $"{Added:d}{StatsText}"
        : $"{_artist}  ·  {Added:d}{StatsText}";

    /// <summary>How many times it's been played, here and in players it was imported from.</summary>
    public int Plays => IsPlaceholder ? 0 : TrackStats.Shared.PlaysOf(Path);

    /// <summary>Stars, 1 to 5, or 0 for none.</summary>
    public int Rating => IsPlaceholder ? 0 : TrackStats.Shared.RatingOf(Path);

    public DateTime? LastPlayed => IsPlaceholder ? null : TrackStats.Shared.LastPlayedOf(Path);

    /// <summary>"  ·  ★★★★  ·  12 plays", leaving out what isn't known.</summary>
    private string StatsText
    {
        get
        {
            var rating = Rating;
            var plays = Plays;
            var text = rating > 0 ? $"  ·  {TrackStats.Stars(rating)}" : string.Empty;
            return plays > 0 ? $"{text}  ·  {plays:N0} {(plays == 1 ? "play" : "plays")}" : text;
        }
    }

    /// <summary>The track with its tags, once they have been read.</summary>
    public Track? Track { get; private set; }

    public bool HasTags => Track is not null;

    /// <summary>The title, artist, album and file name, ready for the library's search box to look in.</summary>
    public string SearchKey => _searchKey ??= SearchText.Key(new[] { _title, _artist, Album, FileName });

    public Track ToTrack() => Track ?? Track.FromFile(Path);

    public void ApplyTags(Track track)
    {
        Track = track;
        _title = track.Title;
        _artist = track.Artist;
        _durationText = track.DurationText;
        _searchKey = null;
        Raise(nameof(Title));
        Raise(nameof(Artist));
        Raise(nameof(DurationText));
        Raise(nameof(Details));
    }

    /// <summary>Shows a new play count or rating.</summary>
    public void StatsChanged() => Raise(nameof(Details));

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
