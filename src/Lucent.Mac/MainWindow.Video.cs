using WinampInspiredVisualizer.Video;

namespace Lucent.Mac;

/// <summary>Videos: the player plays their sound, and the visuals window shows their pictures. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>A video has started (or stopped) being the track: its pictures go up in the visuals window.</summary>
    private void OnVideoChanged()
    {
        var video = _player.Video;
        _visuals.ShowVideo(video);

        // A video is for watching: the visuals window opens for it if it was closed.
        if (video is { HasPicture: true } && !_visuals.IsVisible)
        {
            ToggleWindow(_visuals);
        }
    }
}
