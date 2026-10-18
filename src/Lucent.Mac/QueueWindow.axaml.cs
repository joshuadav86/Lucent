using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Playlist;

namespace Lucent.Mac;

/// <summary>The queue: what's playing and what plays next. The player window does the work.</summary>
public partial class QueueWindow : ToolWindow
{
    public QueueWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        PlacementName = "Queue";

        // Runs before the list's menu opens, to fill in the items that depend on what's selected.
        PlaylistView.AddHandler(ContextRequestedEvent, OnQueueMenuOpening, RoutingStrategies.Tunnel);
    }

    /// <summary>The selected rows, in queue order.</summary>
    public List<PlaylistEntry> SelectedEntries()
    {
        var selected = new HashSet<PlaylistEntry>(PlaylistView.SelectedItems?.OfType<PlaylistEntry>() ?? Enumerable.Empty<PlaylistEntry>());
        return PlaylistView.Items.OfType<PlaylistEntry>().Where(selected.Contains).ToList();
    }

    private void OnPlaylistsMenu(object? sender, RoutedEventArgs e)
    {
        if (Player is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = PlaylistsButton, Placement = PlacementMode.Bottom };
        Player.FillPlaylistsMenu(menu, this);
        menu.Open(PlaylistsButton);
    }

    // Favourite and Add to playlist depend on what's selected, so they're made fresh each time.
    private readonly List<Control> _songMenuItems = new();

    private void OnQueueMenuOpening(object? sender, ContextRequestedEventArgs e)
    {
        foreach (var item in _songMenuItems)
        {
            QueueMenu.Items.Remove(item);
        }

        _songMenuItems.Clear();
        var entries = SelectedEntries();
        if (Player is null || entries.Count == 0)
        {
            // Nothing to act on: no menu.
            e.Handled = true;
            return;
        }

        var tracks = entries.Select(entry => entry.Track).ToList();
        _songMenuItems.Add(Player.FavoriteMenuItem(tracks.Select(track => track.Path).ToList()));
        _songMenuItems.Add(Player.AddToPlaylistMenu(this, () => Task.FromResult(tracks)));
        _songMenuItems.Add(new Separator());
        var at = QueueMenu.Items.IndexOf(SongMenuItemsStart) + 1;
        foreach (var item in _songMenuItems)
        {
            QueueMenu.Items.Insert(at++, item);
        }
    }

    private void OnAddFiles(object? sender, RoutedEventArgs e) => Player?.AddFilesFromDialog(this);

    private void OnAddFolder(object? sender, RoutedEventArgs e) => Player?.AddFolderFromDialog(this);

    private void OnClearQueue(object? sender, RoutedEventArgs e) => Player?.ClearQueue();

    private void OnPlaylistDoubleClick(object? sender, TappedEventArgs e)
    {
        // Only rows count; double-clicking the empty space or the scroll bar does nothing.
        if ((e.Source as Control)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: PlaylistEntry entry })
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnPlaySelected(object? sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
        }
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e) => Player?.RemoveFromQueue(SelectedEntries());

    private void OnEditInfo(object? sender, RoutedEventArgs e) =>
        Player?.EditInfo(SelectedEntries().Select(entry => entry.Track.Path));

    private void OnOpenInEditor(object? sender, RoutedEventArgs e)
    {
        if (SelectedEntries().FirstOrDefault() is { } entry)
        {
            Player?.OpenInEditor(this, entry.Track.Path);
        }
    }

    private void OnConvert(object? sender, RoutedEventArgs e) =>
        Player?.ConvertFiles(this, SelectedEntries().Select(entry => entry.Track.Path).ToList());

    private void OnShowInExplorer(object? sender, RoutedEventArgs e)
    {
        if (PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.ShowInExplorer(entry.Track.Path);
        }
    }

    private void OnPlaylistKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PlaylistView.SelectedItem is PlaylistEntry entry)
        {
            Player?.PlayEntry(entry);
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            // The Mac's delete key sends Back; the forward-delete key sends Delete.
            Player?.RemoveFromQueue(SelectedEntries());
            e.Handled = true;
        }
    }
}
