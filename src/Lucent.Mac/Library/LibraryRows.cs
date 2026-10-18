using System.ComponentModel;
using WinampInspiredVisualizer.Library;

namespace Lucent.Mac.Library;

/// <summary>
/// A folder heading in the library list (or a playlist's heading under PLAYLISTS), standing in for WPF's
/// CollectionViewGroup: the list is flat, a heading row followed by its songs while it's open.
/// </summary>
public sealed class FolderHeading : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isHighlighted;

    public FolderHeading(string name, bool isExpanded)
    {
        Name = name;
        _isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>The songs under the heading, in the list's order.</summary>
    public List<LibraryItem> Items { get; } = new();

    public int ItemCount => Items.Count;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    /// <summary>Highlighted with ⌘-click or Shift-click, to act on several headings at once.</summary>
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted != value)
            {
                _isHighlighted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
            }
        }
    }
}
