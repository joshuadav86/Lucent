using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Lucent.Editor;
using Lucent.Layout;
using Lucent.Settings;
using Lucent.Windows;

namespace Lucent;

// Lucent full screen (Shift+F11, a double-click on the player's title bar, or "Full screen Lucent" on a
// right-click in any of Lucent's windows): every open window tiled across the whole of the player's screen,
// edge to edge, none over another. It's
// apart from the visuals' own fullscreen (F11). The layout is FullScreenLayout in Lucent.Core, shared with
// the Mac app and checked on many screens and scalings by tools/LayoutCheck. While it's on, opening or
// closing a window lays the others out again around it. Switching it off puts every window back where it
// was; D, G and K switch it off and lay the windows out their own way. Locked windows stay where they are,
// and the rest fill the screen beside them. Lucent opens full screen again if it was closed that way.
// Tidy layout (Shift+D, or on the same right-click) lays them out again in one of a few balanced layouts,
// the next each time it's pressed; outside full screen it tidies them in the space they take up.
public partial class MainWindow
{
    private bool _tilingSoon;

    private void SetUpLucentFullScreen()
    {
        LucentFullScreen.IsOn = () => _settings.LucentFullScreen;
        LucentFullScreen.Toggle = ToggleLucentFullScreen;
        LucentFullScreen.Tidy = TidyLayout;
        LucentFullScreen.Reset = ResetLayout;
        LucentFullScreen.PlayerEnlarged = () => PlayerIsEnlarged;
        LucentFullScreen.NormalPlayerSize = NormalPlayerSize;
        LucentFullScreen.Covers = window => TileKindOf(window) is not null;
        _snapper.Swapped += RememberSwap;
        LucentFullScreen.AddTo(WindowToggles.ContextMenu, this);
        LucentFullScreen.AddTo(NowPlayingMenu, this);
        // A right-click anywhere else on the player gets the two on their own.
        LucentPlayer.ContextMenu = new ContextMenu();
        LucentFullScreen.AddTo(LucentPlayer.ContextMenu, this);
        // Every window it lays out, as it first opens: the others are laid out again whenever it's shown or
        // hidden while Lucent's full screen.
        EventManager.RegisterClassHandler(typeof(Window), LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window && TileKindOf(window) is not null)
            {
                window.IsVisibleChanged += (_, _) => TileSoon();
                TileSoon();
            }
        }));
    }

    public void ToggleLucentFullScreen()
    {
        // As a single window, full screen is that window filling the screen.
        if (InSingleWindow)
        {
            ToggleSingleWindowFill();
            return;
        }

        if (_settings.LucentFullScreen)
        {
            LeaveLucentFullScreen(putBack: true);
        }
        else
        {
            EnterLucentFullScreen();
        }
    }

    private void EnterLucentFullScreen()
    {
        RestoreAllWindows();
        // Where everything is now, to go back to.
        _settings.WindowsBeforeFullScreen.Clear();
        foreach (var (name, window) in TiledWindows())
        {
            _settings.WindowsBeforeFullScreen[name] = new WindowBounds
            {
                Left = window.Left, Top = window.Top, Width = SizeOf(window).Width, Height = SizeOf(window).Height,
            };
        }

        _settings.LucentFullScreen = true;
        _settings.FullScreenSwaps.Clear();
        TileWindows();
    }

    /// <param name="putBack">Put every window back where it was before; otherwise they stay as they are, to be laid out another way.</param>
    private void LeaveLucentFullScreen(bool putBack)
    {
        if (!_settings.LucentFullScreen)
        {
            return;
        }

        _settings.LucentFullScreen = false;
        if (putBack)
        {
            RestoreAllWindows();
            foreach (var (name, window) in TiledWindows())
            {
                if (!_settings.WindowsBeforeFullScreen.TryGetValue(name, out var before) || WindowLocks.Holds(window))
                {
                    continue;
                }

                if (SizesItself(window))
                {
                    // The player and the equalizer size themselves: they only move.
                    window.Left = before.Left;
                    window.Top = before.Top;
                }
                else
                {
                    Place(window, before.Left, before.Top, before.Width, before.Height);
                }
            }
        }

        _settings.WindowsBeforeFullScreen.Clear();
        _settings.FullScreenSwaps.Clear();
        SavePlacements();
    }

    /// <summary>Lays the windows out across the screen again soon, once whatever's opening or closing has settled, if Lucent's full screen.</summary>
    private void TileSoon()
    {
        if (!_settings.LucentFullScreen || _tilingSoon || IsShuttingDown)
        {
            return;
        }

        _tilingSoon = true;
        // After the docking that goes on as a window opens (which waits for ContextIdle).
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _tilingSoon = false;
            if (_settings.LucentFullScreen && !IsShuttingDown && WindowState == WindowState.Normal)
            {
                TileWindows();
            }
        });
    }

    /// <summary>Tiles every open window across the player's screen (FullScreenLayout), and remembers where they went.</summary>
    private void TileWindows()
    {
        var area = ScreenArea.Of(this);
        FitEqualizerToPlayer();
        var windows = TiledWindows().Select(entry => (Window: entry.Window, Tile: TileOf(entry.Name, entry.Window))).ToList();
        var tiles = windows.Select(entry => entry.Tile).ToList();
        var layout = FullScreenLayout.Arrange(new TileRect(area.Left, area.Top, area.Width, area.Height), tiles, _settings.FullScreenStyle);
        WindowSwap.Apply(layout, tiles, _settings.FullScreenSwaps);
        PlaceTiles(windows, layout);
    }

    /// <summary>
    /// Tidy layout (Shift+D): lays the windows out in the next of FullScreenLayout's balanced layouts (the
    /// visuals in the middle, the player on one side or the other), forgetting any swaps. Full screen it
    /// fills the screen; otherwise it's a compact block around the player, which stays where it is
    /// (FullScreenLayout.ArrangeAround). Locked windows stay put, and the note it shows says so.
    /// </summary>
    public void TidyLayout()
    {
        if (InSingleWindow)
        {
            TidySingleWindow();
            return;
        }

        RestoreAllWindows();
        FitEqualizerToPlayer();
        var screen = ScreenArea.Of(this);
        var screenArea = new TileRect(screen.Left, screen.Top, screen.Width, screen.Height);
        var windows = TiledWindows().Select(entry => (Window: entry.Window, Tile: TileOf(entry.Name, entry.Window))).ToList();
        var tiles = windows.Select(entry => entry.Tile).ToList();
        var now = windows.ToDictionary(entry => entry.Tile.Key,
            entry => new TileRect(entry.Window.Left, entry.Window.Top, SizeOf(entry.Window).Width, SizeOf(entry.Window).Height));
        var (style, layout) = _settings.LucentFullScreen
            ? FullScreenLayout.Tidy(screenArea, tiles, now, _settings.FullScreenStyle)
            : FullScreenLayout.TidyAround(screenArea, tiles, now, _settings.FullScreenStyle);
        _settings.FullScreenStyle = style;
        _settings.FullScreenSwaps.Clear();
        PlaceTiles(windows, layout);
        var locked = FullScreenLayout.LockedNote(tiles.Where(tile => tile.LockedAt is not null).Select(tile => tile.Key).ToList());
        var name = layout.Places.Count == 0 ? "No room to tidy beside the locked windows" : FullScreenLayout.NameOf(style);
        Toast.Show(this, locked is null ? name : $"{name}. {locked}.");
    }

    /// <summary>Puts each window where the layout says, and remembers where they went.</summary>
    private void PlaceTiles(List<(Window Window, TileWindow Tile)> windows, TileResult layout)
    {
        foreach (var (window, tile) in windows)
        {
            // The visuals keep their spot while they're fullscreen on their own: it's where they come back to.
            if (!layout.Places.TryGetValue(tile.Key, out var place) || window == _visuals && _visuals.IsFullscreen)
            {
                continue;
            }

            if (tile.FixedSize)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = place.Left;
                window.Top = place.Top;
            }
            else
            {
                Place(window, place.Left, place.Top, place.Width, place.Height);
            }
        }

        SavePlacements();
    }

    /// <summary>
    /// A window dragged onto another swapped places with it. While Lucent's full screen, that's remembered,
    /// so laying the windows out again (as one opens or closes) keeps the two where they were swapped to.
    /// </summary>
    private void RememberSwap(Window dragged, Window other)
    {
        if (!_settings.LucentFullScreen)
        {
            return;
        }

        var tiled = TiledWindows();
        if (tiled.FirstOrDefault(entry => entry.Window == dragged).Name is { } first
            && tiled.FirstOrDefault(entry => entry.Window == other).Name is { } second)
        {
            WindowSwap.Remember(_settings.FullScreenSwaps, first, second);
        }
    }

    /// <summary>
    /// The equalizer takes the player's width when it opens, and the layouts put it under the player, so it's fitted
    /// to the player's width as it is now first: a player made bigger since would leave an empty gap beside it.
    /// </summary>
    private void FitEqualizerToPlayer()
    {
        if (!InSingleWindow && EqualizerWindow.Current is { IsVisible: true } equalizer && !WindowLocks.Holds(equalizer))
        {
            equalizer.FitTo(SizeOf(this).Width);
        }
    }

    /// <summary>The shown windows Lucent full screen lays out, by the names their places are kept under.</summary>
    private List<(string Name, Window Window)> TiledWindows()
    {
        var windows = new List<(string, Window)> { ("Player", this) };
        var others = new Window?[]
        {
            OpenEqualizerWindow, _queue, _libraryWindow, ArtworkWindow.Current, _visuals, AnalyzerWindow.Current, RadioWindow.Current, VideoWindow.Current,
        };
        foreach (var window in others.OfType<Window>())
        {
            if (window.IsVisible && (window.WindowState == WindowState.Normal || window == _visuals && _visuals.IsFullscreen))
            {
                windows.Add((WindowLocks.NameOf(window) ?? TileKindOf(window).ToString()!, window));
            }
        }

        var editors = AudioTools.OpenEditors.Where(editor => editor.IsVisible && editor.WindowState == WindowState.Normal).ToList();
        for (var i = 0; i < editors.Count; i++)
        {
            windows.Add((i == 0 ? "Editor" : $"Editor {i + 1}", editors[i]));
        }

        return windows;
    }

    private TileWindow TileOf(string name, Window window)
    {
        // The equalizer sizes itself by hand and may just have been fitted to the player: its size as set.
        var size = window is EqualizerWindow && !double.IsNaN(window.Width) && !double.IsNaN(window.Height)
            ? new Size(window.Width, window.Height)
            : SizeOf(window);
        var kind = TileKindOf(window)!.Value;
        var fixedSize = SizesItself(window);
        var locked = WindowLocks.Holds(window) ? new TileRect(window.Left, window.Top, size.Width, size.Height) : (TileRect?)null;
        return fixedSize
            ? new TileWindow(name, kind, size.Width, size.Height, size.Width, size.Height, FixedSize: true, LockedAt: locked)
            : new TileWindow(name, kind, window.MinWidth, window.MinHeight, size.Width, size.Height, LockedAt: locked);
    }

    /// <summary>The player and the equalizer size themselves, so they keep the size they are and only move.</summary>
    private static bool SizesItself(Window window) => TileKindOf(window) is TileKind.Player or TileKind.Equalizer;

    /// <summary>Which of the windows Lucent full screen lays out this is, or null for the others (dialogs and the like).</summary>
    private static TileKind? TileKindOf(Window window) => window switch
    {
        MainWindow => TileKind.Player,
        EqualizerWindow => TileKind.Equalizer,
        QueueWindow => TileKind.Queue,
        LibraryWindow => TileKind.Library,
        ArtworkWindow => TileKind.Artwork,
        VisualizerWindow => TileKind.Visuals,
        AnalyzerWindow => TileKind.Analyzer,
        VideoWindow => TileKind.Video,
        RadioWindow => TileKind.Radio,
        EditorWindow => TileKind.Editor,
        _ => null,
    };
}
