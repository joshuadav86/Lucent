using Avalonia.Controls;
using Avalonia.Input;
using Lucent.Visualizer;

namespace Lucent.Mac;

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

        var menu = new ContextMenu();
        menu.Items.Add(Item(_isFullscreen ? "Leave fullscreen" : "Fullscreen",
            _isFullscreen ? new KeyGesture(Key.Escape) : FullscreenGesture, ToggleFullscreen));
        menu.Items.Add(new Separator());
        if (_isFullscreen && Player is { } playback)
        {
            // Fullscreen covers the player, so its controls come along in the menu.
            playback.AddPlaybackItems(menu);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(Item("Next visual", new KeyGesture(Key.N), () => ChangePreset(+1)));
        menu.Items.Add(Item("Previous visual", new KeyGesture(Key.B), PreviousPreset));
        // Whichever way B doesn't go (Options > Visuals picks), so both are a click away.
        menu.Items.Add(_settings.PreviousVisual == PreviousVisualMode.LastShown
            ? Item("Visual above in the list", null, () => PreviousPreset(PreviousVisualMode.UpInList))
            : Item("Last visual shown", null, () => PreviousPreset(PreviousVisualMode.LastShown)));
        if (!_isFullscreen)
        {
            // The picker opens from the bar under the visuals, which fullscreen hides.
            menu.Items.Add(Item("Choose a visual…", new KeyGesture(Key.V), TogglePresetPicker));
        }

        if (CurrentChoice() is { } current)
        {
            var favourite = Item("Favorite", new KeyGesture(Key.F), ToggleFavorite);
            favourite.ToggleType = MenuItemToggleType.CheckBox;
            favourite.IsChecked = current.IsFavorite;
            menu.Items.Add(favourite);
        }

        var originals = Item("Lucent originals", null, () =>
        {
            ShowLucentOriginals(!_settings.LucentVisuals);
            Player?.ShowOptionsState();
        });
        originals.ToggleType = MenuItemToggleType.CheckBox;
        originals.IsChecked = _settings.LucentVisuals;
        menu.Items.Add(originals);
        menu.Items.Add(MilkDropMenu());
        menu.Items.Add(AvsMenu());

        menu.Items.Add(new Separator());
        var auto = Item("Auto", new KeyGesture(Key.A), () =>
        {
            ToggleAutoCycle();
            Player?.ShowOptionsState();
        });
        auto.ToggleType = MenuItemToggleType.CheckBox;
        auto.IsChecked = _settings.AutoCycle;
        menu.Items.Add(auto);
        var often = new MenuItem { Header = "Change visual about every" };
        foreach (var seconds in AutoCycleChoices)
        {
            var choice = new MenuItem
            {
                Header = IntervalText(seconds),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = _settings.AutoCycle && seconds == _settings.AutoCycleSeconds,
            };
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
            var record = new MenuItem { Header = "Record video…", IsEnabled = player.CanRecordVideo };
            record.Click += (_, _) =>
            {
                // The recording window opens over the visuals, so come out of fullscreen for it.
                if (_isFullscreen)
                {
                    ToggleFullscreen();
                }

                player.RecordVideo(this);
            };
            ToolTip.SetTip(record, record.IsEnabled ? "Save the song drawn with a visual as an MP4 video, to share" : "Play a song first to record it");
            ToolTip.SetShowOnDisabled(record, true);
            menu.Items.Add(record);
            menu.Items.Add(new Separator());
            player.AddGraphicsItems(menu);
        }

        Windows.LucentFullScreen.AddTo(menu, this);
        menu.Open(VisualizerFrame);
    }

    /// <summary>⌃⌘F: the Mac's key for fullscreen, where Windows uses F11.</summary>
    internal static KeyGesture FullscreenGesture => new(Key.F, KeyModifiers.Control | KeyModifiers.Meta);

    /// <summary>
    /// Double-clicking the visuals switches fullscreen. A single click does nothing, so clicking
    /// the window to bring it to the front never stops the music.
    /// </summary>
    private void OnVisualsDoubleClicked()
    {
        ToggleFullscreen();
    }

    private static MenuItem Item(string header, KeyGesture? keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = keys };
        item.Click += (_, _) => action();
        return item;
    }
}
