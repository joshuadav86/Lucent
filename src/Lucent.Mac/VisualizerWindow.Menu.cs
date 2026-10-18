using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Lucent.Mac;

/// <summary>The menus you get by clicking the visuals. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    private DispatcherTimer? _clickTimer;
    private bool _skipClick;
    private DateTime _activatedAt;

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
        menu.Items.Add(Item("Previous visual", new KeyGesture(Key.B), () => ChangePreset(-1)));
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

        menu.Items.Add(MilkDropMenu());

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

        menu.Open(VisualizerFrame);
    }

    /// <summary>⌃⌘F: the Mac's key for fullscreen, where Windows uses F11.</summary>
    internal static KeyGesture FullscreenGesture => new(Key.F, KeyModifiers.Control | KeyModifiers.Meta);

    /// <summary>
    /// A left click on the visuals plays or pauses the music, in a window or fullscreen. It waits
    /// out the double-click time first, so double-clicking still just switches fullscreen, and the
    /// click that brings Lucent to the front doesn't count.
    /// </summary>
    private void OnVisualsClicked()
    {
        if (_skipClick)
        {
            // The button coming up after a double-click.
            _skipClick = false;
            return;
        }

        if (Player is null || DateTime.UtcNow - _activatedAt < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        if (_clickTimer is null)
        {
            _clickTimer = new DispatcherTimer { Interval = DoubleClickTime() };
            _clickTimer.Tick += (_, _) =>
            {
                _clickTimer.Stop();
                Player?.PlayOrPause();
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

    /// <summary>The longest gap between the clicks of a double-click, as set in System Settings.</summary>
    private TimeSpan DoubleClickTime()
    {
        var time = PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.Zero;
        return time > TimeSpan.Zero ? time : TimeSpan.FromMilliseconds(500);
    }

    private static MenuItem Item(string header, KeyGesture keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = keys };
        item.Click += (_, _) => action();
        return item;
    }
}
