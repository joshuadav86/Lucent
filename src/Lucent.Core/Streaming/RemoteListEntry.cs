using System.ComponentModel;
using Lucent.Library;

namespace Lucent.Streaming;

/// <summary>A row of another app's playlist in the queue window or the library: the song, and whether it's the one playing.</summary>
public sealed class RemoteListEntry : INotifyPropertyChanged
{
    private bool _isCurrent;
    private IReadOnlyList<LibraryCell> _cells = Array.Empty<LibraryCell>();

    public RemoteListEntry(RemoteTrack track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RemoteTrack Track { get; }

    /// <summary>The columns beside the song when its playlist is opened in the library (see <see cref="PlaylistColumns"/>).</summary>
    public IReadOnlyList<LibraryCell> Cells { get => _cells; init => _cells = value; }

    /// <summary>The row's cells at new widths, while a column is dragged wider or narrower.</summary>
    public void Resize(IReadOnlyList<double> widths)
    {
        if (Cells.Count != widths.Count || Cells.Select(cell => cell.Width).SequenceEqual(widths))
        {
            return;
        }

        _cells = Cells.Select((cell, index) => cell with { Width = widths[index] }).ToList();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cells)));
    }

    public bool HasCells => Cells.Count > 0;

    /// <summary>The row as a classic skin's playlist shows it: "Artist - Title".</summary>
    public string ClassicText => string.IsNullOrEmpty(Track.Artist) ? Track.Title : $"{Track.Artist} - {Track.Title}";

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
        }
    }
}
