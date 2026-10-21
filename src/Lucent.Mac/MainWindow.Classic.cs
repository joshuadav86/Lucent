using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.ClassicSkins;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

// Classic skins: the player drawn by a Winamp 2 skin (.wsz) instead of Lucent's own layout, with Lucent's buttons in a strip below it. The
// equalizer and queue take on the skin too. Lucent.Core's ClassicPlayer draws it; this feeds it the player's state and acts on its buttons.
public partial class MainWindow
{
    private ClassicPlayerView? _classicView;
    private StackPanel? _classicPanel;
    private Border? _classicToolbar;
    private readonly Dictionary<string, Bitmap> _classicPreviews = new(StringComparer.OrdinalIgnoreCase);

    private bool IsClassic => _settings.ClassicSkin is not null && _classicView is not null;

    /// <summary>Shows the player in the classic skin the settings name, or as Lucent's own player when there's none.</summary>
    private void ApplyClassicSkin()
    {
        if (_settings.ClassicSkin is not { } name)
        {
            if (_classicPanel is not null)
            {
                _classicPanel.IsVisible = false;
                MoveLucentButtons(toClassic: false);
            }

            LucentPlayer.IsVisible = true;
            ApplyPlayerSize();
            Topmost = false;
            SkinOtherWindows(null);
            return;
        }

        if (_classicView is null)
        {
            _classicView = new ClassicPlayerView(new ClassicPlayer(ClassicSkinLibrary.Default, _player.Capture));
            _classicView.Player.Command += OnClassicCommand;
            _classicView.Player.SeekRequested += fraction => SeekTo(ShownDuration * fraction);
            _classicView.Player.VolumeChanged += volume => VolumeSlider.Value = volume;
            _classicView.Player.BalanceChanged += SetBalance;
            var menu = new MenuFlyout();
            menu.Opening += (_, _) => BuildClassicMenu(menu);
            _classicView.ContextFlyout = menu;
            _classicToolbar = new Border
            {
                BorderThickness = new Thickness(1, 0, 1, 1),
                Padding = new Thickness(4, 3, 4, 3),
                Child = new WrapPanel(),
            };
            // In the theme's colours, following it when it changes.
            _classicToolbar.Bind(Border.BackgroundProperty, _classicToolbar.GetResourceObservable("TitleBarBrush"));
            _classicToolbar.Bind(Border.BorderBrushProperty, _classicToolbar.GetResourceObservable("WindowFrameBrush"));
            _classicPanel = new StackPanel { Children = { _classicView, _classicToolbar } };
            PlayerRoot.Children.Add(_classicPanel);
        }

        _classicView.Player.Skin = ClassicSkinLibrary.Load(name);
        SetClassicZoom();
        _classicPanel!.IsVisible = true;
        MoveLucentButtons(toClassic: true);
        LucentPlayer.IsVisible = false;
        ApplyPlayerSize();
        SizeToContent = SizeToContent.WidthAndHeight;
        SyncClassicPlayer();
    }

    private void SetClassicZoom()
    {
        var zoom = _settings.ClassicDoubleSize ? 2 : 1;
        _classicView!.Zoom = zoom;
        // The buttons wrap onto a second row rather than make the window wider than the skin.
        _classicToolbar!.Width = ClassicSprites.WindowWidth * zoom;
        SkinOtherWindows(_classicView.Player.Skin);
    }

    /// <summary>The equalizer and queue in the classic skin too (at the player's size), or back in Lucent's look with null.</summary>
    private void SkinOtherWindows(ClassicSkin? skin)
    {
        var zoom = _settings.ClassicDoubleSize ? 2 : 1;
        EqualizerWindow.ClassicSkin = skin is null ? null : (skin, zoom);
        EqualizerWindow.Current?.SetClassicSkin(skin, zoom);
        _queue.SetClassicSkin(skin, zoom);
    }

    /// <summary>A transport button on another classic window (the playlist's), done as the player's would.</summary>
    internal void RunClassicCommand(ClassicCommand command) => OnClassicCommand(command);

    /// <summary>
    /// Lucent's own buttons (Sources, audio tools, Options, and the Visuals, Library, Art and other window
    /// toggles) move into a strip under the classic player, so a skin never takes them away; and back again.
    /// </summary>
    private void MoveLucentButtons(bool toClassic)
    {
        var tools = (WrapPanel)_classicToolbar!.Child!;
        if (toClassic == (TitleButtons.Parent == tools))
        {
            return;
        }

        if (toClassic)
        {
            WindowTogglesHome.Children.Remove(WindowToggles);
            TitleBar.Children.Remove(TitleButtons);
            tools.Children.Add(WindowToggles);
            tools.Children.Add(TitleButtons);
        }
        else
        {
            tools.Children.Clear();
            WindowTogglesHome.Children.Add(WindowToggles);
            TitleBar.Children.Add(TitleButtons);
        }
    }

