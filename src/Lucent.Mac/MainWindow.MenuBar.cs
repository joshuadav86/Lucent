using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// The Mac menu bar: the app menu (About, Check for Updates, Options, Hide, Quit) and the player's main commands.
/// Every Lucent window shows the same menus, so they're there whichever window is in front.
/// </summary>
/// <remarks>
/// The player's one-key shortcuts (Space, Z, X, S, R, W, Q...) are left off the menus: a menu
/// item with a plain key would take that key from text boxes, such as the library's search. The
/// menus use ⌘ shortcuts, which macOS shows beside each item.
/// </remarks>
public partial class MainWindow
{
    private void BuildMenuBar()
    {
        if (Application.Current is { } app)
        {
            NativeMenu.SetMenu(app, AppMenu());
        }

        NativeMenu.SetMenu(this, MenuBar());
        foreach (var window in ToolWindows)
        {
            NativeMenu.SetMenu(window, MenuBar());
        }
    }

    /// <summary>The menu under Lucent's name.</summary>
    private NativeMenu AppMenu()
    {
        var menu = new NativeMenu();
        menu.Add(Item("About Lucent", ShowAbout));
        menu.Add(Item("Check for Updates…", CheckForUpdatesFromMenu));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Options…", OpenOptions, new KeyGesture(Key.OemComma, KeyModifiers.Meta)));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Hide Lucent", AppKit.HideApp, new KeyGesture(Key.H, KeyModifiers.Meta)));
        menu.Add(Item("Hide Others", AppKit.HideOtherApps, new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt)));
        menu.Add(Item("Show All", AppKit.ShowAllApps));
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(Item("Quit Lucent", Close, new KeyGesture(Key.Q, KeyModifiers.Meta)));
        return menu;
    }

    /// <summary>File, Playback, Window and Help.</summary>
    private NativeMenu MenuBar()
    {
        var bar = new NativeMenu();

        var file = new NativeMenu();
        file.Add(Item("Add Files…", () => AddFilesFromDialog(this), new KeyGesture(Key.O, KeyModifiers.Meta)));
        file.Add(Item("Add a Folder…", () => AddFolderFromDialog(this), new KeyGesture(Key.O, KeyModifiers.Meta | KeyModifiers.Shift)));
        file.Add(new NativeMenuItemSeparator());
        var editInfo = Item("Edit Info…", EditInfoForSelection, new KeyGesture(Key.I, KeyModifiers.Meta));
        file.Add(editInfo);
        file.Add(Item("Save the Queue as a Playlist…", () => SaveQueueAsPlaylist(this)));
        file.Add(new NativeMenuItemSeparator());
        file.Add(Item("Bandcamp and Beatport…", ShowStores));
        file.Add(Item("Import from Another Player…", ShowImport));
        bar.Add(new NativeMenuItem("File") { Menu = file });

        var playback = new NativeMenu();
        var play = Item("Play", TogglePlayPause);
        playback.Add(play);
        playback.Add(Item("Stop", StopPlayback, new KeyGesture(Key.OemPeriod, KeyModifiers.Meta)));
        playback.Add(Item("Next Track", () => PlayNext(wrap: true), new KeyGesture(Key.Right, KeyModifiers.Meta)));
        playback.Add(Item("Previous Track", PlayPrevious, new KeyGesture(Key.Left, KeyModifiers.Meta)));
        playback.Add(new NativeMenuItemSeparator());
        playback.Add(Item("Volume Up", () => VolumeSlider.Value = Math.Min(1, VolumeSlider.Value + 0.05), new KeyGesture(Key.Up, KeyModifiers.Meta)));
        playback.Add(Item("Volume Down", () => VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 0.05), new KeyGesture(Key.Down, KeyModifiers.Meta)));
        playback.Add(new NativeMenuItemSeparator());
        var shuffle = Check("Shuffle", ToggleShuffle);
        var repeat = Item("Repeat", CycleRepeat);
        var favorite = Check("Favorite Sound", ToggleTrackFavorite);
        playback.Add(shuffle);
        playback.Add(repeat);
        playback.Add(favorite);
        playback.Opening += (_, _) =>
        {
            play.Header = _player.IsPlaying ? "Pause" : "Play";
            shuffle.IsChecked = _settings.Shuffle;
            repeat.Header = _settings.Repeat switch
            {
                RepeatMode.All => "Repeat: Whole Queue",
                RepeatMode.One => "Repeat: This Track",
                _ => "Repeat: Off",
            };
            favorite.IsEnabled = _current is not null;
            favorite.IsChecked = _current is not null && IsFavoriteTrack(_current.Track.Path);
        };
        bar.Add(new NativeMenuItem("Playback") { Menu = playback });

        var visuals = new NativeMenu();
        visuals.Add(Item("Next Visual", () => _visuals.ChangePreset(+1)));
        visuals.Add(Item("Previous Visual", () => _visuals.ChangePreset(-1)));
        visuals.Add(Item("Choose a Visual…", _visuals.TogglePresetPicker));
        var auto = Check("Change on the Beat", () =>
        {
            _visuals.ToggleAutoCycle();
            AutoCycleSwitch.IsChecked = _settings.AutoCycle;
        });
        visuals.Add(auto);
        visuals.Add(Item("Favorite Visual", _visuals.ToggleFavorite));
        visuals.Add(new NativeMenuItemSeparator());
        visuals.Add(Item("Full Screen", _visuals.ToggleFullscreen, new KeyGesture(Key.F, KeyModifiers.Meta | KeyModifiers.Control)));
        visuals.Opening += (_, _) => auto.IsChecked = _settings.AutoCycle;
        bar.Add(new NativeMenuItem("Visuals") { Menu = visuals });

        var window = new NativeMenu();
        var showVisuals = Check("Visuals", () => ToggleWindow(_visuals));
        var showQueue = Check("Queue", () => ToggleWindow(_queue));
        var showLibrary = Check("Library", () => ToggleWindow(_libraryWindow));
        var showArt = Check("Artwork", ToggleArtwork);
        var showEqualizer = Check("Equalizer", ToggleEqualizer);
        var showAnalyzer = Check("Analyzer", ToggleAnalyzer);
        foreach (var item in new[] { showVisuals, showQueue, showLibrary, showArt, showEqualizer, showAnalyzer })
        {
            window.Add(item);
        }

        window.Add(new NativeMenuItemSeparator());
        window.Add(Item("Dock All Windows to the Player", ArrangeWindows));
        window.Add(Item("Fill the Screen", FillScreen));
        window.Add(Item("Shrink Everything", ShrinkWindows));
        window.Add(new NativeMenuItemSeparator());
        window.Add(Item("Minimize", () => WindowState = WindowState.Minimized, new KeyGesture(Key.M, KeyModifiers.Meta)));
        window.Opening += (_, _) =>
        {
            showVisuals.IsChecked = _visuals.IsVisible;
            showQueue.IsChecked = _queue.IsVisible;
            showLibrary.IsChecked = _libraryWindow.IsVisible;
            showArt.IsChecked = ArtworkWindow.IsOpen;
            showEqualizer.IsChecked = OpenEqualizerWindow is not null;
            showAnalyzer.IsChecked = AnalyzerWindow.IsOpen;
        };
        bar.Add(new NativeMenuItem("Window") { Menu = window });

        var help = new NativeMenu();
        help.Add(Item("Check for Updates…", CheckForUpdatesFromMenu));
        help.Add(new NativeMenuItemSeparator());
        help.Add(Item("Report a Bug…", BugReport.Open));
        help.Add(Item("Buy Me a Coffee…", SupportLink.Open));
        bar.Add(new NativeMenuItem("Help") { Menu = help });
        return bar;
    }

    private void OpenOptions()
    {
        Activate();
        if (!OptionsPopup.IsOpen)
        {
            ToggleOptions();
        }
    }

    private async void ShowAbout()
    {
        if (OperatingSystem.IsMacOS())
        {
            AppKit.ShowAboutPanel();
            return;
        }

        await MessageWindow.ShowAsync(this, $"Lucent {AppInfo.Version}", "About Lucent");
    }

    private static NativeMenuItem Item(string header, Action action, KeyGesture? gesture = null)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => action();
        return item;
    }

    private static NativeMenuItem Check(string header, Action action)
    {
        var item = Item(header, action);
        item.ToggleType = NativeMenuItemToggleType.CheckBox;
        return item;
    }
}
