using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinampInspiredVisualizer;

/// <summary>Playback controls, as items for the visuals' right-click menu. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>
    /// Adds what's playing, play or pause, stop, next and previous track, and a volume slider, so the
    /// music can be controlled without leaving fullscreen.
    /// </summary>
    public void AddPlaybackItems(ItemsControl menu)
    {
        if (_player.HasTrack && !string.IsNullOrWhiteSpace(NowPlayingText.Text))
        {
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = NowPlayingText.Text, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis },
                IsEnabled = false,
            });
        }

        menu.Items.Add(PlaybackItem(_player.IsPlaying ? "Pause" : "Play", "Space", TogglePlayPause));
        menu.Items.Add(PlaybackItem("Stop", "", StopPlayback));
        menu.Items.Add(PlaybackItem("Next track", "X", () => PlayNext(wrap: true)));
        menu.Items.Add(PlaybackItem("Previous track", "Z", PlayPrevious));
        menu.Items.Add(VolumeItem());
    }

    private MenuItem VolumeItem()
    {
        var label = new TextBlock { Width = 82, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = VolumeSlider.Value,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
        };

        void ShowVolume() => label.Text = $"Volume {Math.Round(slider.Value * 100)}%";
        ShowVolume();
        // The player's own slider does the work, so the volume is set and saved the same way.
        slider.ValueChanged += (_, e) =>
        {
            VolumeSlider.Value = e.NewValue;
            ShowVolume();
        };
        slider.PreviewMouseWheel += (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta) * 0.05, 0, 1);
            e.Handled = true;
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(slider);
        // Dragging the slider mustn't close the menu, so the volume can be set by ear.
        return new MenuItem { Header = row, StaysOpenOnClick = true };
    }

    private static MenuItem PlaybackItem(string header, string keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = keys };
        item.Click += (_, _) => action();
        return item;
    }
}