    /// <summary>Moves the classic player on a frame, with the player's state as it is now. Called every screen frame.</summary>
    private void TickClassicPlayer()
    {
        if (IsClassic && WindowState != WindowState.Minimized)
        {
            // A screenshot sets the classic player's state itself; the real player isn't playing.
            if (!_previewing)
            {
                SyncClassicPlayer();
            }

            _classicView!.Tick();
            var classic = _classicView.Player;
            _queue.ShowClassicState(classic.PlayState, classic.Position, classic.Duration, classic.ShowRemaining);
        }
    }

    private void SyncClassicPlayer()
    {
        var classic = _classicView!.Player;
        var hasTrack = ShownHasTrack;
        var playing = ShownIsPlaying;
        classic.PlayState = playing ? ClassicPlayState.Playing
            : hasTrack && ShownPosition > TimeSpan.Zero ? ClassicPlayState.Paused
            : ClassicPlayState.Stopped;
        classic.Position = ShownPosition;
        classic.Duration = ShownDuration;
        classic.CanSeek = hasTrack && ShownDuration > TimeSpan.Zero;
        classic.ShowRemaining = _settings.ShowRemainingTime;
        classic.Title = NowPlayingText.Text ?? string.Empty;
        var track = _remote is null ? _current?.Track : null;
        classic.Kbps = track?.Bitrate ?? 0;
        classic.SampleRate = track?.SampleRate ?? 0;
        classic.Channels = track?.Channels ?? (hasTrack ? 2 : 0);
        classic.Volume = VolumeSlider.Value;
        classic.Balance = _settings.Balance;
        classic.Shuffle = _settings.Shuffle;
        classic.Repeat = _settings.Repeat != RepeatMode.Off;
        classic.EqualizerOpen = OpenEqualizerWindow is not null;
        classic.PlaylistOpen = _queue.IsVisible;
        classic.AlwaysOnTop = Topmost;
        classic.DoubleSize = _settings.ClassicDoubleSize;
        classic.IsActive = IsActive;
        classic.Visualizer = _settings.Meter;
    }

    private void OnClassicCommand(ClassicCommand command)
    {
        switch (command)
        {
            case ClassicCommand.Previous:
                PlayPrevious();
                break;
            case ClassicCommand.Play:
                // As in Winamp, Play while playing starts the track again.
                if (ShownIsPlaying)
                {
                    SeekTo(TimeSpan.Zero);
                }
                else
                {
                    TogglePlayPause();
                }

                break;
            case ClassicCommand.Pause:
                if (ShownHasTrack)
                {
                    TogglePlayPause();
                }

                break;
            case ClassicCommand.Stop:
                StopPlayback();
                break;
            case ClassicCommand.Next:
                PlayNext(wrap: true);
                break;
            case ClassicCommand.Open:
                AddFilesFromDialog(this);
                break;
            case ClassicCommand.Shuffle:
                ToggleShuffle();
                break;
            case ClassicCommand.Repeat:
                CycleRepeat();
                break;
            case ClassicCommand.Equalizer:
                ToggleEqualizer();
                break;
            case ClassicCommand.Playlist:
                ToggleWindow(_queue);
                break;
            case ClassicCommand.Visuals:
                ToggleWindow(_visuals);
                break;
            case ClassicCommand.Menu:
                if (_classicView?.ContextFlyout is MenuFlyout menu)
                {
                    menu.ShowAt(_classicView, showAtPointer: true);
                }

                break;
            case ClassicCommand.Minimize:
                WindowState = WindowState.Minimized;
                break;
            case ClassicCommand.Shade:
                _classicView!.Player.IsShaded = !_classicView.Player.IsShaded;
                // Rolled up, the player is just its title strip.
                _classicToolbar!.IsVisible = !_classicView.Player.IsShaded;
                break;
            case ClassicCommand.Close:
                Close();
                break;
            case ClassicCommand.ToggleTimeMode:
                ToggleRemainingTime();
                break;
            case ClassicCommand.CycleVisualizer:
                CycleMeter();
                break;
            case ClassicCommand.AlwaysOnTop:
                Topmost = !Topmost;
                break;
            case ClassicCommand.TrackInfo:
                OnNowPlayingEditInfo(this, new RoutedEventArgs());
                break;
            case ClassicCommand.DoubleSize:
                ToggleClassicDoubleSize();
                break;
        }
    }

    private void SetBalance(double balance)
    {
        _settings.Balance = balance;
        _player.Equalizer.Balance = balance;
    }

    private void ToggleClassicDoubleSize()
    {
        _settings.ClassicDoubleSize = !_settings.ClassicDoubleSize;
        _settings.Save();
        if (_classicView is not null)
        {
            SetClassicZoom();
        }
    }

    /// <summary>Switches to a classic skin by name, or back to Lucent's own player with null.</summary>
    private void ChooseClassicSkin(string? name)
    {
        _settings.ClassicSkin = name;
        _settings.Save();
        ApplyClassicSkin();
        BuildClassicSkinSwatches();
    }

