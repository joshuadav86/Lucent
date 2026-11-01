using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Lucent.Layout;
using Lucent.Settings;
using Lucent.Windows;

namespace Lucent;

// Single window (U, or "Single window" on a right-click in any of Lucent's windows): every open window shown
// as a panel inside the player's window, with the player's title bar across the top as the only one. The
// panels are laid out as Lucent full screen lays the windows out (SingleWindowLayout in Lucent.Core, checked
// by tools/LayoutCheck), in the window instead of across the screen, so Tidy layout (Shift+D) goes through the
// same balanced layouts. Resizing the window lays them out again to fit.
//
// The windows stay open, out of sight (Windows is kept from showing them, SingleWindow.Watch), and lend their
// insides to a panel; everything that asks whether one is open still gets the right answer, and opening or
// closing one (its button, its key, its own close button) adds or takes away its panel. Its keys and dropped
// files are handed on to it, and dragging a panel's title strip moves the single window. A panel going full
// screen on its own (the visuals' F11, say) is taken out to be a window for as long as it's full screen.
// Switching single window off gives every window its insides back where it was before. Editors, Record and
// dialogs stay windows of their own. Not with a classic skin, whose windows are fixed-size pictures; docking
// (D), fill (G), shrink (K), saved layouts and Reset to default layout switch it off, and Lucent full screen
// (Shift+F11, or a double-click on the title bar) makes the single window fill its screen. Lucent opens as a
// single window again if it was closed as one.
public partial class MainWindow
{
    /// <summary>A window shown as a panel in the single window: what was lent from it, to give back.</summary>
    private sealed class Pane
    {
        public Pane(Window window, Border box, object content, SizeToContent sizing)
        {
            Window = window;
            Box = box;
            Content = content;
            Sizing = sizing;
        }

        public Window Window { get; }

        /// <summary>The panel, which the window's insides are in.</summary>
        public Border Box { get; }

        public object Content { get; }

        public SizeToContent Sizing { get; }

        /// <summary>The window's buttons that are only for a window on its own, hidden while it's a panel.</summary>
        public List<UIElement> Hidden { get; } = new();

        /// <summary>The size it would like to be: its size as a window at first, then its panel's last size.</summary>
        public Size Wanted { get; set; }
    }

    private readonly Dictionary<Window, Pane> _panes = new();

    /// <summary>Windows taken out of the single window for their own full screen, to go back in after.</summary>
    private readonly HashSet<Window> _takenOut = new();

    private Grid? _singleFrame;
    private Border? _header;
    private Canvas? _paneArea;
    private bool _retilingSoon;
    private bool _growSoon;

    /// <summary>Lucent is opening as a single window: the windows it opens on the way stay out of sight.</summary>
    private bool _openingSingle;

    /// <summary>Where the single window was before it filled the screen (Shift+F11 or a double-click on its title bar), to go back to.</summary>
    private Rect? _singleBeforeFill;

    /// <summary>
    /// Single window has just ended: the player shrinking back from the single window's size isn't a resize
    /// for the windows round it to follow, as they go back to their own places (see OnPlayerSizeChanged).
    /// </summary>
    private bool _sizeFromSingleWindow;

    private bool InSingleWindow => _singleFrame is not null;

    /// <summary>The player's width, for the equalizer under it: in the single window the window's much wider than the player.</summary>
    internal double PlayerColumnWidth => InSingleWindow ? PlayerPaneWidth : ActualWidth;

    private double PlayerPaneWidth => PlayerWidth * PlayerScale;

    private void SetUpSingleWindow()
    {
        SingleWindow.IsOn = () => InSingleWindow;
        SingleWindow.IsAvailable = () => !IsClassic;
        SingleWindow.Toggle = ToggleSingleWindow;
        SingleWindow.PaneOf = window => _panes.TryGetValue(window, out var pane) ? pane.Box : null;
        SingleWindow.KeepsOutOfSight = window => (InSingleWindow || _openingSingle) && Hostable(window) && !_takenOut.Contains(window);
        SingleWindow.TakeOut = TakeOutOfSingleWindow;
        SingleWindow.PutBack = PutBackInSingleWindow;
        SingleWindow.DragPanel = StartPanelDrag;
        // A window opened while it's on becomes a panel, and one closed takes its panel with it.
        SingleWindow.ShownOrHidden = OnWindowShownOrHidden;
        _openingSingle = _settings.SingleWindow;
    }

