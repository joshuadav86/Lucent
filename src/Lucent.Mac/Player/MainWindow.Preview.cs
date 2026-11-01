using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Lucent.Layout;
using Lucent.Mac.Windows;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Lucent.Playlist;

namespace Lucent.Mac;

/// <summary>For the screenshots (Lucent --screenshots): a player that looks like it's playing, without any sound.</summary>
public partial class MainWindow
{
    // Set for screenshots, so the meter keeps its made-up levels instead of falling silent.
    private bool _previewing;

    /// <summary>Queues the tracks and shows the first as playing, part way through, with the meter moving.</summary>
    internal void ShowPreview(IReadOnlyList<Track> tracks, TimeSpan position)
    {
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        if (_playlist.Count > 0)
        {
            _current = _playlist[0];
            _current.IsCurrent = true;
            ShowNowPlaying(_current.Track);
        }

        TimeText.Text = TimeFormat.Format(position);
        PlayStateIcon.Data = (Geometry)this.FindResource("PlaySmallIcon")!;
        PlayPauseIcon.Data = (Geometry)this.FindResource("PauseIcon")!;
        PlayPauseIcon.Margin = new Avalonia.Thickness(0);
        var duration = _current?.Track.Duration ?? TimeSpan.Zero;
        if (duration > TimeSpan.Zero)
        {
            _updatingSeekBar = true;
            SeekBar.IsEnabled = true;
            SeekBar.Maximum = duration.TotalSeconds;
            SeekBar.Value = Math.Min(position.TotalSeconds, duration.TotalSeconds);
            _updatingSeekBar = false;
        }

        // The timer would put the real (silent) player's time back.
        _positionTimer.Stop();
        _previewing = true;
        _meter.ShowSample();
    }

    /// <summary>
    /// Single window, <paramref name="width"/> by <paramref name="height"/>, with the visuals, queue, library and
    /// equalizer as panels in it (and the artwork too with <paramref name="artwork"/>), laid out with <paramref name="style"/>.
    /// </summary>
    internal void PreviewSingleWindow(double width, double height, Lucent.Layout.TileStyle style, bool artwork = false)
    {
        ShowTool(_visuals);
        ShowTool(_queue);
        ShowTool(_libraryWindow);
        if (OpenEqualizerWindow is null)
        {
            OpenEqualizer();
        }

        if (artwork && !ArtworkWindow.IsOpen)
        {
            ToggleArtwork();
        }

        _settings.SingleWindowStyle = style;
        _settings.SingleWindowSwaps.Clear();
        _settings.SingleWindowDividers.Clear();
        _settings.SingleWindowSizes.Clear();
        _settings.SingleWindowPlace = new Lucent.Settings.WindowBounds { Left = 0, Top = 0, Width = width, Height = height };
        EnterSingleWindow();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Width = width;
        Height = height;
        UpdateLayout();
        Retile();
        UpdateLayout();
    }

    /// <summary>
    /// The single window rearranged with the mouse, as someone would: the library dragged by its title bar onto
    /// the visuals to swap them, then the player's column dragged 140 points, then the whole arrangement kept as a
    /// saved layout and opened again. Says what happened.
    /// </summary>
    internal string PreviewRearrangeSingleWindow()
    {
        var notes = new List<string>();
        if (_paneArea is null || _panes.Values.FirstOrDefault(pane => KeyOf(pane) == "Library") is not { } library)
        {
            return "not a single window with the library";
        }

        Point InWindow(double x, double y) => _paneArea.TranslatePoint(new Point(x, y), this) ?? new Point(x, y);
        var caption = library.Content.GetVisualDescendants().OfType<Control>().First(Chrome.GetIsCaption);
        var press = caption.TranslatePoint(new Point(8, caption.Bounds.Height / 2), this) ?? default;
        var visuals = _laidOut!.Places["Visuals"];
        var drop = InWindow(visuals.Left + visuals.Width / 2, visuals.Top + visuals.Height / 2);
        this.MouseDown(press, MouseButton.Left);
        this.MouseMove(new Point(press.X + 20, press.Y + 20));
        this.MouseMove(drop);
        notes.Add($"outline over the visuals: {_swapOutline is not null && Canvas.GetLeft(_swapOutline) == visuals.Left}");
        this.MouseUp(drop, MouseButton.Left);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        notes.Add($"swapped: {string.Join(", ", _settings.SingleWindowSwaps.Select(swap => string.Join("<>", swap)))}");

        var grip = _grips.FirstOrDefault(entry => entry.Tag is GripFor { Line.Carries.Count: > 0 })
            ?? _grips.FirstOrDefault(entry => entry.Tag is GripFor { Line.Across: false });
        if (grip?.Tag is GripFor { Line: var moving })
        {
            var at = InWindow(moving.Position, (moving.Start + moving.End) / 2);
            this.MouseDown(at, MouseButton.Left);
            this.MouseMove(new Point(at.X + 70, at.Y));
            this.MouseMove(new Point(at.X + 140, at.Y));
            this.MouseUp(new Point(at.X + 140, at.Y), MouseButton.Left);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            notes.Add($"line between {string.Join("+", moving.Before)} and {string.Join("+", moving.After)} moved to " +
                $"{string.Join(", ", _settings.SingleWindowDividers.Select(divider => divider.Offset.ToString("0")))}");
            notes.Add($"now: {string.Join(", ", _laidOut!.Places.Select(entry => $"{entry.Key} {entry.Value.Left:0},{entry.Value.Top:0} {entry.Value.Width:0}x{entry.Value.Height:0}"))}");
        }
        else
        {
            notes.Add("no line to drag");
        }

        // Kept as a saved layout, and opened again from separate windows.
        var before = string.Join(", ", _laidOut!.Places.Select(entry => $"{entry.Key} {entry.Value.Left:0},{entry.Value.Top:0} {entry.Value.Width:0}x{entry.Value.Height:0}"));
        var saved = CurrentLayout("Rearranged");
        LeaveSingleWindow();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        OpenLayout(saved);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Width = 1600;
        Height = 900;
        UpdateLayout();
        Retile();
        var after = _laidOut is null ? "none" : string.Join(", ", _laidOut.Places.Select(entry => $"{entry.Key} {entry.Value.Left:0},{entry.Value.Top:0} {entry.Value.Width:0}x{entry.Value.Height:0}"));
        notes.Add($"saved layout opened again: single window {InSingleWindow}, same panels {before == after}{(before == after ? "" : $" ({after})")}");
        UpdateLayout();
        return string.Join("\n", notes);
    }

