using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Playlist;

namespace Lucent.Mac;

/// <summary>Playback controls, as items for the visuals' right-click menu. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>Plays or pauses, as the Space key does. Clicking a video uses it.</summary>
    public void PlayOrPause() => TogglePlayPause();

    /// <summary>
    /// Adds what's playing, a bar to move through the song, play or pause, stop, next and previous
    /// track, and a volume slider, so the
    /// music can be controlled without leaving fullscreen.
    /// </summary>
    public void AddPlaybackItems(ItemsControl menu)
    {
        if (ShownHasTrack && !string.IsNullOrWhiteSpace(NowPlayingText.Text))
        {
            menu.Items.Add(new MenuItem
            {
                Header = new TextBlock { Text = NowPlayingText.Text, MaxWidth = 280, TextTrimming = TextTrimming.CharacterEllipsis },
                IsEnabled = false,
            });
        }

        if (ShownHasTrack && ShownDuration > TimeSpan.Zero)
        {
            menu.Items.Add(SeekItem());
        }

        menu.Items.Add(PlaybackItem(ShownIsPlaying ? "Pause" : "Play", "Space", TogglePlayPause));
        menu.Items.Add(PlaybackItem("Stop", "", StopPlayback));
        menu.Items.Add(PlaybackItem("Next track", "X", () => PlayNext(wrap: true)));
        menu.Items.Add(PlaybackItem("Previous track", "Z", PlayPrevious));
        menu.Items.Add(VolumeItem());
    }

    private MenuItem SeekItem()
    {
        var duration = ShownDuration;
        var time = new TextBlock { Width = 82, VerticalAlignment = VerticalAlignment.Center };
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = duration.TotalSeconds,
            Value = Math.Min(ShownPosition.TotalSeconds, duration.TotalSeconds),
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Theme = (ControlTheme)this.FindResource("SeekSlider")!,
        };

        var dragging = false;
        var updating = false;
        void ShowTime() => time.Text = TimeFormat.Format(TimeSpan.FromSeconds(slider.Value));
        ShowTime();

        // The bar follows the song while the menu is open, except while it's being dragged.
        var follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        follow.Tick += (_, _) =>
        {
            if (dragging || !ShownHasTrack)
            {
                return;
            }

            updating = true;
            slider.Value = Math.Min(ShownPosition.TotalSeconds, slider.Maximum);
            updating = false;
            ShowTime();
        };
        slider.Loaded += (_, _) => follow.Start();
        slider.Unloaded += (_, _) => follow.Stop();

        // Like the player's own bar: dragging shows the time and jumps when let go, a click jumps
        // straight there, and scrolling skips 5 seconds.
        void Start() => dragging = true;
        void Finish()
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            SeekTo(TimeSpan.FromSeconds(slider.Value));
        }

        slider.AddHandler(PointerPressedEvent, (_, _) => Start(), RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(PointerReleasedEvent, (_, _) => Finish(), RoutingStrategies.Tunnel, handledEventsToo: true);
        slider.AddHandler(PointerCaptureLostEvent, (_, _) => Finish(), handledEventsToo: true);
        slider.AddHandler(Thumb.DragStartedEvent, (_, _) => Start());
        slider.AddHandler(Thumb.DragCompletedEvent, (_, _) => Finish());
        slider.ValueChanged += (_, e) =>
        {
            ShowTime();
            if (!updating && !dragging)
            {
                SeekTo(TimeSpan.FromSeconds(e.NewValue));
            }
        };
        slider.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta.Y) * 5, 0, slider.Maximum);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(time);
        row.Children.Add(slider);
        return new MenuItem { Header = row, StaysOpenOnClick = true };
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
            Theme = (ControlTheme)this.FindResource("VolumeSlider")!,
        };

        void ShowVolume() => label.Text = $"Volume {Math.Round(slider.Value * 100)}%";
        ShowVolume();
        // The player's own slider does the work, so the volume is set and saved the same way.
        slider.ValueChanged += (_, e) =>
        {
            VolumeSlider.Value = e.NewValue;
            ShowVolume();
        };
        slider.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            slider.Value = Math.Clamp(slider.Value + Math.Sign(e.Delta.Y) * 0.05, 0, 1);
            e.Handled = true;
        }, RoutingStrategies.Tunnel);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(slider);
        // Dragging the slider mustn't close the menu, so the volume can be set by ear.
        return new MenuItem { Header = row, StaysOpenOnClick = true };
    }

    private static MenuItem PlaybackItem(string header, string keys, Action action)
    {
        var item = new MenuItem { Header = header };
        if (keys.Length > 0)
        {
            item.InputGesture = KeyGesture.Parse(keys);
        }

        item.Click += (_, _) => action();
        return item;
    }
}
