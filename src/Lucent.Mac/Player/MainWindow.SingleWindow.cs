using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Layout;
using Lucent.Mac.Editor;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;
using Lucent.Settings;

namespace Lucent.Mac;

// Single window (U, Window › Single Window, or "Single window" on a right-click in any of Lucent's windows):
// every open window shown as a panel inside the player's window, with the player's title bar across the top
// as the only one. The panels are laid out as Lucent full screen lays the windows out (SingleWindowLayout in
// Lucent.Core, checked by tools/LayoutCheck), in the window instead of across the screen, so Tidy layout
// (Shift+D) goes through the same balanced layouts. Resizing the window lays them out again to fit.
//
// The windows stay open, out of sight (ordered out, as the windows are while the player's in the Dock), and
// lend their insides to a panel; everything that asks whether one is open still gets the right answer, and
// opening or closing one (its button, its key, its own close button) adds or takes away its panel. Its keys
// and dropped files are handed on to it. A panel going full screen on its own (the visuals' F11, say) is
// taken out to be a window for as long as it's full screen. Switching single window off gives every window
// its insides back where it was before. Editors, Record and dialogs stay windows of their own. Not with a
// classic skin, whose windows are fixed-size pictures; docking (D), fill (G), shrink (K), Lucent full screen,
// saved layouts and Reset to default layout switch it off. Lucent opens as a single window again if it was
// closed as one.
public partial class MainWindow
{
    /// <summary>A window shown as a panel in the single window: what was lent from it, to give back.</summary>
    private sealed class Pane
    {
        public Pane(Window window, Border box, Control content, List<IStyle> styles, SizeToContent sizing)
        {
            Window = window;
            Box = box;
            Content = content;
            Styles = styles;
            Sizing = sizing;
        }

        public Window Window { get; }

        /// <summary>The panel, which the window's insides are in.</summary>
        public Border Box { get; }

        public Control Content { get; }

        /// <summary>The window's own styles, which go with its insides.</summary>
        public List<IStyle> Styles { get; }

        public SizeToContent Sizing { get; }

        /// <summary>The window's buttons that are only for a window on its own, hidden while it's a panel.</summary>
        public List<Control> Hidden { get; } = new();

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

    /// <summary>Where the single window was before it filled the screen (Shift+F11 or a double-click on its title bar), to go back to.</summary>
    private Rect? _singleBeforeFill;

    /// <summary>
    /// Single window has just ended: the player shrinking back from the single window's size isn't a resize
    /// for the windows round it to follow, as they go back to their own places (see OnPlayerSizeChanged).
    /// </summary>
    private bool _sizeFromSingleWindow;

    private bool InSingleWindow => _singleFrame is not null;

    /// <summary>The player's width, for the equalizer under it: in the single window the window's much wider than the player.</summary>
    internal double PlayerColumnWidth => InSingleWindow ? PlayerPaneWidth : Bounds.Width;

    private double PlayerPaneWidth => PlayerWidth * PlayerScale;

    private void SetUpSingleWindow()
    {
        SingleWindow.IsOn = () => InSingleWindow;
        SingleWindow.IsAvailable = () => !IsClassic;
        SingleWindow.Toggle = ToggleSingleWindow;
        SingleWindow.PaneOf = window => _panes.TryGetValue(window, out var pane) ? pane.Box : null;
        SingleWindow.TakeOut = TakeOutOfSingleWindow;
        SingleWindow.PutBack = PutBackInSingleWindow;
        SingleWindow.DragPanel = StartPanelDrag;
        // A window opened while it's on becomes a panel, and one closed takes its panel with it.
        IsVisibleProperty.Changed.AddClassHandler<Window>((window, _) => OnWindowShownOrHidden(window));
    }

    private void OnToggleSingleWindow(object? sender, RoutedEventArgs e) => ToggleSingleWindow();

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
            Toast.Show(this, "Single window works with Lucent's own look, not a classic skin");
            return;
        }

