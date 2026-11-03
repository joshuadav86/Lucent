using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using Lucent.Library;

namespace Lucent.Mac;

/// <summary>
/// The library's tag columns: genre, year, BPM and the rest beside each song, chosen and put in order by the
/// user, with headings that sort by them. Sorting by any tag also works from the sort menu with no columns shown.
/// </summary>
public partial class LibraryWindow
{
    // The space the title needs beside the columns; adding a column widens the window to keep it.
    private const double TitleRoom = 330;

    // How far the mouse moves before a pressed heading is dragged rather than clicked.
    private const double HeadingDragDistance = 4;

    // A heading being pressed or dragged to a new place.
    private Border? _pressedHeading;
    private Point _headingPressPoint;
    private bool _draggingHeading;

    // The lines between the headings, which resize the columns either side of them.
    private ColumnGrips? _columnGrips;

    private ColumnGrips ColumnGrips => _columnGrips ??= new ColumnGrips(ColumnHeader,
        () => LibraryColumns.Shown.Select(LibraryColumns.Width).ToList(), ResizeColumns, FitColumn, () => _settings.Save());

    /// <summary>
    /// Shows the settings' columns: their headings, and the cells in every row. The saved playlists' songs under
    /// PLAYLISTS show the same columns, sorted inside each playlist (SortPlaylistsByColumn).
    /// </summary>
    private void ApplyColumns()
    {
        ColumnWidths.Custom = _settings.ColumnWidths ??= new();
        var shown = _settings.LibraryColumnsShown.Where(LibraryColumns.Choosable.Contains).Distinct().ToList();
        if (!shown.SequenceEqual(LibraryColumns.Shown))
        {
            LibraryColumns.Shown = shown;
            foreach (var item in _library.Items.Concat(_playlistItems))
            {
                item.ColumnsChanged();
            }
        }

        UpdateColumnHeadings();
    }

    private void UpdateColumnHeadings()
    {
        // Under PLAYLISTS, only over saved playlists' songs; an opened streaming playlist has its own headings.
        ColumnHeader.IsVisible = LibraryColumns.Shown.Count > 0 && !ShowingRemotePlaylist
            && (!ShowingPlaylists || _savedPlaylistItems.Any(item => !item.IsPlaceholder));
        ColumnHeadings.Children.Clear();
        foreach (var column in LibraryColumns.Shown)
        {
            var text = new TextBlock
            {
                Text = HeadingText(column),
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = LibraryColumns.AlignRight(column) ? TextAlignment.Right : TextAlignment.Left,
            };
            var heading = new Border
            {
                Child = text,
                Width = LibraryColumns.Width(column),
                Padding = new Thickness(8, 0, 8, 0),
                BorderThickness = new Thickness(1, 0, 0, 0),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = column,
            };
            ToolTip.SetTip(heading, ShowingPlaylists
                ? $"Sort each playlist by {LibraryColumns.Label(column).ToLowerInvariant()}. A third click puts it back in order. {MoveOrResize}"
                : $"Sort by {LibraryColumns.Label(column).ToLowerInvariant()}. {MoveOrResize}");
            text.SetResourceReference(TextBlock.ForegroundProperty, HeadingBrush(column));
            heading.SetResourceReference(Border.BorderBrushProperty, "HairlineBrush");
            heading.PointerPressed += OnHeadingPointerPressed;
            heading.PointerMoved += OnHeadingPointerMoved;
            heading.PointerReleased += OnHeadingPointerReleased;
            heading.PointerCaptureLost += (_, _) => EndHeadingDrag();
            ColumnHeadings.Children.Add(heading);
        }

        TitleHeading.Text = HeadingText(LibraryColumn.Title);
        TitleHeading.SetResourceReference(TextBlock.ForegroundProperty, HeadingBrush(LibraryColumn.Title));
        LengthHeading.Text = HeadingText(LibraryColumn.Length);
        LengthHeading.SetResourceReference(TextBlock.ForegroundProperty, HeadingBrush(LibraryColumn.Length));
        ColumnGrips.Place();
    }

