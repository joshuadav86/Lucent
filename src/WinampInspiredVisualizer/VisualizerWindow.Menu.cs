using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace WinampInspiredVisualizer;

/// <summary>The menus you get by clicking the visuals. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    private DispatcherTimer? _clickTimer;
    private bool _skipClick;

    /// <summary>
    /// Fullscreen, playback controls (in fullscreen), moving between visuals, favourites and Auto, and the graphics options, at the
    /// mouse. It works in fullscreen too, where the controls under the visuals are hidden.
    /// </summary>
    private void ShowVisualsMenu()
    {
        if (_settings is null)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = VisualizerFrame, Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item(_isFullscreen ? "Leave fullscreen" : "Fullscreen", _isFullscreen ? "Esc" : "F11", ToggleFullscreen));
        menu.Items.Add(new Separator());
        if (_isFullscreen && Player is { } playback)
        {
            // Fullscreen covers the player, so its controls come along in the menu.
            playback.AddPlaybackItems(menu);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item("Next visual", "N", () => ChangePreset(+1)));
        menu.Items.Add(Item("Previous visual", "B", () => ChangePreset(-1)));
        if (!_isFullscreen)
        {
            // The picker opens from the bar under the visuals, which fullscreen hides.
            menu.Items.Add(Item("Choose a visual…", "V", TogglePresetPicker));
        }

        if (CurrentChoice() is { } current)
        {
            var favourite = Item("Favourite", "F", ToggleFavorite);
            favourite.IsChecked = current.IsFavorite;
            menu.Items.Add(favourite);
        }

        menu.Items.Add(new Separator());
        var auto = Item("Auto", "A", () =>
        {
            ToggleAutoCycle();
            Player?.ShowOptionsState();
        });
        auto.IsChecked = _settings.AutoCycle;
        menu.Items.Add(auto);
        var often = new MenuItem { Header = "Change visual about every" };
        foreach (var seconds in AutoCycleChoices)
        {
            var choice = new MenuItem { Header = IntervalText(seconds), IsChecked = _settings.AutoCycle && seconds == _settings.AutoCycleSeconds };
            choice.Click += (_, _) =>
            {
                // Choosing how often only makes sense with Auto on, so this turns it on too.
                _settings.AutoCycleSeconds = seconds;
                _settings.AutoCycle = true;
                Player?.ApplyVisualizerSettings();
                Player?.ShowOptionsState();
                UpdateAutoCycleButton();
                _settings.Save();
            };
            often.Items.Add(choice);
        }

        menu.Items.Add(often);
        if (Player is { } player)
        {
            menu.Items.Add(new Separator());
            player.AddGraphicsItems(menu);
        }

        menu.IsOpen = true;
    }

    /// <summary>
    /// In fullscreen, a left click on the visuals brings up the song's seek bar and playback
    /// controls at the mouse, since fullscreen covers the player. It waits out the double-click
    /// time first, so double-clicking still just leaves fullscreen.
    /// </summary>
    private void OnVisualsClicked()
    {
        if (_skipClick)
        {
            // The button coming up after a double-click.
            _skipClick = false;
            return;
        }

        if (!_isFullscreen || Player is null)
        {
            return;
        }

        if (_clickTimer is null)
        {
            _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
            _clickTimer.Tick += (_, _) =>
            {
                _clickTimer.Stop();
                ShowPlaybackMenu();
            };
        }

        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void OnVisualsDoubleClicked()
    {
        _clickTimer?.Stop();
        _skipClick = true;
        ToggleFullscreen();
    }

    private void ShowPlaybackMenu()
    {
        if (!_isFullscreen || Player is not { } playback)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = VisualizerFrame, Placement = PlacementMode.MousePoint };
        playback.AddPlaybackItems(menu);
        menu.IsOpen = true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    private static MenuItem Item(string header, string keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = keys };
        item.Click += (_, _) => action();
        return item;
    }
}
