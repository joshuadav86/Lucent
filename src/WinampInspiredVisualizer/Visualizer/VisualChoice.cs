using System.ComponentModel;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A row in the visual picker: one visual, and whether it's starred.</summary>
public sealed class VisualChoice : INotifyPropertyChanged
{
    private bool _isFavorite;

    public VisualChoice(string name, int index, string family, IReadOnlyList<string> tags)
    {
        Name = name;
        Index = index;
        Family = family;
        Tags = tags;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>Its position among all the visuals, as the visualizer numbers them.</summary>
    public int Index { get; }

    /// <summary>The folder it's listed in, such as "Fractal".</summary>
    public string Family { get; }

    /// <summary>Its tags, such as "Calm" and "Space", in the order the picker lists tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>"Fractal · Calm, Geometric", shown when the mouse rests on the visual in the picker.</summary>
    public string StyleText => Tags.Count == 0 ? Family : $"{Family} · {string.Join(", ", Tags)}";

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

    public override string ToString() => Name;
}
