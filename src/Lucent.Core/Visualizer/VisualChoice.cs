using System.ComponentModel;
using WinampInspiredVisualizer.Library;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A row in the visual picker: one visual, and whether it's starred.</summary>
public sealed class VisualChoice : INotifyPropertyChanged
{
    private bool _isFavorite;

    public VisualChoice(string name, int index, string family, IReadOnlyList<string> tags, string? author = null)
    {
        Name = name;
        Index = index;
        Family = family;
        Tags = tags;
        Author = author;
        SearchKey = SearchText.Key(new[] { name, family, author ?? string.Empty }.Concat(tags));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>Its position among all the visuals, as the visualizer numbers them.</summary>
    public int Index { get; }

    /// <summary>The folder it's listed in, such as "Fractal".</summary>
    public string Family { get; }

    /// <summary>Its tags, such as "Calm" and "Space", in the order the picker lists tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>A MilkDrop preset, listed in the MilkDrop folder.</summary>
    public bool IsMilkDrop => Family == VisualStyles.MilkDropFamily;

    /// <summary>A Winamp AVS preset, listed in the Winamp AVS folder.</summary>
    public bool IsAvs => Family == VisualStyles.AvsFamily;

    /// <summary>Who made it (MilkDrop and AVS presets), or null.</summary>
    public string? Author { get; }

    /// <summary>When it joined the visuals, for the picker's "Newest first" order, or null if that isn't known.</summary>
    public DateTime? Added { get; init; }

    /// <summary>The name with its author, "Reaction Diffusion by Geiss", for the name shown over the visuals.</summary>
    public string Label => PresetSource.LabelFor(Name, Author);

    /// <summary>Its name, family and tags, ready for the picker's search box to look in.</summary>
    public string SearchKey { get; }

    /// <summary>"Fractal · Calm, Geometric", shown when the mouse rests on the visual in the picker (without a tag that just repeats the family).</summary>
    public string StyleText
    {
        get
        {
            var tags = Tags.Where(tag => !string.Equals(tag, Family, StringComparison.OrdinalIgnoreCase)).ToList();
            return (Author is null ? Family : $"{Family} · by {Author}") + (tags.Count == 0 ? "" : $" · {string.Join(", ", tags)}");
        }
    }

    public bool IsFavorite
    {
        get => _isFavorite;
        set
        {
            if (_isFavorite != value)
            {
                _isFavorite = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFavorite)));
            }
        }
    }

    /// <summary>"412 visuals", or "37 of 412" when a search or tags narrow the list, for the picker's count.</summary>
    public static string CountText(int shown, int total) =>
        shown == total ? $"{total} visual{(total == 1 ? "" : "s")}" : $"{shown} of {total}";

    public override string ToString() => Name;
}
