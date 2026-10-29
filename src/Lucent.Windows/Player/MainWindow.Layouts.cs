using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Lucent.Layout;
using Lucent.Settings;
using Lucent.Windows;

namespace Lucent;

// Layouts saved by name (right-click › Layouts in any of Lucent's windows): "Save current layout…" keeps
// which windows are open, where each is and how big, the locks, the link, the player's size and whether
// Lucent's full screen, and picking the layout from the same menu (or pressing its number, 1 to 9) puts
// them all back that way. Kept in the settings (AppSettings.Layouts, SavedLayouts in Lucent.Core).
public partial class MainWindow
{
    private void SetUpSavedLayouts()
    {
        SavedLayoutsMenu.Layouts = () => _settings.Layouts;
        SavedLayoutsMenu.Open = OpenLayout;
        SavedLayoutsMenu.Save = SaveLayout;
        SavedLayoutsMenu.Rename = RenameLayout;
        SavedLayoutsMenu.Delete = DeleteLayout;
    }

    /// <summary>The number on a key (1 to 9, on the top row or the number pad), for opening a saved layout.</summary>
    private static int? LayoutNumber(Key key) => key switch
    {
        >= Key.D1 and <= Key.D9 => key - Key.D0,
        >= Key.NumPad1 and <= Key.NumPad9 => key - Key.NumPad0,
        _ => null,
    };

