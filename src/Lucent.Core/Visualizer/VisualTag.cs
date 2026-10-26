using System.ComponentModel;

namespace Lucent.Visualizer;

/// <summary>A tick box in the visual picker: one tag, whether it's ticked, and how many visuals have it.</summary>
public sealed class VisualTag : INotifyPropertyChanged
{
    private bool _isChecked;
    private int _count;

    public VisualTag(string name)
    {
        Name = name;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value, nameof(IsChecked));
    }

    /// <summary>How many of the visuals in the chosen list have this tag.</summary>
    public int Count
    {
        get => _count;
        set => Set(ref _count, value, nameof(Count));
    }

    private void Set<T>(ref T field, T value, string property)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }
}