    /// <summary>The player's capture, for the screenshots to play a song into.</summary>
    internal Lucent.Audio.AudioCapture PreviewCapture => _player.Capture;

    /// <summary>Moves a preview on <paramref name="deltaTime"/> seconds, to <paramref name="position"/>, with the meter following the capture.</summary>
    internal void PreviewStep(TimeSpan position, float deltaTime)
    {
        TimeText.Text = TimeFormat.Format(position);
        _updatingSeekBar = true;
        SeekBar.Value = Math.Min(position.TotalSeconds, SeekBar.Maximum);
        _updatingSeekBar = false;
        _meter.Tick(deltaTime);
    }

    /// <summary>Shows <paramref name="station"/> as on, playing <paramref name="song"/>, for <paramref name="heard"/> so far.</summary>
    internal void PreviewStation(Lucent.Radio.RadioStation station, string song, string details, TimeSpan heard)
    {
        _station = station;
        _radioList = new[] { station };
        SetMarquee(song);
        TrackDetails.Text = details;
        TimeText.Text = TimeFormat.Format(heard);
        PlayStateIcon.Data = (Geometry)this.FindResource("PlaySmallIcon")!;
        PlayPauseIcon.Data = (Geometry)this.FindResource("PauseIcon")!;
        PlayPauseIcon.Margin = new Avalonia.Thickness(0);
        SeekBar.IsEnabled = false;
        _positionTimer.Stop();
        _previewing = true;
        _meter.ShowSample();
        UpdateTrackFavoriteButton();
    }

    /// <summary>Shows Pandora's thumbs in shuffle and repeat's place, as while Lucent controls Pandora, with <paramref name="rating"/> lit.</summary>
    internal void PreviewThumbs(Lucent.Streaming.SongRating rating, string details)
    {
        TrackDetails.Text = details;
        ShuffleButton.IsVisible = RepeatButton.IsVisible = false;
        ThumbUpButton.IsVisible = ThumbDownButton.IsVisible = true;
        ThumbUpButton.IsChecked = rating == Lucent.Streaming.SongRating.Up;
        ThumbDownButton.IsChecked = rating == Lucent.Streaming.SongRating.Down;
    }

    /// <summary>Puts on a skin (with its own theme), as picking it in the options does.</summary>
    internal void PreviewSkin(string skin)
    {
        _settings.Skin = skin;
        _settings.Theme = Themes.SkinManager.Find(skin).Theme;
        ApplyLook();
    }

    /// <summary>Draws the player with Lucent Classic, playing the first track at <paramref name="position"/>.</summary>
    internal void PreviewClassicSkin(TimeSpan position, bool shaded)
    {
        _settings.ClassicSkin = Lucent.ClassicSkins.ClassicSkinLibrary.DefaultName;
        ApplyClassicSkin();
        var classic = _classicView!.Player;
        classic.PlayState = Lucent.ClassicSkins.ClassicPlayState.Playing;
        classic.Position = position;
        classic.Duration = _current?.Track.Duration ?? TimeSpan.Zero;
        classic.CanSeek = classic.Duration > TimeSpan.Zero;
        classic.IsShaded = shaded;
        _classicToolbar!.IsVisible = !shaded;
        _classicView.Tick();
    }

    /// <summary>Takes the options panel out of its pop-up, on a tab, so a screenshot can show it in a window of its own.</summary>
    internal Control TakeOptionsPanel(string tab)
    {
        ShowOptionValues();
        UpdateHdrHint();
        foreach (var button in OptionsPanel.GetLogicalDescendants().OfType<RadioButton>().Where(button => button.GroupName == "OptionsTab"))
        {
            button.IsChecked = (string?)button.Tag == tab;
        }

        GraphicsTab.IsVisible = tab == "Graphics";
        VisualsTab.IsVisible = tab == "Visuals";
        SoundTab.IsVisible = tab == "Sound";
        ThemeTab.IsVisible = tab == "Theme";
        UpdatesTab.IsVisible = tab == "Updates";
        LastfmTab.IsVisible = tab == "Lastfm";
        LibraryTab.IsVisible = tab == "Library";
        BuildSkinSwatches(Themes.ThemeManager.Find(_settings.Theme));
        BuildClassicSkinSwatches();
        var panel = OptionsPopup.Child!;
        OptionsPopup.Child = null;
        return panel;
    }

    /// <summary>The Sources panel, out of its popup so a screenshot can show it.</summary>
    internal Control TakeSourcesPanel()
    {
        UpdateSourcesPanel();
        var panel = SourcesPopup.Child!;
        SourcesPopup.Child = null;
        return panel;
    }
}
