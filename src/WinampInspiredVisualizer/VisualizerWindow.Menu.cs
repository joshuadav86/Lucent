using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer;

/// <summary>The menus you get by clicking the visuals. Kept in its own file.</summary>
public partial class VisualizerWindow
{
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
        menu.Items.Add(Item("Previous visual", "B", PreviousPreset));
        // Whichever way B doesn't go (Options > Visuals picks), so both are a click away.
        menu.Items.Add(_settings.PreviousVisual == PreviousVisualMode.LastShown
            ? Item("Visual above in the list", null, () => PreviousPreset(PreviousVisualMode.UpInList))
            : Item("Last visual shown", null, () => PreviousPreset(PreviousVisualMode.LastShown)));
        if (!_isFullscreen)
        {
            // The picker opens from the bar under the visuals, which fullscreen hides.
            menu.Items.Add(Item("Choose a visual…", "V", TogglePresetPicker));
        }

        if (CurrentChoice() is { } current)
        {
            var favourite = Item("Favorite", "F", ToggleFavorite);
            favourite.IsChecked = current.IsFavorite;
            menu.Items.Add(favourite);
        }

        menu.Items.Add(MilkDropMenu());
        menu.Items.Add(AvsMenu());

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
            var record = Item("Record video…", "", () =>
            {
                // The recording window opens over the visuals, so come out of fullscreen for it.
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                }

                player.RecordVideo(this);
            });
            record.IsEnabled = player.CanRecordVideo;
            record.ToolTip = record.IsEnabled ? "Save the song drawn with a visual as an MP4 video, to share" : "Play a song first to record it";
            ToolTipService.SetShowOnDisabled(record, true);
            menu.Items.Add(record);
            menu.Items.Add(new Separator());
            player.AddGraphicsItems(menu);
        }

        Windows.LucentFullScreen.AddTo(menu);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Double-clicking the visuals switches fullscreen. A single click does nothing, so clicking
    /// the window to bring it to the front never stops the music.
    /// </summary>
    private void OnVisualsDoubleClicked()
    {
        ToggleFullscreen();
    }

    private static MenuItem Item(string header, string? keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = keys };
        item.Click += (_, _) => action();
        return item;
    }
}
