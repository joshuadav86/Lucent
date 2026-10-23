using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Layout;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

// Lucent full screen (Shift+F11, or "Full screen Lucent" on a right-click in any of Lucent's windows):
// every open window tiled across the whole of the player's screen, edge to edge, none over another. It's
// apart from the visuals' own fullscreen (F11). The layout is FullScreenLayout in Lucent.Core, shared with
// the Mac app and checked on many screens and scalings by tools/LayoutCheck. While it's on, opening or
// closing a window lays the others out again around it. Switching it off puts every window back where it
// was; D, G and K switch it off and lay the windows out their own way. Locked windows stay where they are,
// and the rest fill the screen beside them. Lucent opens full screen again if it was closed that way.
public partial class MainWindow
{
    private bool _tilingSoon;

    private void SetUpLucentFullScreen()
    {
        LucentFullScreen.IsOn = () => _settings.LucentFullScreen;
        LucentFullScreen.Toggle = ToggleLucentFullScreen;
        LucentFullScreen.Covers = window => TileKindOf(window) is not null;
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
        var windows = TiledWindows().Select(entry => (Window: entry.Window, Tile: TileOf(entry.Name, entry.Window))).ToList();
        var layout = FullScreenLayout.Arrange(new TileRect(area.Left, area.Top, area.Width, area.Height), windows.Select(entry => entry.Tile).ToList());
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
        var size = SizeOf(window);
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
