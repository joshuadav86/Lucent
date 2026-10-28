using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
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

    /// <summary>
    /// Shows the settings' columns: their headings, and the cells in every row. Saved playlists, listed under
    /// PLAYLISTS without the headings, have none.
    /// </summary>
    private void ApplyColumns()
    {
        var shown = ShowingPlaylists
            ? new List<LibraryColumn>()
            : _settings.LibraryColumnsShown.Where(LibraryColumns.Choosable.Contains).Distinct().ToList();
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
        ColumnHeader.IsVisible = LibraryColumns.Shown.Count > 0;
        ColumnHeadings.Children.Clear();
        foreach (var column in LibraryColumns.Shown)
        {
            var text = new TextBlock
            {
                Text = HeadingText(column),
                FontSize = 11,
                Padding = new Thickness(0, 0, 10, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                TextAlignment = LibraryColumns.AlignRight(column) ? TextAlignment.Right : TextAlignment.Left,
            };
            var heading = new Border
            {
                Child = text,
                Width = LibraryColumns.Width(column),
                Background = Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Tag = column,
            };
            ToolTip.SetTip(heading, $"Sort by {LibraryColumns.Label(column).ToLowerInvariant()}. Drag to move it.");
            text.SetResourceReference(TextBlock.ForegroundProperty, HeadingBrush(column));
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
    }

    /// <summary>The sorted column's heading has an arrow and stands out.</summary>
    private string HeadingText(LibraryColumn column) => _settings.LibrarySortColumn == column
        ? $"{LibraryColumns.Heading(column)} {(_settings.LibrarySortDescending ? "▼" : "▲")}"
        : LibraryColumns.Heading(column);

    private string HeadingBrush(LibraryColumn column) => _settings.LibrarySortColumn == column ? "AccentBrush" : "FaintTextBrush";

    /// <summary>Sorts by a column: a second time on the same one flips it.</summary>
    private void SortByColumn(LibraryColumn column, bool flipIfSorted = true)
    {
        _settings.LibrarySortDescending = _settings.LibrarySortColumn == column && flipIfSorted
            ? !_settings.LibrarySortDescending
            : LibraryColumns.DescendingFirst(column);
        _settings.LibrarySortColumn = column;
        _settings.Save();
        SortLabel.Text = LibraryItemComparer.LabelFor(_settings);
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
            var needed = LibraryColumns.ShownWidth + LibraryColumns.Width(column) + TitleRoom;
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

        _settings.Save();
        ApplyColumns();
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
        _headingPressPoint = e.GetPosition(ColumnHeadings);
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

        var offset = e.GetPosition(ColumnHeadings).X - _headingPressPoint.X;
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
        if (_pressedHeading is not { Tag: LibraryColumn column } heading || !ReferenceEquals(sender, heading))
        {
            return;
        }

        var dragged = _draggingHeading;
        var x = e.GetPosition(ColumnHeadings).X;
        e.Pointer.Capture(null);
        EndHeadingDrag();
        e.Handled = true;
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
