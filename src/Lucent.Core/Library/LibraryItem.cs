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

    public LibraryItem(string path, string folder, string directory, DateTime added)
    {
        Path = path;
        Folder = folder;
        Directory = directory;
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

    /// <summary>When the file arrived: the later of its created and modified times.</summary>
    public DateTime Added { get; }

    public string Title => _title;

    public string Artist => _artist;

    public string Album => Track?.Album ?? string.Empty;

    public string DurationText => _durationText;

    public string Details => IsPlaceholder ? string.Empty : string.IsNullOrEmpty(_artist)
        ? Added.ToString("d")
        : $"{_artist}  ·  {Added:d}";

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

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
