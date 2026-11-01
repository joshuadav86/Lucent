using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Lucent.Layout;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

// Rearranging the single window's panels by hand (MainWindow.SingleWindow.cs has the rest of it):
//   - Dragging a panel by its title bar onto another panel swaps the two, as dragging one window onto another
//     does (an outline says "Swap with …"); the swaps are kept (AppSettings.SingleWindowSwaps) and laid out
//     again whenever the panels are. Not the player or the equalizer, which size themselves.
//   - The lines between panels can be dragged to make the panels either side bigger or smaller (PanelDividers
//     in Lucent.Core), down to their smallest; each line moved is kept (AppSettings.SingleWindowDividers) and
//     moved again from wherever the layout puts it, so it stays moved as the window's resized. Not a line
//     beside the player or the equalizer. Tidy layout (Shift+D) forgets the swaps and the lines moved.
public partial class MainWindow
{
    /// <summary>A panel being dragged by its title bar, to swap with the panel it's dropped on.</summary>
    private sealed class PanelDrag
    {
        public PanelDrag(Pane pane, string key, Point start)
        {
            Pane = pane;
            Key = key;
            Start = start;
        }

        public Pane Pane { get; }

        public string Key { get; }

        public Point Start { get; }

        /// <summary>It's moved far enough to be a drag rather than a click.</summary>
        public bool Moving { get; set; }

        /// <summary>The panel it would swap with if dropped now, or null.</summary>
        public string? Target { get; set; }
    }

    /// <summary>What a grip's for: a line, and which of its edges (0, or the far side of the column it carries).</summary>
    private sealed record GripFor(DividerLine Line, double Edge);

    /// <summary>A line between panels being dragged.</summary>
    private sealed record DividerDrag(Border Grip, DividerLine Line, Point Start, double From, double Least, double Most);

    /// <summary>How far a panel's dragged before it's a drag, not a click.</summary>
    private const double PanelDragStart = 6;

    private readonly List<Border> _grips = new();
    private PanelDrag? _panelDrag;
    private DividerDrag? _dividerDrag;
    private Border? _swapOutline;

    /// <summary>The panels and where the layout put them (before any lines were moved) when they were last laid out.</summary>
    private List<TileWindow>? _laidOutTiles;
    private TileResult? _laidOutBase;
    private TileResult? _laidOut;

    /// <summary>Hooks a new panel up to be dragged by its title bar.</summary>
    private void WatchPanelDrags(Border box)
    {
        box.PointerMoved += OnPanelDragMoved;
        box.PointerReleased += OnPanelDragReleased;
        box.PointerCaptureLost += (_, _) => EndPanelDrag(swap: false);
    }

    /// <summary>Forgets the drags and the lines' grips, as single window ends.</summary>
    private void ForgetPanelDrags()
    {
        _panelDrag = null;
        _dividerDrag = null;
        _swapOutline = null;
        _grips.Clear();
        _laidOutTiles = null;
        _laidOutBase = null;
        _laidOut = null;
    }

    /// <summary>The layout with the lines moved by hand moved too, kept for dragging. The panels go where it says.</summary>
    private TileResult WithDividers(TileResult layout, List<TileWindow> tiles)
    {
        var moved = PanelDividers.Apply(layout, tiles, _settings.SingleWindowDividers);
        _laidOutTiles = tiles;
        _laidOutBase = layout;
        _laidOut = moved;
        if (_dividerDrag is null)
        {
            ShowGrips(moved, tiles);
        }

        return moved;
    }

    // ---- Swapping panels ----

    /// <summary>A press on a panel's title bar: the start of dragging it onto another. False if it isn't a panel that can swap.</summary>
    private bool StartPanelDrag(Visual from, PointerPressedEventArgs e)
    {
        if (_paneArea is null || _laidOutTiles is null || PaneAt(from) is not { } pane || KeyOf(pane) is not { } key
            || _laidOutTiles.FirstOrDefault(tile => tile.Key == key) is not { FixedSize: false })
        {
            return false;
        }

        _panelDrag = new PanelDrag(pane, key, e.GetPosition(_paneArea));
        e.Pointer.Capture(pane.Box);
        return true;
    }