    // The end of each heading's tooltip.
    private const string MoveOrResize = "Drag to move it, or drag the line beside it to resize it.";

    /// <summary>Keeps new widths for the library's columns and shows them.</summary>
    private void ResizeColumns(double[] widths)
    {
        foreach (var (column, width) in LibraryColumns.Shown.Zip(widths))
        {
            _settings.ColumnWidths[column.ToString()] = width;
        }

        ShowColumnWidths();
    }

    /// <summary>Shows the library's columns at their widths: their headings, and the cells in every row.</summary>
    private void ShowColumnWidths()
    {
        foreach (var heading in ColumnHeadings.Children.OfType<Border>())
        {
            if (heading.Tag is LibraryColumn column)
            {
                heading.Width = LibraryColumns.Width(column);
            }
        }

        foreach (var item in _library.Items.Concat(_playlistItems))
        {
            item.ColumnsChanged();
        }

        ColumnGrips.Place();
    }

    /// <summary>A double-click on a line: the column is made as wide as its widest text, heading included.</summary>
    private void FitColumn(int index)
    {
        if (index >= LibraryColumns.Shown.Count)
        {
            return;
        }

        var column = LibraryColumns.Shown[index];
        IEnumerable<LibraryItem> rows = ShowingPlaylists ? _playlistItems : _library.Items;
        var widest = rows.Select(item => LibraryColumns.Text(column, item)).Where(text => text.Length > 0).Distinct()
            .Select(text => TextWidth(text, 12)).Append(TextWidth(HeadingText(column), 11)).Max();
        var widths = LibraryColumns.Shown.Select(LibraryColumns.Width).ToArray();
        widths[index] = ColumnWidths.Fit(widths, index, widest, ColumnGrips.Room);
        ResizeColumns(widths);
        _settings.Save();
    }

    /// <summary>Puts the shown columns back at their standard widths.</summary>
    private void ResetColumnWidths(IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            _settings.ColumnWidths.Remove(name);
        }

