using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Lucent.Library;
using Lucent.Streaming;

namespace Lucent;

/// <summary>
/// The columns of a streaming playlist opened from PLAYLISTS: album, date added, year and the rest beside each song,
/// chosen, put in order and sorted by as the library's tag columns are (LibraryWindow.Columns.cs). Each app fills
/// what it knows, so a column it has nothing for is left out of its playlists.
/// </summary>
public partial class LibraryWindow
{
    // The columns the open playlist shows now: the settings' ones its app fills, in their order.
    private List<PlaylistColumn> _playlistShown = new();

    /// <summary>What the open playlist is sorted by: the settings' choice, or its own order when its app can't fill it.</summary>
    private PlaylistColumn ShownPlaylistSort =>
        _openRemoteList is { } list && PlaylistColumns.For(list).Contains(_settings.PlaylistSortColumn)
            ? _settings.PlaylistSortColumn
            : PlaylistColumn.Order;

    private bool ShownPlaylistDescending => _settings.PlaylistSortDescending && ShownPlaylistSort == _settings.PlaylistSortColumn;

    /// <summary>The open playlist's rows, sorted, with their columns' cells.</summary>
    private List<RemoteListEntry> PlaylistEntries(RemoteList? list)
    {
        if (list is null)
        {
            _playlistShown = new List<PlaylistColumn>();
            return new List<RemoteListEntry>();
        }

        var filled = PlaylistColumns.For(list);
        _playlistShown = _settings.PlaylistColumnsShown.Where(PlaylistColumns.Choosable.Contains).Where(filled.Contains).Distinct().ToList();
        return PlaylistColumns.Sort(list.Tracks, ShownPlaylistSort, ShownPlaylistDescending)
            .Select(song => new RemoteListEntry(song.Track) { Cells = PlaylistColumns.Cells(_playlistShown, song.Track, song.Order) })
            .ToList();
    }

    /// <summary>The headings over the open playlist's songs, and the sort button's label.</summary>
    private void UpdatePlaylistHeadings()
    {
        var songs = _openRemoteList?.Tracks.Count ?? 0;
        RemoteSortButton.Visibility = PlaylistColumnHeader.Visibility = songs > 1 ? Visibility.Visible : Visibility.Collapsed;
        RemoteSortLabel.Text = PlaylistColumns.SortLabel(ShownPlaylistSort, ShownPlaylistDescending);
        PlaylistColumnHeadings.Children.Clear();
        foreach (var column in _playlistShown)
        {
            var text = new TextBlock
            {
                Text = PlaylistHeadingText(column),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = PlaylistColumns.AlignRight(column) ? TextAlignment.Right : TextAlignment.Left,
            };
            var heading = new Border
            {
                Child = text,
                Width = PlaylistColumns.Width(column),
                Padding = new Thickness(8, 0, 8, 0),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = column,
                ToolTip = $"Sort by {PlaylistColumns.Label(column).ToLowerInvariant()}. Drag to move it.",
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, PlaylistHeadingBrush(column));
            heading.SetResourceReference(Border.BorderBrushProperty, "HairlineBrush");
            heading.MouseLeftButtonDown += OnHeadingMouseDown;
            heading.MouseMove += OnHeadingMouseMove;
            heading.MouseLeftButtonUp += OnHeadingMouseUp;
            heading.LostMouseCapture += (_, _) => EndHeadingDrag();
            PlaylistColumnHeadings.Children.Add(heading);
        }

        PlaylistTitleHeading.Text = PlaylistHeadingText(PlaylistColumn.Title);
        PlaylistTitleHeading.SetResourceReference(TextBlock.ForegroundProperty, PlaylistHeadingBrush(PlaylistColumn.Title));
        PlaylistLengthHeading.Text = PlaylistHeadingText(PlaylistColumn.Length);
        PlaylistLengthHeading.SetResourceReference(TextBlock.ForegroundProperty, PlaylistHeadingBrush(PlaylistColumn.Length));
    }

    /// <summary>The sorted column's heading has an arrow and stands out.</summary>
    private string PlaylistHeadingText(PlaylistColumn column) => ShownPlaylistSort == column
        ? $"{PlaylistColumns.Heading(column)} {(ShownPlaylistDescending ? "▼" : "▲")}"
        : PlaylistColumns.Heading(column);

    private string PlaylistHeadingBrush(PlaylistColumn column) => ShownPlaylistSort == column ? "AccentBrush" : "FaintTextBrush";

    /// <summary>
    /// Sorts the open playlist by a column: a second time on the same one flips it. Then back to the song playing, in
    /// its new place, or else to the top.
    /// </summary>
    private void SortPlaylistBy(PlaylistColumn column, bool flipIfSorted = true)
    {
        _settings.PlaylistSortDescending = ShownPlaylistSort == column && flipIfSorted
            ? !ShownPlaylistDescending
            : PlaylistColumns.DescendingFirst(column);
        _settings.PlaylistSortColumn = column;
        _settings.Save();
        if (_openRemoteList is { } list)
        {
            _remoteMarked = false;
            ShowRemoteSongs(list, list.Note);
            if (!_remoteMarked && _openRemoteEntries.Count > 0)
            {
                RemotePlaylistView.ScrollIntoView(_openRemoteEntries[0]);
            }
        }
    }

