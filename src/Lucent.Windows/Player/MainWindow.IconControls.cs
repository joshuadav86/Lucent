using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;

namespace Lucent;

// Previous, Play/Pause and Next on Lucent's icon: the buttons under the preview that shows when the
// pointer rests on the player's taskbar button, as Windows Media Player has. The preview already shows
// the song (the window's title). The Mac has the same commands, and the song, in its Dock menu.
public partial class MainWindow
{
    private ThumbButtonInfo? _iconPlayPause;

    private void BuildIconControls()
    {
        var previous = IconButton("Previous", "PreviousTrackIcon", PlayPrevious);
        var playPause = IconButton("Play", "PlayIcon", TogglePlayPause);
        var next = IconButton("Next", "NextTrackIcon", () => PlayNext(wrap: true));
        _iconPlayPause = playPause;

        TaskbarItemInfo = new TaskbarItemInfo
        {
            ThumbButtonInfos = new ThumbButtonInfoCollection { previous, playPause, next },
        };

        UpdateIconControls();
    }

    /// <summary>Shows Play or Pause on the taskbar's play button.</summary>
    private void UpdateIconControls()
    {
        if (_iconPlayPause is null)
        {
            return;
        }

        var playing = ShownIsPlaying;
        var description = playing ? "Pause" : "Play";
        if (_iconPlayPause.Description != description)
        {
            _iconPlayPause.Description = description;
            _iconPlayPause.ImageSource = IconImage(playing ? "PauseIcon" : "PlayIcon");
        }
    }

    private ThumbButtonInfo IconButton(string description, string icon, Action action)
    {
        var button = new ThumbButtonInfo { Description = description, ImageSource = IconImage(icon) };
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>
    /// One of the player's icons, white on a clear square: Windows draws the buttons on its own dark
    /// glass, whatever the skin.
    /// </summary>
    private ImageSource IconImage(string icon)
    {
        var geometry = (Geometry)FindResource(icon);
        var bounds = geometry.Bounds;
        var size = Math.Max(bounds.Width, bounds.Height) + 4;
        var frame = new RectangleGeometry(new Rect(
            bounds.X + (bounds.Width - size) / 2, bounds.Y + (bounds.Height - size) / 2, size, size));

        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null, frame));
        drawing.Children.Add(new GeometryDrawing(Brushes.White, null, geometry));
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