        _settings.Save();
        ShowColumnWidths();
        ShowPlaylistColumnWidths();
    }

    /// <summary>"Reset column widths", when any of <paramref name="names"/> has been dragged to a new width.</summary>
    private void AddResetWidths(ItemCollection items, IReadOnlyList<string> names)
    {
        if (names.Any(_settings.ColumnWidths.ContainsKey))
        {
            var reset = new MenuItem { Header = "Reset column widths" };
            reset.Click += (_, _) => ResetColumnWidths(names);
            items.Add(reset);
            items.Add(new Separator());
        }
    }

    /// <summary>How wide a row's text is in the window's font.</summary>
    private double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), size, null)
            .WidthIncludingTrailingWhitespace;

    /// <summary>What the headings show as sorted: the library's sort, or under PLAYLISTS the playlists' own.</summary>
    private (LibraryColumn? Column, bool Descending) HeadingSort => ShowingPlaylists
        ? (_settings.SavedPlaylistSortColumn, _settings.SavedPlaylistSortDescending)
        : (_settings.LibrarySortColumn, _settings.LibrarySortDescending);

    /// <summary>The sorted column's heading has an arrow and stands out.</summary>
    private string HeadingText(LibraryColumn column) => HeadingSort.Column == column
        ? $"{LibraryColumns.Heading(column)} {(HeadingSort.Descending ? "▼" : "▲")}"
        : LibraryColumns.Heading(column);

    private string HeadingBrush(LibraryColumn column) => HeadingSort.Column == column ? "AccentBrush" : "FaintTextBrush";

    /// <summary>Sorts by a column: a second time on the same one flips it.</summary>
    private void SortByColumn(LibraryColumn column, bool flipIfSorted = true)
    {
        if (ShowingPlaylists)
        {
            SortPlaylistsByColumn(column);
            return;
        }

        _settings.LibrarySortDescending = _settings.LibrarySortColumn == column && flipIfSorted
            ? !_settings.LibrarySortDescending
            : LibraryColumns.DescendingFirst(column);
        _settings.LibrarySortColumn = column;
        _settings.Save();
        SortLabel.Text = LibraryItemComparer.LabelFor(_settings);
        UpdateColumnHeadings();
        RebuildLibraryView();
    }

    /// <summary>
    /// Sorts the songs inside each saved playlist by a column: a second click flips it, and a third puts them back in
    /// the playlist's order. Only the list changes, never the playlist.
    /// </summary>
    private void SortPlaylistsByColumn(LibraryColumn column)
    {
        (_settings.SavedPlaylistSortColumn, _settings.SavedPlaylistSortDescending) =
            SavedPlaylistOrder.Next(_settings.SavedPlaylistSortColumn, _settings.SavedPlaylistSortDescending, column);
        _settings.Save();
        UpdateColumnHeadings();
        RebuildLibraryView();
    }

    private void OnTitleHeadingClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            SortByColumn(LibraryColumn.Title);
        }
    }

    private void OnLengthHeadingClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            SortByColumn(LibraryColumn.Length);
        }
    }

    /// <summary>Shows a column, or hides it. A new column goes on the end, and the window widens to fit it.</summary>
    private void ToggleColumn(LibraryColumn column)
    {
        if (!_settings.LibraryColumnsShown.Remove(column))
        {
            _settings.LibraryColumnsShown.Add(column);
            WidenToFit(LibraryColumns.ShownWidth + LibraryColumns.Width(column) + TitleRoom);
        }

        _settings.Save();
        ApplyColumns();
    }

    /// <summary>Widens the window to <paramref name="needed"/> if it's narrower.</summary>
    private void WidenToFit(double needed)
    {
        if (Bounds.Width < needed)
        {
            // Through the player, so a library docked on its left grows leftward and stays docked.
            if (Player is not null)
            {
                Player.WidenLibrary(needed);
            }
            else
            {
                Width = needed;
            }
        }
    }

    /// <summary>
    /// The first time the window is shown after Genre and Year were put in it for the user (the settings'
    /// LibraryNeedsWidening), it widens once to fit them.
    /// </summary>
    private void WidenForNewColumnsWhenShown()
    {
        // After the player has put the windows in their places, so it can tell what the library is docked to.
        Opened += (_, _) => Dispatcher.UIThread.Post(WidenForNewColumns, DispatcherPriority.Background);
    }

    private void WidenForNewColumns()
    {
        if (_settings.LibraryNeedsWidening && !ShowingPlaylists)
        {
            _settings.LibraryNeedsWidening = false;
            WidenToFit(LibraryColumns.ShownWidth + TitleRoom);
        }
    }

    private void MoveColumn(LibraryColumn column, int index)
    {
        var columns = _settings.LibraryColumnsShown;
        index = Math.Clamp(index, 0, columns.Count - 1);
        if (columns.IndexOf(column) is var from && from >= 0 && from != index)
        {
            columns.RemoveAt(from);
            columns.Insert(index, column);
            _settings.Save();
            ApplyColumns();
        }
    }

    /// <summary>"Columns": a tick for each tag that can be shown beside the songs.</summary>
    private MenuItem ColumnsMenu()
    {
        var menu = new MenuItem { Header = "Columns" };
        AddColumnChoices(menu.Items);
        return menu;
    }

    private void AddColumnChoices(ItemCollection items)
    {
        foreach (var column in LibraryColumns.Choosable)
        {
            var item = new MenuItem
            {
                Header = LibraryColumns.Label(column),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _settings.LibraryColumnsShown.Contains(column),
                StaysOpenOnClick = true,
            };
            item.Click += (_, _) =>
            {
                ToggleColumn(column);
                item.IsChecked = _settings.LibraryColumnsShown.Contains(column);
            };
            items.Add(item);
        }
    }

    /// <summary>"Sort by tag": every column, whether it's shown or not.</summary>
    private MenuItem SortByTagMenu()
    {
        var menu = new MenuItem { Header = "Sort by tag" };
        foreach (var column in LibraryColumns.All)
        {
            var item = new MenuItem
            {
                Header = LibraryColumns.Label(column),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _settings.LibrarySortColumn == column,
            };
            item.Click += (_, _) => SortByColumn(column, flipIfSorted: false);
            menu.Items.Add(item);
        }

        return menu;
    }

    /// <summary>The headings' right-click menu: move or hide the column under the mouse, and choose the columns.</summary>
    private void OnColumnHeaderMenuOpening(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu { PlacementTarget = ColumnHeader, Placement = PlacementMode.Pointer };
        if ((e.Source as Visual)?.FindAncestorOfType<Border>(includeSelf: true) is { Tag: LibraryColumn column })
        {
            var index = _settings.LibraryColumnsShown.IndexOf(column);
            var left = new MenuItem { Header = "Move left", IsEnabled = index > 0 };
            left.Click += (_, _) => MoveColumn(column, index - 1);
            var right = new MenuItem { Header = "Move right", IsEnabled = index < _settings.LibraryColumnsShown.Count - 1 };
            right.Click += (_, _) => MoveColumn(column, index + 1);
            var hide = new MenuItem { Header = $"Hide {LibraryColumns.Label(column)}" };
            hide.Click += (_, _) => ToggleColumn(column);
            menu.Items.Add(left);
            menu.Items.Add(right);
            menu.Items.Add(hide);
            menu.Items.Add(new Separator());
        }

        AddResetWidths(menu.Items, LibraryColumns.Shown.Select(shown => shown.ToString()).ToList());
        AddColumnChoices(menu.Items);
        menu.Open(ColumnHeader);
    }

    private void OnHeadingPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressedHeading = (Border)sender!;
        _headingPressPoint = e.GetPosition(HeadingsOf(_pressedHeading));
        _draggingHeading = false;
        e.Pointer.Capture(_pressedHeading);
        e.Handled = true;
    }

    private void OnHeadingPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressedHeading is null || !ReferenceEquals(sender, _pressedHeading))
        {
            return;
        }

        var offset = e.GetPosition(HeadingsOf(_pressedHeading)).X - _headingPressPoint.X;
        if (!_draggingHeading && Math.Abs(offset) < HeadingDragDistance)
        {
            return;
        }

        // The heading follows the mouse until it's let go.
        _draggingHeading = true;
        _pressedHeading.RenderTransform = new TranslateTransform(offset, 0);
        _pressedHeading.Opacity = 0.7;
    }

    private void OnHeadingPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressedHeading is not { } heading || !ReferenceEquals(sender, heading))
        {
            return;
        }

        var dragged = _draggingHeading;
        var x = e.GetPosition(HeadingsOf(heading)).X;
        e.Pointer.Capture(null);
        EndHeadingDrag();
        e.Handled = true;

        // An opened streaming playlist's heading (LibraryWindow.PlaylistColumns.cs).
        if (heading.Tag is PlaylistColumn playlistColumn)
        {
            if (dragged)
            {
                DropPlaylistHeading(playlistColumn, x);
            }
            else
            {
                SortPlaylistBy(playlistColumn);
            }

            return;
        }

        if (heading.Tag is not LibraryColumn column)
        {
            return;
        }

        if (!dragged)
        {
            SortByColumn(column);
            return;
        }

        // It goes where the mouse was let go: after every other column whose middle is to the left of it.
        var others = _settings.LibraryColumnsShown.Where(shown => shown != column).ToList();
        var index = 0;
        var left = 0.0;
        foreach (var shown in LibraryColumns.Shown)
        {
            var width = LibraryColumns.Width(shown);
            if (shown != column && x > left + width / 2)
            {
                index = others.IndexOf(shown) + 1;
            }

            left += width;
        }

        MoveColumn(column, index);
    }

    // The row of headings a heading is in: the library's, or an opened streaming playlist's.
    private static Visual HeadingsOf(Border heading) => (Visual)heading.Parent!;

    private void EndHeadingDrag()
    {
        if (_pressedHeading is { } heading)
        {
            _pressedHeading = null;
            heading.RenderTransform = null;
            heading.Opacity = 1;
        }

        _draggingHeading = false;
    }
}