    private void OnPanelDragMoved(object? sender, PointerEventArgs e)
    {
        if (_panelDrag is not { } drag || sender != drag.Pane.Box || _paneArea is null || _laidOut is null || _laidOutTiles is null)
        {
            return;
        }

        var at = e.GetPosition(_paneArea);
        if (!drag.Moving)
        {
            if (Math.Abs(at.X - drag.Start.X) < PanelDragStart && Math.Abs(at.Y - drag.Start.Y) < PanelDragStart)
            {
                return;
            }

            drag.Moving = true;
            drag.Pane.Box.Opacity = 0.6;
        }

        drag.Target = SwapTargetAt(drag.Key, at);
        ShowSwapOutline(drag.Target);
    }

    private void OnPanelDragReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panelDrag is not { } drag || sender != drag.Pane.Box)
        {
            return;
        }

        e.Handled = true;
        EndPanelDrag(swap: true);
        e.Pointer.Capture(null);
    }

    private void EndPanelDrag(bool swap)
    {
        if (_panelDrag is not { } drag)
        {
            return;
        }

        _panelDrag = null;
        drag.Pane.Box.Opacity = 1;
        ShowSwapOutline(null);
        if (swap && drag.Moving && drag.Target is { } target)
        {
            WindowSwap.Remember(_settings.SingleWindowSwaps, drag.Key, target);
            _settings.Save();
            Retile();
        }
    }

    /// <summary>The panel a panel dropped here would swap with, as WindowSwap decides for windows, or null.</summary>
    private string? SwapTargetAt(string key, Point at)
    {
        if (_laidOut is null || _laidOutTiles is null || !_laidOut.Places.TryGetValue(key, out var place))
        {
            return null;
        }

        var tiles = _laidOutTiles.ToDictionary(tile => tile.Key);
        var dragged = tiles[key];
        var others = _laidOut.Places.Where(entry => entry.Key != key && tiles.ContainsKey(entry.Key)).ToList();
        var candidates = others.Select(entry => new SwapCandidate(entry.Value, tiles[entry.Key].MinWidth, tiles[entry.Key].MinHeight,
            !tiles[entry.Key].FixedSize)).ToList();
        var index = WindowSwap.TargetAt(at.X, at.Y, new SwapCandidate(place, dragged.MinWidth, dragged.MinHeight, true), candidates, WindowSwap.Depth);
        return index < 0 ? null : others[index].Key;
    }

    /// <summary>Outlines the panel a drop would swap with, saying so, or takes the outline away.</summary>
    private void ShowSwapOutline(string? target)
    {
        if (_paneArea is null)
        {
            return;
        }

        if (target is null || _laidOut is null || !_laidOut.Places.TryGetValue(target, out var place))
        {
            if (_swapOutline is not null)
            {
                _paneArea.Children.Remove(_swapOutline);
                _swapOutline = null;
            }

            return;
        }

        if (_swapOutline is null)
        {
            _swapOutline = new Border
            {
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6),
                BorderBrush = ThemeBrush("AccentBrush") ?? Brushes.DodgerBlue,
                Background = new SolidColorBrush(Colors.Black, 0.35),
                IsHitTestVisible = false,
                ZIndex = 20,
                Child = new TextBlock
                {
                    FontSize = 15,
                    FontWeight = FontWeight.SemiBold,
                    Foreground = ThemeBrush("TextBrush") ?? Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            _paneArea.Children.Add(_swapOutline);
        }

        ((TextBlock)_swapOutline.Child!).Text = $"Swap with {target}";
        Canvas.SetLeft(_swapOutline, place.Left);
        Canvas.SetTop(_swapOutline, place.Top);
        _swapOutline.Width = place.Width;
        _swapOutline.Height = place.Height;
    }

    /// <summary>The name a panel's laid out by.</summary>
    private static string? KeyOf(Pane pane) => WindowLocks.NameOf(pane.Window) ?? TileKindOf(pane.Window)?.ToString();

    private IBrush? ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var found) ? found as IBrush : null;

    // ---- Moving the lines between panels ----

    /// <summary>A strip over each line between panels that can be dragged, with the cursor for dragging it.</summary>
    private void ShowGrips(TileResult layout, List<TileWindow> tiles)
    {
        if (_paneArea is null)
        {
            return;
        }

        foreach (var old in _grips)
        {
            _paneArea.Children.Remove(old);
        }

        _grips.Clear();
        foreach (var line in PanelDividers.Lines(layout.Places, tiles))
        {
            // A line carrying the player's column along can be dragged by either of the column's edges.
            foreach (var edge in line.CarriedWidth > 0 ? new[] { 0, line.CarriedWidth } : new[] { 0.0 })
            {
                AddGrip(line, edge);
            }
        }
    }

    private void AddGrip(DividerLine line, double edge)
    {
            var grip = new Border
            {
                Background = Brushes.Transparent,
                Opacity = 0.6,
                Cursor = new Cursor(line.Across ? StandardCursorType.SizeNorthSouth : StandardCursorType.SizeWestEast),
                ZIndex = 10,
                Tag = new GripFor(line, edge),
            };
            PlaceGrip(grip, line, edge);
            grip.PointerEntered += (_, _) => grip.Background = ThemeBrush("AccentBrush") ?? Brushes.DodgerBlue;
            grip.PointerExited += (_, _) =>
            {
                if (_dividerDrag?.Grip != grip)
                {
                    grip.Background = Brushes.Transparent;
                }
            };
            grip.PointerPressed += OnGripPressed;
            grip.PointerMoved += OnGripMoved;
            grip.PointerReleased += OnGripReleased;
            grip.PointerCaptureLost += (_, _) => EndDividerDrag();
            _paneArea!.Children.Add(grip);
            _grips.Add(grip);
    }

    private static void PlaceGrip(Border grip, DividerLine line, double offset)
    {
        var half = PanelDividers.Grip / 2;
        if (line.Across)
        {
            Canvas.SetLeft(grip, line.Start);
            Canvas.SetTop(grip, line.Position + offset - half);
            grip.Width = line.End - line.Start;
            grip.Height = PanelDividers.Grip;
        }
        else
        {
            Canvas.SetLeft(grip, line.Position + offset - half);
            Canvas.SetTop(grip, line.Start);
            grip.Width = PanelDividers.Grip;
            grip.Height = line.End - line.Start;
        }
    }

    private void OnGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { Tag: GripFor { Line: var shown } } grip || _paneArea is null || _laidOutBase is null || _laidOutTiles is null
            || !e.GetCurrentPoint(grip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // Moved as the last line moved, from where it is with the others moved.
        var others = _settings.SingleWindowDividers.Where(divider => !shown.Is(divider)).ToList();
        var from = _settings.SingleWindowDividers.FirstOrDefault(shown.Is)?.Offset ?? 0;
        var without = PanelDividers.Apply(_laidOutBase, _laidOutTiles, others);
        if (PanelDividers.Lines(without.Places, _laidOutTiles).FirstOrDefault(shown.Is) is not { } line)
        {
            return;
        }

        var (least, most) = PanelDividers.Room(line, without.Places, _laidOutTiles);
        _settings.SingleWindowDividers = others;
        from = Math.Clamp(from, least, most);
        PanelDividers.Remember(_settings.SingleWindowDividers, line, from);
        _dividerDrag = new DividerDrag(grip, line, e.GetPosition(_paneArea), from, least, most);
        e.Pointer.Capture(grip);
        e.Handled = true;
    }

    private void OnGripMoved(object? sender, PointerEventArgs e)
    {
        if (_dividerDrag is not { } drag || sender != drag.Grip || _paneArea is null)
        {
            return;
        }

        var at = e.GetPosition(_paneArea);
        var offset = Math.Clamp(drag.From + (drag.Line.Across ? at.Y - drag.Start.Y : at.X - drag.Start.X), drag.Least, drag.Most);
        PanelDividers.Remember(_settings.SingleWindowDividers, drag.Line, offset);
        foreach (var grip in _grips)
        {
            if (grip.Tag is GripFor { } other && other.Line.Is(drag.Line))
            {
                PlaceGrip(grip, drag.Line, other.Edge + offset);
            }
        }

        Retile();
    }

    private void OnGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dividerDrag is not { } drag || sender != drag.Grip)
        {
            return;
        }

        e.Handled = true;
        e.Pointer.Capture(null);
        EndDividerDrag();
    }

    private void EndDividerDrag()
    {
        if (_dividerDrag is not { } drag)
        {
            return;
        }

        _dividerDrag = null;
        drag.Grip.Background = Brushes.Transparent;
        _settings.Save();
        // The grips again, for the lines where they are now.
        Retile();
    }
}
