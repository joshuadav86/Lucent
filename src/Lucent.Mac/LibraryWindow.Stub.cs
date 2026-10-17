// STUB: owned by library worker
using Avalonia.Controls;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

public partial class LibraryWindow : ToolWindow
{
    public LibraryWindow()
    {
        Title = "Lucent Library";
        PlacementName = "Library";
        Width = 360;
        Height = 520;
        MinWidth = 260;
        MinHeight = 200;
        Content = LibraryView;
    }

    public ListBox LibraryView { get; } = new();

    public IEnumerable<string> CollapsedFolders => Array.Empty<string>();

    public void Attach(MusicLibrary library, AppSettings settings)
    {
    }

    public void PlaylistsChanged()
    {
    }

    public void FavoritesChanged()
    {
    }

    public List<LibraryItem> SelectedLibraryItems() => new();
}