    private void OnToggleSingleWindow(object sender, RoutedEventArgs e) => ToggleSingleWindow();

    public void ToggleSingleWindow()
    {
        if (InSingleWindow)
        {
            LeaveSingleWindow();
        }
        else
        {
            EnterSingleWindow();
        }
    }

    /// <summary>Which windows become panels: the ones Lucent full screen lays out, but not editors, which stay windows.</summary>
    private static bool Hostable(Window window) => TileKindOf(window) is { } kind && kind is not (TileKind.Player or TileKind.Editor);

    private void EnterSingleWindow()
    {
        if (InSingleWindow || IsShuttingDown)
        {
            return;
        }

        if (IsClassic)
        {
            _openingSingle = false;
            Toast.Show(this, "Single window works with Lucent's own look, not a classic skin");
            return;
        }

        LeaveLucentFullScreen(putBack: true);
        RestoreAllWindows();
        // Where every window is as a window, to go back to (nothing's saved while it's on).
        SavePlacements();
        var shown = TiledWindows().Where(entry => entry.Window == this || Hostable(entry.Window)).ToList();
        var screen = ScreenArea.Of(this);
        var screenArea = new TileRect(screen.Left, screen.Top, screen.Width, screen.Height);
        var place = _settings.SingleWindowPlace is { Width: > 0, Height: > 0 } saved
            ? SingleWindowLayout.OnScreen(new TileRect(saved.Left, saved.Top, saved.Width, saved.Height), screenArea)
            : SingleWindowLayout.Around(shown.Select(entry => new TileRect(entry.Window.Left, entry.Window.Top,
                SizeOf(entry.Window).Width, SizeOf(entry.Window).Height)).ToList(), screenArea);

        // The frame: the player's title bar across the top, and the panels under it, the player's own among them.
        var playerRoot = PlayerRoot;
        Content = null;
        WindowRoot.Children.Remove(TitleBar);
        _header = new Border { Child = TitleBar };
        _paneArea = new Canvas { ClipToBounds = true };
        Grid.SetRow(_paneArea, 1);
        _singleFrame = new Grid
        {
            RowDefinitions = { new RowDefinition { Height = GridLength.Auto }, new RowDefinition() },
            Children = { _header, _paneArea },
        };
        playerRoot.Width = PlayerPaneWidth;
        playerRoot.HorizontalAlignment = HorizontalAlignment.Left;
        playerRoot.VerticalAlignment = VerticalAlignment.Top;
        _paneArea.Children.Add(playerRoot);
        Content = _singleFrame;
        _paneArea.SizeChanged += (_, _) => RetileSoon();
        // Linking and locking are for separate windows.
        LinkToggle.Visibility = Visibility.Collapsed;
        foreach (var lockButton in TitleButtons.Children.OfType<LockButton>())
        {
            lockButton.Visibility = Visibility.Collapsed;
        }

        // The window sizes freely now, rather than scaling the player.
        SizeToContent = SizeToContent.Manual;
        MaxWidth = double.PositiveInfinity;
        MinWidth = 0;
        ResizeMode = ResizeMode.CanResize;
        _settings.SingleWindow = true;
        _settings.LucentFullScreen = false;
        _openingSingle = false;

        foreach (var (_, window) in shown.Where(entry => entry.Window != this))
        {
            Host(window);
        }

        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = place.Left;
        Top = place.Top;
        Width = place.Width;
        Height = place.Height;
        RetileSoon(grow: true);
        _settings.Save();
    }

