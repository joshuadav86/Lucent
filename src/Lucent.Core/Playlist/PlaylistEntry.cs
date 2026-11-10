using System.ComponentModel;
using Lucent.Library;

namespace Lucent.Playlist;

/// <summary>A playlist row: the track plus whether it is the one currently loaded.</summary>
public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private bool _isCurrent;
    private string? _searchKey;

    public PlaylistEntry(Track track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The track's file and tags. Replaced by <see cref="Refresh"/> when its info is edited.</summary>
    public Track Track { get; private set; }

    public string ArtistSuffix => string.IsNullOrEmpty(Track.Artist) ? string.Empty : $"  ·  {Track.Artist}";

    public string Indicator => _isCurrent ? "▶" : string.Empty;

    /// <summary>The row as a classic skin's playlist shows it: "Artist - Title".</summary>
    public string ClassicText => string.IsNullOrEmpty(Track.Artist) ? Track.Title : $"{Track.Artist} - {Track.Title}";

    /// <summary>What the queue's search looks in: the title, artist, album, album artist, genre and file name.</summary>
    public string SearchKey => _searchKey ??= SearchText.Key(new[]
    {
        Track.Title, Track.Artist, Track.Album, Track.Tags.AlbumArtist, Track.Tags.Genre,
        Track.IsStreamed ? null : System.IO.Path.GetFileNameWithoutExtension(Track.Path),
    });

    /// <summary>
    /// Shows freshly read tags for the same file, after its info was edited. The row itself stays, so it
    /// keeps its place in the queue and, if it's the one playing, carries on being the current track.
    /// </summary>
    public void Refresh(Track track)
    {
        Track = track;
        _searchKey = null;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Track)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ArtistSuffix)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClassicText)));
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent == value)
            {
                return;
            }

            _isCurrent = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrent)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Indicator)));
        }
    }
}
