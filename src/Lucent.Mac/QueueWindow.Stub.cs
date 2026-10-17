// STUB: owned by library worker
using Avalonia.Controls;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Playlist;

namespace Lucent.Mac;

public partial class QueueWindow : ToolWindow
{
    public QueueWindow()
    {
        Title = "Lucent Queue";
        PlacementName = "Queue";
        Width = 520;
        Height = 320;
        MinWidth = 300;
        MinHeight = 160;
        Content = new Panel { Children = { PlaylistView, EmptyPlaylistHint } };
    }

    public ListBox PlaylistView { get; } = new();

    public Control EmptyPlaylistHint { get; } = new StackPanel();

    public TextBlock QueueCount { get; } = new();

    public List<PlaylistEntry> SelectedEntries() => PlaylistView.SelectedItems?.OfType<PlaylistEntry>().ToList() ?? new();
}