    private void LeaveSingleWindow()
    {
        _openingSingle = false;
        if (!InSingleWindow)
        {
            return;
        }

        SaveSingleWindowPlace();
        _singleBeforeFill = null;
        _sizeFromSingleWindow = true;
        _ = ForgetSizeFromSingleWindowSoon();
        _settings.SingleWindow = false;
        var giveBack = _panes.Keys.ToList();
        foreach (var window in giveBack)
        {
            GiveBack(window);
        }

        // The player as it was: its title bar back on top of it, sized by its scale.
        var playerRoot = PlayerRoot;
        _paneArea!.Children.Remove(playerRoot);
        _header!.Child = null;
        Content = null;
        WindowRoot.Children.Insert(0, TitleBar);
        playerRoot.Width = double.NaN;
        playerRoot.ClearValue(HorizontalAlignmentProperty);
        playerRoot.ClearValue(VerticalAlignmentProperty);
        Content = playerRoot;
        _singleFrame = null;
        _header = null;
        _paneArea = null;
        ForgetPanelDrags();
        LinkToggle.Visibility = Visibility.Visible;
        foreach (var lockButton in TitleButtons.Children.OfType<LockButton>())
        {
            lockButton.Visibility = Visibility.Visible;
        }

        MinHeight = 0;
        ApplyPlayerSize();
        if (Saved("Player") is { } player)
        {
            Left = player.Left;
            Top = player.Top;
        }

        // Each window where it was left, or docked to the player if it's never been placed, then back in sight.
        foreach (var window in giveBack.Where(window => window.IsVisible))
        {
            // Docked ones too: the player's back where it was, so the group fits together as it did.
            if (!OpenWhereLeft(window, evenDocked: true))
            {
                if (window is ToolWindow tool && tool != _visuals)
                {
                    DockToPlayer(tool);
                }
                else if (window == _visuals)
                {
                    PlaceBesidePlayer(_visuals);
                }
            }

            SingleWindow.BringBack(window);
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (!InSingleWindow && !IsShuttingDown)
            {
                RedockSmallWindows();
                SavePlacements();
            }
        }, DispatcherPriority.Background);
        _settings.Save();
    }

    private async Task ForgetSizeFromSingleWindowSoon()
    {
        // Long enough for the player to settle at its own size, however the system takes its time over it.
        await Task.Delay(TimeSpan.FromSeconds(2));
        _sizeFromSingleWindow = false;
    }

    /// <summary>Lucent opens as a single window if it was closed as one.</summary>
    private void ReopenSingleWindow()
    {
        if (_settings.SingleWindow && !IsClassic)
        {
            _settings.SingleWindow = false;
            EnterSingleWindow();
        }
        else
        {
            _settings.SingleWindow = false;
            _openingSingle = false;
        }
    }

    /// <summary>A window opened while it's on becomes a panel; one closed (or hidden) takes its panel away.</summary>
    private void OnWindowShownOrHidden(Window window)
    {
        if (!InSingleWindow || window == this || IsShuttingDown || !Hostable(window))
        {
            return;
        }

        if (window.IsVisible && !_panes.ContainsKey(window) && !_takenOut.Contains(window))
        {
            Host(window);
            RetileSoon(grow: true);
        }
        else if (!window.IsVisible)
        {
            _takenOut.Remove(window);
            if (_panes.ContainsKey(window))
            {
                GiveBack(window);
                RetileSoon();
            }
        }
    }

    /// <summary>Lends a window's insides to a new panel, and keeps the window out of sight.</summary>
    private void Host(Window window)
    {
        if (_paneArea is null || _panes.ContainsKey(window) || window.Content is not FrameworkElement content)
        {
            return;
        }

        var size = SizeOf(window);
        var wanted = new Size(
            size.Width > 0 && !double.IsNaN(size.Width) ? size.Width : Math.Max(window.MinWidth, 400),
            size.Height > 0 && !double.IsNaN(size.Height) ? size.Height : Math.Max(window.MinHeight, 300));
        var sizing = window.SizeToContent;
        // It keeps its size while its insides are away, rather than shrinking to nothing.
        if (sizing != SizeToContent.Manual)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = wanted.Width;
            window.Height = wanted.Height;
        }

        window.Content = null;
        // The window's own resources (and the styles in them) go with its insides, shared rather than moved.
        var box = new Border { ClipToBounds = true, DataContext = window.DataContext, Resources = window.Resources, Child = content };
        var caption = WindowChrome.GetWindowChrome(window)?.CaptionHeight ?? 34;
        box.MouseLeftButtonDown += (_, e) => OnPaneCaptionPressed(box, caption, e);
        box.MouseRightButtonUp += (_, e) => OnPaneCaptionRightClick(box, caption, e);
        WatchPanelDrags(box);
        _paneArea.Children.Add(box);
        var pane = new Pane(window, box, content, sizing) { Wanted = wanted };
        // The size it had as a panel last time, so it's arranged as it was (not the equalizer, which sizes itself).
        if (TileKindOf(window) != TileKind.Equalizer && KeyOf(pane) is { } name
            && _settings.SingleWindowSizes.TryGetValue(name, out var kept) && kept.Width > 0 && kept.Height > 0)
        {
            pane.Wanted = new Size(kept.Width, kept.Height);
        }

        // Its lock, its maximize and its keep-on-top are for a window on its own.
        foreach (var element in LogicalDescendants(content).OfType<FrameworkElement>()
                     .Where(element => element.Visibility == Visibility.Visible && (element is LockButton || element.Name is "MaximizeButton" or "OnTopToggle")))
        {
            element.Visibility = Visibility.Collapsed;
            pane.Hidden.Add(element);
        }

        _panes[window] = pane;
        SingleWindow.PutAway(window);
    }

    /// <summary>Gives a window its insides back from its panel (it stays out of sight; <see cref="SingleWindow.BringBack"/> shows it).</summary>
    private void GiveBack(Window window)
    {
        if (!_panes.Remove(window, out var pane))
        {
            return;
        }

        pane.Hidden.ForEach(element => element.Visibility = Visibility.Visible);
        pane.Box.Child = null;
        _paneArea?.Children.Remove(pane.Box);
        pane.Box.Resources = new ResourceDictionary();
        window.Content = pane.Content;
        if (pane.Sizing != SizeToContent.Manual)
        {
            window.SizeToContent = pane.Sizing;
        }
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var descendant in LogicalDescendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// A press on a panel's title strip (where its window's title bar would drag it) drags the panel, to swap it
    /// with another; the equalizer's, which can't swap, moves the single window.
    /// </summary>
    private void OnPaneCaptionPressed(Border box, double caption, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ClickCount != 1 || e.GetPosition(box).Y > caption || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        e.Handled = true;
        if (!StartPanelDrag(box, e) && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    /// <summary>A right-click on a panel's title strip gets Lucent's window menu, as a window's title bar does.</summary>
    private void OnPaneCaptionRightClick(Border box, double caption, MouseButtonEventArgs e)
    {
        if (e.Handled || e.GetPosition(box).Y > caption)
        {
            return;
        }

        e.Handled = LucentFullScreen.ShowWindowMenu(this);
    }

    /// <summary>Takes a panel out to be a window of its own, where its panel is, for its own full screen.</summary>
    private bool TakeOutOfSingleWindow(Window window)
    {
        if (!_panes.TryGetValue(window, out var pane))
        {
            return false;
        }

        var at = pane.Box.TranslatePoint(new Point(0, 0), this);
        var size = new Size(pane.Box.ActualWidth, pane.Box.ActualHeight);
        _takenOut.Add(window);
        GiveBack(window);
        window.SizeToContent = SizeToContent.Manual;
        window.Left = Left + at.X;
        window.Top = Top + at.Y;
        window.Width = size.Width;
        window.Height = size.Height;
        SingleWindow.BringBack(window);
        RetileSoon();
        return true;
    }

    /// <summary>Puts a window taken out for its own full screen back in its panel.</summary>
    private void PutBackInSingleWindow(Window window)
    {
        if (!_takenOut.Remove(window) || !InSingleWindow || !window.IsVisible)
        {
            return;
        }

        Host(window);
        RetileSoon();
    }

    /// <summary>Lays the panels out again soon, once whatever's opening, closing or resizing has settled.</summary>
    /// <param name="grow">Make the window bigger if the panels don't fit (a panel's just opened).</param>
    private void RetileSoon(bool grow = false)
    {
        _growSoon |= grow;
        if (_retilingSoon)
        {
            return;
        }

        _retilingSoon = true;
        Dispatcher.InvokeAsync(() =>
        {
            _retilingSoon = false;
            var growNow = _growSoon;
            _growSoon = false;
            Retile(growNow);
        }, DispatcherPriority.Background);
    }

    /// <summary>The panels, as SingleWindowLayout knows them, each with its box.</summary>
    private List<(FrameworkElement Box, TileWindow Tile)> PanelTiles()
    {
        // The player keeps its own size: its width at its scale, and as tall as that makes it.
        PlayerRoot.Measure(new Size(PlayerPaneWidth, double.PositiveInfinity));
        var playerSize = new Size(PlayerPaneWidth, Math.Ceiling(PlayerRoot.DesiredSize.Height));
        var panels = new List<(FrameworkElement, TileWindow)>
        {
            (PlayerRoot, new TileWindow("Player", TileKind.Player, playerSize.Width, playerSize.Height, playerSize.Width, playerSize.Height, FixedSize: true)),
        };
        foreach (var pane in _panes.Values)
        {
            var kind = TileKindOf(pane.Window)!.Value;
            var name = WindowLocks.NameOf(pane.Window) ?? kind.ToString();
            if (kind == TileKind.Equalizer)
            {
                // The equalizer sizes itself, as a window does to fit its sliders.
                var size = new Size(
                    double.IsNaN(pane.Window.Width) ? pane.Wanted.Width : pane.Window.Width,
                    double.IsNaN(pane.Window.Height) ? pane.Wanted.Height : pane.Window.Height);
                panels.Add((pane.Box, new TileWindow(name, kind, size.Width, size.Height, size.Width, size.Height, FixedSize: true)));
            }
            else
            {
                panels.Add((pane.Box, new TileWindow(name, kind, pane.Window.MinWidth, pane.Window.MinHeight,
                    Math.Max(pane.Wanted.Width, pane.Window.MinWidth), Math.Max(pane.Wanted.Height, pane.Window.MinHeight))));
            }
        }

        return panels;
    }

    /// <summary>The title bar's height, which the panels go under.</summary>
    private double HeaderHeight => _header is { ActualHeight: > 0 } header ? header.ActualHeight : TitleBar.Height;

    /// <summary>
    /// Lays the panels out in the window as it is now. With <paramref name="grow"/>, the window first grows
    /// (staying on its screen) if they wouldn't all fit; a panel that still doesn't fit waits, out of sight,
    /// until the window's made big enough.
    /// </summary>
    private void Retile(bool grow = false)
    {
        if (!InSingleWindow || _paneArea is null || IsShuttingDown)
        {
            return;
        }

        var panels = PanelTiles();
        var tiles = panels.Select(panel => panel.Tile).ToList();
        var style = _settings.SingleWindowStyle;
        var header = HeaderHeight;
        var (smallestWidth, smallestHeight) = SingleWindowLayout.Smallest(tiles, style);
        MinWidth = smallestWidth;
        MinHeight = smallestHeight + header;
        var body = new Size(_paneArea.ActualWidth > 0 ? _paneArea.ActualWidth : ActualWidth,
            _paneArea.ActualHeight > 0 ? _paneArea.ActualHeight : ActualHeight - header);
        if (WindowState == WindowState.Normal)
        {
            var screen = ScreenArea.Of(this);
            var (width, height) = grow
                ? SingleWindowLayout.Grown(body.Width, body.Height, tiles, style, (screen.Width, screen.Height - header))
                : (Math.Max(body.Width, smallestWidth), Math.Max(body.Height, smallestHeight));
            if (width > body.Width + 0.5 || height > body.Height + 0.5)
            {
                var place = SingleWindowLayout.OnScreen(new TileRect(Left, Top, width, height + header),
                    new TileRect(screen.Left, screen.Top, screen.Width, screen.Height));
                Left = place.Left;
                Top = place.Top;
                Width = place.Width;
                Height = place.Height;
                body = new Size(place.Width, place.Height - header);
            }
        }

        var laidOut = SingleWindowLayout.Arrange(body.Width, body.Height, tiles, style, _settings.SingleWindowSwaps);
        var layout = WithDividers(laidOut, tiles);
        foreach (var (box, tile) in panels)
        {
            if (!layout.Places.TryGetValue(tile.Key, out var place))
            {
                box.Visibility = Visibility.Collapsed;
                continue;
            }

            box.Visibility = Visibility.Visible;
            Canvas.SetLeft(box, place.Left);
            Canvas.SetTop(box, place.Top);
            if (box != PlayerRoot)
            {
                box.Width = place.Width;
                box.Height = place.Height;
            }

            // What it'd like as the layout has it, not as a line moved by hand has it, or each layout would
            // move the line again on top of the last.
            if (_panes.Values.FirstOrDefault(pane => pane.Box == box) is { } pane && !tile.FixedSize
                && laidOut.Places.TryGetValue(tile.Key, out var wanted))
            {
                pane.Wanted = new Size(wanted.Width, wanted.Height);
                _settings.SingleWindowSizes[tile.Key] = new WindowBounds { Width = wanted.Width, Height = wanted.Height };
            }
        }
    }

    /// <summary>Tidy layout (Shift+D) in the single window: the panels in the next balanced layout, any swaps and lines moved forgotten.</summary>
    private void TidySingleWindow()
    {
        if (_paneArea is null)
        {
            return;
        }

        var panels = PanelTiles();
        var now = panels.ToDictionary(panel => panel.Tile.Key, panel => new TileRect(
            Canvas.GetLeft(panel.Box), Canvas.GetTop(panel.Box), panel.Box.ActualWidth, panel.Box.ActualHeight));
        var (style, _) = SingleWindowLayout.Tidy(_paneArea.ActualWidth, _paneArea.ActualHeight,
            panels.Select(panel => panel.Tile).ToList(), now, _settings.SingleWindowStyle);
        _settings.SingleWindowStyle = style;
        _settings.SingleWindowSwaps.Clear();
        _settings.SingleWindowDividers.Clear();
        Retile();
        Toast.Show(PlayerRoot, FullScreenLayout.NameOf(style));
    }

    /// <summary>
    /// Shift+F11 or a double-click on the title bar while it's a single window: the window fills its screen, or
    /// goes back to where it was. (Not Windows' own maximize, which would cover the frame Lucent draws round it.)
    /// </summary>
    private void ToggleSingleWindowFill()
    {
        if (_singleBeforeFill is { } before)
        {
            _singleBeforeFill = null;
            Left = before.Left;
            Top = before.Top;
            Width = before.Width;
            Height = before.Height;
            return;
        }

        _singleBeforeFill = new Rect(Left, Top, ActualWidth, ActualHeight);
        var screen = ScreenArea.Of(this);
        Left = screen.Left;
        Top = screen.Top;
        Width = screen.Width;
        Height = screen.Height;
    }

    /// <summary>Remembers where the single window is and its size (not while it fills the screen).</summary>
    private void SaveSingleWindowPlace()
    {
        if (!InSingleWindow || WindowState != WindowState.Normal)
        {
            return;
        }

        var bounds = _singleBeforeFill ?? new Rect(Left, Top, ActualWidth, ActualHeight);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.SingleWindowPlace = new WindowBounds { Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height };
        }
    }

    /// <summary>The panel a key press or a drop happened in, if it's one of the windows' insides.</summary>
    private Pane? PaneAt(object? source)
    {
        if (!InSingleWindow || SingleWindow.VisualOf(source) is not { } visual)
        {
            return null;
        }

        foreach (var pane in _panes.Values)
        {
            if (pane.Box == visual || pane.Box.IsAncestorOf(visual))
            {
                return pane;
            }
        }

        return null;
    }

    /// <summary>
    /// A key pressed in a panel goes to its window first, as it would if it were a window of its own (Esc closes
    /// the equalizer, F11 takes the visuals full screen, and so on). True if the window used it.
    /// </summary>
    private bool KeyForPane(KeyEventArgs e)
    {
        if (e.Handled || PaneAt(e.OriginalSource) is not { } pane || e.InputSource is null)
        {
            return false;
        }

        var handed = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
            // Typing in a text box is still typing, wherever the key's handed on to.
            Source = e.OriginalSource,
        };
        pane.Window.RaiseEvent(handed);
        e.Handled = handed.Handled;
        return handed.Handled;
    }

    /// <summary>Music dropped on a panel goes to its window (the queue adds it, rather than playing it now). True if it took it.</summary>
    private bool DropForPane(DragEventArgs e)
    {
        if (PaneAt(e.OriginalSource) is not { Window: ToolWindow tool })
        {
            return false;
        }

        tool.TakeDrop(e);
        e.Handled = true;
        return true;
    }
}