    /// <summary>The classic player's right-click menu (and its O button's).</summary>
    private void BuildClassicMenu(MenuFlyout menu)
    {
        menu.Items.Clear();
        var skins = new MenuItem { Header = "Skins" };
        foreach (var name in ClassicSkinLibrary.Names())
        {
            var item = new MenuItem
            {
                Header = name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = string.Equals(name, _settings.ClassicSkin, StringComparison.OrdinalIgnoreCase),
            };
            item.Click += (_, _) => ChooseClassicSkin(name);
            skins.Items.Add(item);
        }

        skins.Items.Add(new Separator());
        skins.Items.Add(MenuItemFor("Import a skin…", ImportClassicSkin));
        skins.Items.Add(MenuItemFor("Find skins at the Winamp Skin Museum", OpenSkinMuseum));
        if (_settings.ClassicSkin is { } current && !string.Equals(current, ClassicSkinLibrary.DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            skins.Items.Add(MenuItemFor($"Remove {current}", () => RemoveClassicSkin(current)));
        }

        menu.Items.Add(skins);
        menu.Items.Add(MenuItemFor("Lucent's own player", () => ChooseClassicSkin(null)));
        menu.Items.Add(new Separator());
        var doubleSize = MenuItemFor("Double size", ToggleClassicDoubleSize);
        doubleSize.ToggleType = MenuItemToggleType.CheckBox;
        doubleSize.IsChecked = _settings.ClassicDoubleSize;
        menu.Items.Add(doubleSize);
        var onTop = MenuItemFor("Always on top", () => Topmost = !Topmost);
        onTop.ToggleType = MenuItemToggleType.CheckBox;
        onTop.IsChecked = Topmost;
        menu.Items.Add(onTop);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("Add files…", () => AddFilesFromDialog(this)));
        menu.Items.Add(MenuItemFor("Add a folder…", () => AddFolderFromDialog(this)));
        menu.Items.Add(MenuItemFor("Sources…", () => ToggleSources(SourcesButton)));
        menu.Items.Add(MenuItemFor("Options…", ShowOptions));

        LucentFullScreen.AddTo(menu);
    }

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnImportClassicSkin(object? sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = false;
        ImportClassicSkin();
    }

    private async void ImportClassicSkin()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import a Winamp skin",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Winamp skins") { Patterns = new[] { "*.wsz", "*.zip" } },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            ImportClassicSkins(new[] { path });
        }
    }

    /// <summary>Imports skins (from the dialog or dropped on a window) and switches to the last one.</summary>
    private async void ImportClassicSkins(IEnumerable<string> paths)
    {
        string? imported = null;
        foreach (var path in paths)
        {
            try
            {
                imported = ClassicSkinLibrary.Import(path);
                _classicPreviews.Remove(imported);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                await MessageWindow.ShowAsync(this, $"Lucent couldn't use {Path.GetFileName(path)} as a skin.\n\n{ex.Message}",
                    "Import a skin");
            }
        }

        if (imported is not null)
        {
            ChooseClassicSkin(imported);
        }
    }

    private void RemoveClassicSkin(string name)
    {
        try
        {
            ClassicSkinLibrary.Remove(name);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        _classicPreviews.Remove(name);
        ChooseClassicSkin(ClassicSkinLibrary.DefaultName);
    }

    private void OnFindClassicSkins(object? sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = false;
        OpenSkinMuseum();
    }

    private static void OpenSkinMuseum()
    {
        try
        {
            Process.Start(new ProcessStartInfo(ClassicSkinLibrary.SkinMuseumUrl) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser to hand it to.
        }
    }

    /// <summary>The classic skins in the options panel's Look tab, each shown as a little picture of its player.</summary>
    private void BuildClassicSkinSwatches()
    {
        ClassicSkinSwatches.Children.Clear();
        foreach (var name in ClassicSkinLibrary.Names())
        {
            if (!_classicPreviews.TryGetValue(name, out var preview))
            {
                preview = ClassicPlayerView.Preview(ClassicSkinLibrary.Load(name));
                _classicPreviews[name] = preview;
            }

            var picture = new Image { Source = preview, Stretch = Stretch.Uniform, Height = 62 };
            RenderOptions.SetBitmapInterpolationMode(picture, BitmapInterpolationMode.HighQuality);
            var swatch = new RadioButton
            {
                Theme = (ControlTheme)this.FindResource("ThemeSwatch")!,
                GroupName = "ClassicSkin",
                Tag = name,
                Content = picture,
                IsChecked = string.Equals(name, _settings.ClassicSkin, StringComparison.OrdinalIgnoreCase),
            };
            ToolTip.SetTip(swatch, name);
            swatch.Click += (_, _) => ChooseClassicSkin(name);
            ClassicSkinSwatches.Children.Add(swatch);
        }
    }

    /// <summary>Takes the .wsz files out of a drop, importing them; returns what's left (the music).</summary>
    private List<string> TakeDroppedSkins(IEnumerable<string> paths)
    {
        var all = paths.ToList();
        var skins = all.Where(ClassicSkinLibrary.IsSkinFile).ToList();
        if (skins.Count > 0)
        {
            ImportClassicSkins(skins);
        }

        return all.Except(skins).ToList();
    }
}