    /// <summary>Asks for a name and saves the windows as they are now under it. A name that's taken replaces that layout, after a warning.</summary>
    private void SaveLayout(Window over)
    {
        string? warned = null;
        var name = PromptWindow.Ask(over, "Save layout",
            "A name for the windows as they are now, such as Studio or Party. Your first nine layouts are on the keys 1 to 9.",
            "Save", UnusedLayoutName(), answer =>
            {
                var clean = SavedLayouts.CleanName(answer);
                if (SavedLayouts.Find(_settings.Layouts, clean) is not { } existing || string.Equals(warned, clean, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                warned = clean;
                return $"There's already a layout called {existing.Name}. Press Save again to replace it.";
            });
        if (name is null)
        {
            return;
        }

        var layout = CurrentLayout(name);
        SavedLayouts.Save(_settings.Layouts, layout);
        _settings.Save();
        var key = SavedLayouts.KeyOf(_settings.Layouts, layout);
        Toast.Show(this, key is null ? $"Saved {layout.Name}" : $"Saved {layout.Name}. Press {key} to go back to it.");
    }

    /// <summary>"Layout 1", or the next number no layout has yet, to start the name from.</summary>
    private string UnusedLayoutName()
    {
        var number = _settings.Layouts.Count + 1;
        while (SavedLayouts.Find(_settings.Layouts, $"Layout {number}") is not null)
        {
            number++;
        }

        return $"Layout {number}";
    }

    private void RenameLayout(Window over, SavedLayout layout)
    {
        var name = PromptWindow.Ask(over, "Rename layout", "A new name for this layout.", "Rename", layout.Name,
            answer => SavedLayouts.Find(_settings.Layouts, answer) is { } other && other != layout ? "There's already a layout with that name." : null);
        if (name is not null && SavedLayouts.Rename(_settings.Layouts, layout.Name, name))
        {
            _settings.Save();
        }
    }

    private void DeleteLayout(SavedLayout layout)
    {
        if (SavedLayouts.Delete(_settings.Layouts, layout.Name))
        {
            _settings.Save();
            Toast.Show(this, $"Deleted {layout.Name}");
        }
    }

    /// <summary>The windows as they are now, as a layout to keep.</summary>
    private SavedLayout CurrentLayout(string name)
    {
        var layout = new SavedLayout
        {
            Name = name,
            Locked = _settings.LockedWindows.ToList(),
            Linked = _settings.LinkWindows,
            FullScreen = _settings.LucentFullScreen,
            FullScreenStyle = _settings.FullScreenStyle,
            FullScreenSwaps = _settings.FullScreenSwaps.Select(swap => swap.ToList()).ToList(),
            PlayerScale = _settings.PlayerScale,
        };
        foreach (var (key, window) in TiledWindows())
        {
            if (_settings.LucentFullScreen && _settings.WindowsBeforeFullScreen.TryGetValue(key, out var before))
            {
                // Full screen, it keeps where they go back to; opening it lays them out full screen again.
                layout.Windows[key] = SavedLayouts.Copy(before);
            }
            else if (window == _visuals && _visuals.IsFullscreen && _settings.Windows.TryGetValue(key, out var visuals))
            {
                layout.Windows[key] = SavedLayouts.Copy(visuals);
            }
            else
            {
                layout.Windows[key] = new WindowBounds { Left = window.Left, Top = window.Top, Width = SizeOf(window).Width, Height = SizeOf(window).Height };
            }
        }

        return layout;
    }

    /// <summary>
    /// Puts the windows back as a layout has them: the ones it had open opened and the rest closed (the video,
    /// the radio and editors stay as they are, as closing them would stop what's in them), each in its place,
    /// locked and linked as they were, and full screen again if it was saved that way.
    /// </summary>
    public void OpenLayout(SavedLayout layout)
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        // Unlocked while they move; the layout's own locks go on once they're in place.
        foreach (var window in Application.Current.Windows.Cast<Window>().ToList())
        {
            WindowLocks.SetLocked(window, false);
        }

        _settings.LockedWindows.Clear();
        if (_settings.LinkWindows != layout.Linked)
        {
            ToggleLink();
        }

        _settings.FullScreenStyle = layout.FullScreenStyle;
        if (!IsClassic)
        {
            _settings.PlayerScale = layout.PlayerScale;
            ApplyPlayerSize();
        }

        var places = PlacesOnScreen(layout);
        // Kept as their places first, so the windows it opens open there rather than docking to the player.
        foreach (var (name, place) in places)
        {
            var kept = SavedLayouts.Copy(place);
            kept.Docked = false;
            _settings.Windows[name] = kept;
        }

        if (places.TryGetValue("Player", out var player))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = player.Left;
            Top = player.Top;
        }

        foreach (var tool in ToolWindows)
        {
            if (tool.IsVisible != layout.Windows.ContainsKey(tool.PlacementName))
            {
                ToggleWindow(tool);
            }
        }

        if (EqualizerWindow.Current is not null != layout.Windows.ContainsKey("Equalizer"))
        {
            ToggleEqualizer();
        }

        if (AnalyzerWindow.IsOpen != layout.Windows.ContainsKey("Analyzer"))
        {
            ToggleAnalyzer();
        }

        if (ArtworkWindow.IsOpen != layout.Windows.ContainsKey("Artwork"))
        {
            ToggleArtwork();
        }

        // Once the player's at its size and the windows that opened have docked (as full screen waits too).
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => FinishOpeningLayout(layout, places));
    }

    private void FinishOpeningLayout(SavedLayout layout, Dictionary<string, WindowBounds> places)
    {
        if (IsShuttingDown)
        {
            return;
        }

        var open = TiledWindows();
        // The player first, then the windows around it.
        foreach (var (name, window) in open)
        {
            if (!places.TryGetValue(name, out var place))
            {
                continue;
            }

            if (SizesItself(window))
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

        foreach (var name in layout.Locked)
        {
            if (open.FirstOrDefault(entry => entry.Name == name).Window is { } window)
            {
                WindowLocks.SetLocked(window, true);
            }
            else if (!_settings.LockedWindows.Contains(name))
            {
                // Locked where it's kept, for when it's opened.
                _settings.LockedWindows.Add(name);
            }
        }

        if (layout.FullScreen)
        {
            EnterLucentFullScreen();
            _settings.FullScreenSwaps = layout.FullScreenSwaps.Select(swap => swap.ToList()).ToList();
            TileWindows();
        }
        else
        {
            SavePlacements();
        }

        Toast.Show(this, layout.Name);
    }

    /// <summary>
    /// Where the layout puts each window. Saved with the player on a screen that's not there now, the whole
    /// layout moves onto the player's screen, keeping its shape. Otherwise a window whose place is off the
    /// screens now is left out, to open docked to the player instead.
    /// </summary>
    private Dictionary<string, WindowBounds> PlacesOnScreen(SavedLayout layout)
    {
        static Rect RectOf(WindowBounds place) => new(place.Left, place.Top, place.Width, place.Height);
        if (layout.Windows.TryGetValue("Player", out var player) && !IsOnScreen(RectOf(player)))
        {
            var area = ScreenArea.Of(this);
            return SavedLayouts.MovedOnto(layout.Windows, new TileRect(area.Left, area.Top, area.Width, area.Height));
        }

        return layout.Windows.Where(entry => IsOnScreen(RectOf(entry.Value)))
            .ToDictionary(entry => entry.Key, entry => SavedLayouts.Copy(entry.Value));
    }
}