    private void OnPlaylistTitleHeadingClick(object sender, MouseButtonEventArgs e) => SortPlaylistBy(PlaylistColumn.Title);

    private void OnPlaylistLengthHeadingClick(object sender, MouseButtonEventArgs e) => SortPlaylistBy(PlaylistColumn.Length);

    /// <summary>The open playlist's rows again, after its columns changed.</summary>
    private void RelistPlaylist()
    {
        if (_openRemoteList is { } list)
        {
            ShowRemoteSongs(list, list.Note);
        }
    }

    /// <summary>Shows a column, or hides it. A new column goes on the end, and the window widens to fit it.</summary>
    private void TogglePlaylistColumn(PlaylistColumn column)
    {
        if (!_settings.PlaylistColumnsShown.Remove(column))
        {
            _settings.PlaylistColumnsShown.Add(column);
            WidenToFit(_playlistShown.Sum(PlaylistColumns.Width) + PlaylistColumns.Width(column) + TitleRoom);
        }

        _settings.Save();
        RelistPlaylist();
    }

    /// <summary>
    /// The first time a playlist with columns opens, the window widens once to fit them, as the library did for Genre
    /// and Year.
    /// </summary>
    private void WidenForPlaylistColumns()
    {
        if (!_settings.PlaylistColumnsFitted && _playlistShown.Count > 0)
        {
            _settings.PlaylistColumnsFitted = true;
            _settings.Save();
            WidenToFit(_playlistShown.Sum(PlaylistColumns.Width) + TitleRoom);
        }
    }

    /// <summary>
    /// Puts a column just after <paramref name="after"/>, or first when that's null. Columns the playlist's app doesn't
    /// fill keep their places in the settings, for the apps that do.
    /// </summary>
    private void MovePlaylistColumn(PlaylistColumn column, PlaylistColumn? after)
    {
        var columns = _settings.PlaylistColumnsShown;
        if (after == column || !columns.Remove(column))
        {
            return;
        }

        columns.Insert(after is { } other ? columns.IndexOf(other) + 1 : 0, column);
        _settings.Save();
        RelistPlaylist();
    }

    /// <summary>A dragged heading goes where the mouse was let go: after every other column whose middle is to the left of it.</summary>
    private void DropPlaylistHeading(PlaylistColumn column, double x)
    {
        PlaylistColumn? after = null;
        var left = 0.0;
        foreach (var shown in _playlistShown)
        {
            var width = PlaylistColumns.Width(shown);
            if (shown != column && x > left + width / 2)
            {
                after = shown;
            }

            left += width;
        }

        MovePlaylistColumn(column, after);
    }

    /// <summary>A tick for each column the open playlist's app can fill.</summary>
    private void AddPlaylistColumnChoices(ItemCollection items)
    {
        if (_openRemoteList is not { } list)
        {
            return;
        }

        foreach (var column in PlaylistColumns.For(list).Where(PlaylistColumns.Choosable.Contains))
        {
            var item = new MenuItem
            {
                Header = PlaylistColumns.Label(column),
                IsCheckable = true,
                IsChecked = _playlistShown.Contains(column),
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) => TogglePlaylistColumn(column);
            items.Add(item);
        }
    }

    /// <summary>The sort button: the playlist's own order, or any column its app fills, and the columns to show.</summary>
    private void OnRemoteSortMenu(object sender, RoutedEventArgs e)
    {
        if (_openRemoteList is not { } list)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = RemoteSortButton, Placement = PlacementMode.Bottom };
        foreach (var column in PlaylistColumns.For(list))
        {
            var item = new MenuItem { Header = PlaylistColumns.Label(column), IsChecked = ShownPlaylistSort == column };
            item.Click += (_, _) => SortPlaylistBy(column, flipIfSorted: false);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var columns = new MenuItem { Header = "Columns" };
        AddPlaylistColumnChoices(columns.Items);
        menu.Items.Add(columns);
        menu.IsOpen = true;
    }

    /// <summary>The headings' right-click menu: move or hide the column under the mouse, and choose the columns.</summary>
    private void OnPlaylistColumnHeaderMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var menu = PlaylistColumnHeader.ContextMenu!;
        menu.Items.Clear();
        if (Ancestor<Border>(e.OriginalSource as DependencyObject) is { Tag: PlaylistColumn column })
        {
            var index = _playlistShown.IndexOf(column);
            var left = new MenuItem { Header = "Move left", IsEnabled = index > 0 };
            left.Click += (_, _) => MovePlaylistColumn(column, index >= 2 ? _playlistShown[index - 2] : null);
            var right = new MenuItem { Header = "Move right", IsEnabled = index < _playlistShown.Count - 1 };
            right.Click += (_, _) => MovePlaylistColumn(column, _playlistShown[index + 1]);
            var hide = new MenuItem { Header = $"Hide {PlaylistColumns.Label(column)}" };
            hide.Click += (_, _) => TogglePlaylistColumn(column);
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(hide);
            menu.Items.Add(new Separator());
        }

        AddPlaylistColumnChoices(menu.Items);
    }
}
