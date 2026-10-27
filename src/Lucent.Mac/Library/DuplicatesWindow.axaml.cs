using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using Lucent.Library;
using Lucent.Settings;

namespace Lucent.Mac;

/// <summary>
/// Find duplicates: the songs in the library more than once (<see cref="DuplicateFinder"/>), each copy with a
/// tick. The best copy of each starts unticked and the rest ticked; the ticked ones go to the Trash, and their
/// plays, ratings, favourites and places in saved playlists move to the copy that's kept.
/// </summary>
public partial class DuplicatesWindow : Window
{
    private MusicLibrary _library = null!;
    private AppSettings _settings = null!;
    private MainWindow? _player;
    private List<DuplicateGroup> _groups = new();

    public DuplicatesWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
    }

    /// <summary>Looks through the library and shows what it found.</summary>
    public void Show(Window owner, MusicLibrary library, AppSettings settings, MainWindow? player)
    {
        Prepare(library, settings, player);
        Show(owner);
    }

    /// <summary>Looks through the library, without showing the window yet (the screenshot tool draws it).</summary>
    internal void Prepare(MusicLibrary library, AppSettings settings, MainWindow? player)
    {
        _library = library;
        _settings = settings;
        _player = player;
        Find();
    }

    private void Find()
    {
        foreach (var row in _groups.SelectMany(group => group.Rows))
        {
            row.PropertyChanged -= OnRowChanged;
        }

        _groups = DuplicateFinder.Find(_library.Items);
        foreach (var row in _groups.SelectMany(group => group.Rows))
        {
            row.PropertyChanged += OnRowChanged;
        }

        GroupList.ItemsSource = _groups;
        var copies = _groups.Sum(group => group.Rows.Count);
        CountText.Text = _groups.Count == 0 ? string.Empty
            : $"{_groups.Count:N0} {(_groups.Count == 1 ? "song" : "songs")}  ·  {copies:N0} files";
        var stillReading = _library.IsScanning ? " The library is still reading tags, so look again when it's done." : string.Empty;
        SummaryText.Text = _groups.Count == 0
            ? $"No duplicates found. Songs count as the same when the artist, title, and length match.{stillReading}"
            : $"Songs with the same artist, title, and length. Ticked copies go to the {Trash.Name}, and their plays, ratings, and favorites move to the copy you keep.{stillReading}";
        UpdateRemoveButton();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e) => UpdateRemoveButton();

    private void UpdateRemoveButton()
    {
        var count = _groups.Sum(group => group.Rows.Count(row => row.Remove));
        RemoveButton.Content = count == 0 ? $"Move to {Trash.Name}" : $"Move {count:N0} to {Trash.Name}";
        RemoveButton.IsEnabled = count > 0;
        RemoveButton.IsVisible = _groups.Count > 0;
    }

    private async void OnRemove(object? sender, RoutedEventArgs e)
    {
        var removal = DuplicateFinder.Plan(_groups);
        if (removal.Remove.Count == 0)
        {
            return;
        }

        var none = _groups.Count(group => group.Rows.All(row => row.Remove));
        var warning = none == 0 ? string.Empty
            : $"\n\n{(none == 1 ? "One song has" : $"{none} songs have")} every copy ticked, so no copy of {(none == 1 ? "it" : "them")} will be left.";
        var answer = await MessageWindow.ShowAsync(this,
            $"Move {removal.Remove.Count:N0} {(removal.Remove.Count == 1 ? "file" : "files")} to the {Trash.Name}?{warning}",
            "Duplicates", MessageButtons.YesNo, MessageResult.No);
        if (answer != MessageResult.Yes)
        {
            return;
        }

        IsEnabled = false;
        var favorites = _settings.FavoriteTracks.ToList();
        var (moved, newFavorites) = await Task.Run(() =>
        {
            var moved = Trash.MoveToTrash(removal.Remove);
            return (moved, DuplicateFinder.CarryOver(removal, moved, favorites));
        });
        if (_player is not null)
        {
            _player.SetFavoriteTracks(newFavorites, favorite: true);
            _player.SetFavoriteTracks(moved.Where(path => favorites.Contains(path, StringComparer.OrdinalIgnoreCase)), favorite: false);
        }

        await _library.RescanAsync();
        IsEnabled = true;
        Find();
        var failed = removal.Remove.Count - moved.Count;
        if (failed > 0)
        {
            await MessageWindow.ShowAsync(this,
                $"{failed:N0} {(failed == 1 ? "file" : "files")} couldn't be moved to the {Trash.Name}. They may be open in another app, or read-only.",
                "Duplicates");
        }
    }

    private void OnCopyDoubleClick(object? sender, TappedEventArgs e)
    {
        if ((sender as Control)?.DataContext is DuplicateCopy copy)
        {
            // Double-clicking plays the copy, to compare them by ear. Its two clicks leave the tick as it was.
            e.Handled = true;
            _player?.PlayFromLibrary(copy.Item);
        }
    }

    private void OnPlayCopy(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DuplicateCopy copy)
        {
            _player?.PlayFromLibrary(copy.Item);
        }
    }

    private void OnShowCopy(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.DataContext is DuplicateCopy copy)
        {
            _player?.ShowInExplorer(copy.Item.Path);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
