using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using WinampInspiredVisualizer.Playlist;

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
        _settings.ClassicSkin = WinampInspiredVisualizer.ClassicSkins.ClassicSkinLibrary.DefaultName;
        ApplyClassicSkin();
        var classic = _classicView!.Player;
        classic.PlayState = WinampInspiredVisualizer.ClassicSkins.ClassicPlayState.Playing;
        classic.Position = position;
        classic.Duration = _current?.Track.Duration ?? TimeSpan.Zero;
        classic.CanSeek = classic.Duration > TimeSpan.Zero;
        classic.IsShaded = shaded;
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
