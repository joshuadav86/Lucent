using System.ComponentModel;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>A row of another app's playlist in the queue window: the song, and whether it's the one playing.</summary>
public sealed class RemoteListEntry : INotifyPropertyChanged
{
    private bool _isCurrent;

    public RemoteListEntry(RemoteTrack track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;

    public RemoteTrack Track { get; }

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
