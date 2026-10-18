using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using WinampInspiredVisualizer.Library;

namespace Lucent.Mac.Library;

/// <summary>
/// A folder heading in the library list (or a playlist's heading under PLAYLISTS), standing in for WPF's
/// CollectionViewGroup: the list is flat, a heading row followed by its songs while it's open. A store's
/// heading (Bandcamp, Beatport) is followed by a subheading for each folder inside it instead.
/// </summary>
public sealed class FolderHeading : INotifyPropertyChanged
{
    private bool _isExpanded;
    private bool _isHighlighted;

    public FolderHeading(string name, bool isExpanded, string? label = null, bool isSubheading = false)
    {
        Name = name;
        Label = label ?? name;
        IsSubheading = isSubheading;
        _isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>What the heading is known by (for remembering it's closed, and highlighting): its whole folder label.</summary>
    public string Name { get; }

    /// <summary>What it shows: the name, or for a subheading just the folder inside the store's ("Artist / Album").</summary>
    public string Label { get; }

    public bool IsSubheading { get; }

    /// <summary>Subheadings are set in under their store's heading.</summary>
    public Thickness Indent => IsSubheading ? new Thickness(14, 0, 0, 0) : default;

    public FontWeight LabelWeight => IsSubheading ? FontWeight.Normal : FontWeight.SemiBold;

    /// <summary>The songs under the heading, in the list's order (through its subheadings, for a store's).</summary>
    public List<LibraryItem> Items { get; } = new();

    /// <summary>A store's folders, each a subheading; empty for any other heading.</summary>
    public List<FolderHeading> Subheadings { get; } = new();

    /// <summary>The rows under the heading while it's open: its songs, or its subheadings and their open songs.</summary>
    public IEnumerable<object> OpenRows()
    {
        if (Subheadings.Count == 0)
        {
            return Items;
        }

        // Any songs right in the store's folder come first, then each subheading.
        return Items.Where(item => !item.IsInSection)
            .Concat(Subheadings.SelectMany(sub => sub.IsExpanded ? sub.Items.Prepend<object>(sub) : new object[] { sub }));
    }

    /// <summary>The folder on disk behind the heading: a store's own folder for its heading, or the songs' folder.</summary>
    public string? Directory => Items.FirstOrDefault() is { } first
        ? string.Equals(Name, first.Section, StringComparison.Ordinal) ? first.SectionDirectory : first.Directory
        : null;

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
