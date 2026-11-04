using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lucent.Library;
using Lucent.Mac.Windows;

namespace Lucent.Mac.Library;

/// <summary>
/// Makes the lines between a row of column headings draggable, as in a streaming app's playlist: over a line the pointer
/// shows the resize cursor and the line lights up, dragging it moves the line with the pointer and resizes the columns
/// either side (<see cref="ColumnWidths.Drag"/>), and a double-click fits them to their widest text. The grips lie over
/// the headings' grid, whose columns are the title, the tag columns' row, and the length; the length's line has none, as
/// the length stays as it is.
/// </summary>
internal sealed class ColumnGrips
{
    // How wide the line can be grabbed: a few pixels either side of it.
    private const double GripWidth = 11;

    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);

    private readonly Grid _header;
    private readonly Canvas _grips = new();
    private readonly Func<IReadOnlyList<double>> _widths;
    private readonly Action<double[]> _resize;
    private readonly Action<int> _fit;
    private readonly Action _done;

    // The line being dragged, and the widths and pointer when it was pressed.
    private Border? _dragged;
    private double[] _start = Array.Empty<double>();
    private double _pressX;

    /// <param name="header">The headings' grid.</param>
    /// <param name="widths">The tag columns' widths now, left to right.</param>
    /// <param name="resize">Shows the columns at new widths, while a line is dragged or after a fit.</param>
    /// <param name="fit">Fits a column (by its index) to its widest text.</param>
    /// <param name="done">Keeps the widths, once a line is let go.</param>
    public ColumnGrips(Grid header, Func<IReadOnlyList<double>> widths, Action<double[]> resize, Action<int> fit, Action done)
    {
        _header = header;
        _widths = widths;
        _resize = resize;
        _fit = fit;
        _done = done;
        Grid.SetColumnSpan(_grips, 3);
        header.Children.Add(_grips);
        header.SizeChanged += (_, _) => Place();
    }

    /// <summary>The room for the title and the tag columns: the headings less the length's column.</summary>
    public double Room => _header.Bounds.Width - _header.ColumnDefinitions[2].ActualWidth;

    /// <summary>Puts a grip over each line, for the columns' widths now. Call it whenever the headings change.</summary>
    public void Place()
    {
        var widths = _widths();
        // A line on each tag column's left.
        var lines = widths.Count;
        while (_grips.Children.Count < lines)
        {
            _grips.Children.Add(NewGrip());
        }

        while (_grips.Children.Count > lines && !ReferenceEquals(_grips.Children[^1], _dragged))
        {
            _grips.Children.RemoveAt(_grips.Children.Count - 1);
        }

        // The columns end where the length begins, and the title has the rest.
        var x = Room - widths.Sum();
        for (var line = 0; line < lines; line++)
        {
            var grip = (Border)_grips.Children[line];
            grip.Tag = line;
            grip.Height = _header.Bounds.Height;
            Canvas.SetLeft(grip, Math.Round(x) - (GripWidth - 1) / 2);
            x += widths[line];
        }
    }

    private Border NewGrip()
    {
        var mark = new Border { Width = 2, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0 };
        mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        var grip = new Border { Width = GripWidth, Background = Brushes.Transparent, Cursor = ResizeCursor, Child = mark };
        grip.PointerEntered += (_, _) => mark.Opacity = 1;
        grip.PointerExited += (_, _) => mark.Opacity = ReferenceEquals(grip, _dragged) ? 1 : 0;
        grip.PointerPressed += OnGripPressed;
        grip.PointerMoved += OnGripMoved;
        grip.PointerReleased += (_, e) => e.Pointer.Capture(null);
        grip.PointerCaptureLost += (_, _) => EndDrag(grip);
        return grip;
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        var grip = (Border)sender!;
        if (!e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            foreach (var column in ColumnWidths.FittedBy((int)grip.Tag!))
            {
                _fit(column);
            }

            Place();
            return;
        }

        _dragged = grip;
        _start = _widths().ToArray();
        _pressX = e.GetPosition(_header).X;
        e.Pointer.Capture(grip);
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dragged is not { } grip || !ReferenceEquals(sender, grip))
        {
            return;
        }

        var widths = ColumnWidths.Drag(_start, (int)grip.Tag!, e.GetPosition(_header).X - _pressX, Room);
        if (!widths.SequenceEqual(_widths()))
        {
            _resize(widths);
            Place();
        }
    }

    private void EndDrag(Border grip)
    {
        if (!ReferenceEquals(grip, _dragged))
        {
            return;
        }

        _dragged = null;
        grip.Child!.Opacity = grip.IsPointerOver ? 1 : 0;
        if (!_start.SequenceEqual(_widths()))
        {
            _done();
        }

        Place();
    }
}
