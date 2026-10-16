using System.ComponentModel;

namespace WinampInspiredVisualizer.Playlist;

/// <summary>A playlist row: the track plus whether it is the one currently loaded.</summary>
public sealed class PlaylistEntry : INotifyPropertyChanged
{
    private bool _isCurrent;

    public PlaylistEntry(Track track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;

    public Track Track { get; }

    public string ArtistSuffix => string.IsNullOrEmpty(Track.Artist) ? string.Empty : $"  ·  {Track.Artist}";

    public string Indicator => _isCurrent ? "▶" : string.Empty;

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
