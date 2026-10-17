using System.ComponentModel;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>A row in the visual picker: one visual, and whether it's starred.</summary>
public sealed class VisualChoice : INotifyPropertyChanged
{
    private bool _isFavorite;

    public VisualChoice(string name, int index)
    {
        Name = name;
        Index = index;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>Its position among all the visuals, as the visualizer numbers them.</summary>
    public int Index { get; }

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