        LeaveLucentFullScreen(putBack: true);
        RestoreAllWindows();
        // Where every window is as a window, to go back to (nothing's saved while it's on).
        SavePlacements();
        var shown = TiledWindows().Where(entry => entry.Window == this || Hostable(entry.Window)).ToList();
        var screen = Placement.WorkArea(this);
        var screenArea = new TileRect(screen.X, screen.Y, screen.Width, screen.Height);
        var place = _settings.SingleWindowPlace is { Width: > 0, Height: > 0 } saved
            ? SingleWindowLayout.OnScreen(new TileRect(saved.Left, saved.Top, saved.Width, saved.Height), screenArea)
            : SingleWindowLayout.Around(shown.Select(entry => ToTileRect(Placement.BoundsOf(entry.Window))).ToList(), screenArea);

        // The frame: the player's title bar across the top, and the panels under it, the player's own among them.
        var playerRoot = PlayerRoot;
        Lend(this);
        WindowRoot.Children.Remove(TitleBar);
        _header = new Border { Child = TitleBar };
        _paneArea = new Canvas { ClipToBounds = true };
        Grid.SetRow(_paneArea, 1);
        _singleFrame = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Children = { _header, _paneArea } };
        playerRoot.Width = PlayerPaneWidth;
        _paneArea.Children.Add(playerRoot);
        Content = _singleFrame;
        _paneArea.SizeChanged += (_, _) => RetileSoon();
        // Linking and locking are for separate windows.
        LinkToggle.IsVisible = false;
        foreach (var lockButton in TitleButtons.Children.OfType<LockButton>())
        {
            lockButton.IsVisible = false;
        }

        // The window sizes freely now, rather than scaling the player.
        SizeToContent = SizeToContent.Manual;
        MaxWidth = double.PositiveInfinity;
        MinWidth = 0;
        CanResize = true;
        FreeShape();
        _settings.SingleWindow = true;
        _settings.LucentFullScreen = false;

        foreach (var (_, window) in shown.Where(entry => entry.Window != this))
        {
            Host(window);
        }

        this.MoveTo(place.Left, place.Top);
        Width = place.Width;
        Height = place.Height;
        RetileSoon(grow: true);
        _settings.Save();
    }

    private void LeaveSingleWindow()
    {
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
            GiveBack(window, show: false);
        }

        // The player as it was: its title bar back on top of it, sized by its scale.
        var playerRoot = PlayerRoot;
        _paneArea!.Children.Remove(playerRoot);
        _header!.Child = null;
        Lend(this);
        WindowRoot.Children.Insert(0, TitleBar);
        playerRoot.Width = double.NaN;
        Content = playerRoot;
        _singleFrame = null;
        _header = null;
        _paneArea = null;
        ForgetPanelDrags();
        LinkToggle.IsVisible = true;
        foreach (var lockButton in TitleButtons.Children.OfType<LockButton>())
        {
            lockButton.IsVisible = true;
        }

        MinHeight = 0;
        ApplyPlayerSize();
        if (Saved("Player") is { } player)
        {
            this.MoveTo(player.Left, player.Top);
        }

        // Each window where it was left, or docked to the player if it's never been placed, then back in sight.
        var placed = new List<(Window, PixelPoint)>();
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

            BringBack(window, belong: false);
            placed.Add((window, window.Position));
        }

        _ = BelongAfterLeaving(placed);
        Dispatcher.UIThread.Post(() =>
        {
            KeepPlayerShape();
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
            // It's still on its way to the screen: its insides go now, and it's put out of sight once it's there.
            Host(window, showing: true);
            RetileSoon(grow: true);
        }
        else if (!window.IsVisible)
        {
            _takenOut.Remove(window);
            if (_panes.ContainsKey(window))
            {
                GiveBack(window, show: false);
                RetileSoon();
            }
        }
    }

    /// <summary>Lends a window's insides to a new panel, and puts the window out of sight.</summary>
    /// <param name="showing">It's being shown now: macOS puts it on the screen at the end of that, so it's put away once it's opened.</param>
    private void Host(Window window, bool showing = false)
    {
        if (_paneArea is null || _panes.ContainsKey(window) || window.Content is not Control content)
        {
            return;
        }

        var size = Placement.SizeOf(window);
        var wanted = new Size(
            size.Width > 0 ? size.Width : Math.Max(window.MinWidth, 400),
            size.Height > 0 ? size.Height : Math.Max(window.MinHeight, 300));
        var sizing = window.SizeToContent;
        // It keeps its size while its insides are away, rather than shrinking to nothing.
        if (sizing != SizeToContent.Manual)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = wanted.Width;
            window.Height = wanted.Height;
        }

        Lend(window);
        var styles = window.Styles.ToList();
        window.Styles.Clear();
        var box = new Border { ClipToBounds = true, DataContext = window.DataContext };
        box.Styles.AddRange(styles);
        // The window's own resources, looked up from its insides: copied, so the window still has them too.
        foreach (var key in window.Resources.Keys.ToList())
        {
            if (window.Resources.TryGetValue(key, out var value))
            {
                box.Resources[key] = value;
            }
        }

        box.Child = content;
        WatchPanelDrags(box);
        _paneArea.Children.Add(box);
        var pane = new Pane(window, box, content, styles, sizing) { Wanted = wanted };
        // The size it had as a panel last time, so it's arranged as it was (not the equalizer, which sizes itself).
        if (TileKindOf(window) != TileKind.Equalizer && KeyOf(pane) is { } name
            && _settings.SingleWindowSizes.TryGetValue(name, out var kept) && kept.Width > 0 && kept.Height > 0)
        {
            pane.Wanted = new Size(kept.Width, kept.Height);
        }

        // Out of sight, at a size, until it's laid out: the canvas would otherwise measure it with no limit, and a
        // list in it (a library or queue of thousands of songs) would make every row at once, hanging Lucent.
        box.Width = pane.Wanted.Width;
        box.Height = pane.Wanted.Height;
        box.IsVisible = false;

        // Its lock, its maximize and its keep-on-top are for a window on its own.
        pane.Hidden.AddRange(content.GetLogicalDescendants().OfType<Control>()
            .Where(control => control.IsVisible && (control is LockButton || control.Name is "MaximizeButton" or "OnTopToggle")));
        pane.Hidden.ForEach(control => control.IsVisible = false);
        _panes[window] = pane;
        if (showing)
        {
            void OnOpened(object? sender, EventArgs e)
            {
                window.Opened -= OnOpened;
                if (_panes.ContainsKey(window))
                {
                    PutAway(window);
                }
            }

            window.Opened += OnOpened;
        }
        else
        {
            PutAway(window);
        }
    }

    /// <summary>Gives a window its insides back from its panel. With <paramref name="show"/> it's put back in sight too.</summary>
    private void GiveBack(Window window, bool show)
    {
        if (!_panes.Remove(window, out var pane))
        {
            return;
        }

        pane.Hidden.ForEach(control => control.IsVisible = true);
        pane.Box.Child = null;
        _paneArea?.Children.Remove(pane.Box);
        UpdateLayout();
        pane.Box.Styles.Clear();
        window.Styles.AddRange(pane.Styles);
        window.Content = pane.Content;
        if (pane.Sizing != SizeToContent.Manual)
        {
            window.SizeToContent = pane.Sizing;
        }

        if (show)
        {
            BringBack(window);
        }
    }

    /// <summary>
    /// Takes a window's insides away from it, so they can go in another window: Avalonia only lets go of them
    /// once its layout's caught up (otherwise they'd still be its to lay out, wherever they went).
    /// </summary>
    private static void Lend(Window window)
    {
        window.Content = null;
        foreach (var presenter in window.GetVisualDescendants().OfType<ContentPresenter>().Where(presenter => presenter.Name == "PART_ContentPresenter"))
        {
            presenter.UpdateChild();
        }

        window.UpdateLayout();
    }

    /// <summary>
    /// Takes a window off the screen without closing it, as Lucent does with the windows while the player's in the
    /// Dock. It stops belonging to the player too, so moving the single window doesn't carry it along out of sight.
    /// </summary>
    private void PutAway(Window window)
    {
        if (NativeWindow(window) is var native && native != IntPtr.Zero)
        {
            if (NativeWindow(this) is var player && player != IntPtr.Zero)
            {
                ObjC.SendVoid(player, "removeChildWindow:", native);
            }

            ObjC.SendVoid(native, "orderOut:", IntPtr.Zero);
        }
    }

    /// <summary>Puts a window put away with <see cref="PutAway"/> back on the screen, belonging to the player again unless <paramref name="belong"/> is false.</summary>
    private void BringBack(Window window, bool belong = true)
    {
        var native = NativeWindow(window);
        if (native == IntPtr.Zero)
        {
            return;
        }

        // Ordered out, it stopped belonging to the player (and moving with it); it does again, in front of it.
        if (belong)
        {
            Belong(native);
        }

        ObjC.SendVoid(native, "orderFront:", IntPtr.Zero);
    }

    /// <summary>Makes a window belong to the player again, so it moves with it and stays in front of it.</summary>
    private void Belong(IntPtr native)
    {
        if (NativeWindow(this) is var player && player != IntPtr.Zero)
        {
            ObjC.SendVoid(player, "addChildWindow:ordered:", native, 1UL);
        }
    }

    /// <summary>
    /// Once the player's settled at its own place and size after single window: each window it gave back is put
    /// where it was meant to go, if it's been carried off, and belongs to the player again. (Belonging to it any
    /// sooner, macOS can carry them along as the player's frame goes back from the single window's.)
    /// </summary>
    private async Task BelongAfterLeaving(List<(Window Window, PixelPoint At)> placed)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        if (InSingleWindow || IsShuttingDown)
        {
            return;
        }

        foreach (var (window, at) in placed)
        {
            if (!window.IsVisible || SingleWindow.Holds(window) || NativeWindow(window) is var native && native == IntPtr.Zero)
            {
                continue;
            }

            if (window.Position != at)
            {
                window.Position = at;
            }

            Belong(NativeWindow(window));
        }

        SavePlacements();
    }

    /// <summary>Lets the window be any shape (the player alone keeps its shape as it's resized).</summary>
    private void FreeShape()
    {
        if (OperatingSystem.IsMacOS() && NativeWindow(this) is var native && native != IntPtr.Zero)
        {
            // Resize increments and a fixed shape can't both be set: setting one clears the other.
            ObjC.SendVoid(native, "setContentResizeIncrements:", new ObjC.CGSize { Width = 1, Height = 1 });
        }
    }

    /// <summary>Takes a panel out to be a window of its own, where its panel is, for its own full screen.</summary>
    private bool TakeOutOfSingleWindow(Window window)
    {
        if (!_panes.TryGetValue(window, out var pane))
        {
            return false;
        }

        var at = pane.Box.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var size = pane.Box.Bounds.Size;
        _takenOut.Add(window);
        GiveBack(window, show: false);
        window.SizeToContent = SizeToContent.Manual;
        Placement.Place(window, Left + at.X, Top + at.Y, size.Width, size.Height);
        BringBack(window);
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
        Dispatcher.UIThread.Post(() =>
        {
            _retilingSoon = false;
            var growNow = _growSoon;
            _growSoon = false;
            Retile(growNow);
        }, DispatcherPriority.Background);
    }

    /// <summary>The panels, as SingleWindowLayout knows them, each with its box.</summary>
    private List<(Control Box, TileWindow Tile)> PanelTiles()
    {
        // The player keeps its own size: its width at its scale, and as tall as that makes it.
        PlayerRoot.Measure(new Size(PlayerPaneWidth, double.PositiveInfinity));
        var playerSize = new Size(PlayerPaneWidth, Math.Ceiling(PlayerRoot.DesiredSize.Height));
        var panels = new List<(Control, TileWindow)>
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
    private double HeaderHeight => _header is { Bounds.Height: > 0 } header ? header.Bounds.Height : TitleBar.Height;

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
        var body = new Size(Math.Max(_paneArea.Bounds.Width, Bounds.Width), Math.Max(_paneArea.Bounds.Height, Bounds.Height - header));
        if (WindowState == WindowState.Normal)
        {
            var screen = Placement.WorkArea(this);
            var (width, height) = grow
                ? SingleWindowLayout.Grown(body.Width, body.Height, tiles, style, (screen.Width, screen.Height - header))
                : (Math.Max(body.Width, smallestWidth), Math.Max(body.Height, smallestHeight));
            if (width > body.Width + 0.5 || height > body.Height + 0.5)
            {
                var place = SingleWindowLayout.OnScreen(new TileRect(Left, Top, width, height + header),
                    new TileRect(screen.X, screen.Y, screen.Width, screen.Height));
                this.MoveTo(place.Left, place.Top);
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
                box.IsVisible = false;
                continue;
            }

            box.IsVisible = true;
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
            Canvas.GetLeft(panel.Box), Canvas.GetTop(panel.Box), panel.Box.Bounds.Width, panel.Box.Bounds.Height));
        var (style, _) = SingleWindowLayout.Tidy(_paneArea.Bounds.Width, _paneArea.Bounds.Height,
            panels.Select(panel => panel.Tile).ToList(), now, _settings.SingleWindowStyle);
        _settings.SingleWindowStyle = style;
        _settings.SingleWindowSwaps.Clear();
        _settings.SingleWindowDividers.Clear();
        Retile();
        Toast.Show(PlayerRoot, FullScreenLayout.NameOf(style));
    }

    /// <summary>
    /// Shift+F11 or a double-click on the title bar while it's a single window: the window fills its screen, or
    /// goes back to where it was.
    /// </summary>
    private void ToggleSingleWindowFill()
    {
        if (_singleBeforeFill is { } before)
        {
            _singleBeforeFill = null;
            this.MoveTo(before.X, before.Y);
            Width = before.Width;
            Height = before.Height;
            return;
        }

        _singleBeforeFill = Placement.BoundsOf(this);
        var screen = Placement.WorkArea(this);
        this.MoveTo(screen.X, screen.Y);
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

        var bounds = _singleBeforeFill ?? Placement.BoundsOf(this);
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _settings.SingleWindowPlace = new WindowBounds { Left = bounds.X, Top = bounds.Y, Width = bounds.Width, Height = bounds.Height };
        }
    }

    /// <summary>The panel a key press or a drop happened in, if it's one of the windows' insides.</summary>
    private Pane? PaneAt(object? source)
    {
        if (!InSingleWindow || source is not Visual visual)
        {
            return null;
        }

        foreach (var pane in _panes.Values)
        {
            if (pane.Box == visual || pane.Box.IsVisualAncestorOf(visual))
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
        if (e.Handled || PaneAt(e.Source) is not { } pane)
        {
            return false;
        }

        var handed = new KeyEventArgs
        {
            RoutedEvent = KeyDownEvent,
            Key = e.Key,
            KeyModifiers = e.KeyModifiers,
            PhysicalKey = e.PhysicalKey,
            KeySymbol = e.KeySymbol,
            KeyDeviceType = e.KeyDeviceType,
            Source = e.Source,
        };
        pane.Window.RaiseEvent(handed);
        e.Handled = handed.Handled;
        return handed.Handled;
    }

    /// <summary>Music dropped on a panel goes to its window (the queue adds it, rather than playing it now). True if it took it.</summary>
    private bool DropForPane(DragEventArgs e)
    {
        if (PaneAt(e.Source) is not { Window: ToolWindow tool })
        {
            return false;
        }

        tool.TakeDrop(e);
        return true;
    }

    private static TileRect ToTileRect(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
