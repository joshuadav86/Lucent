using Avalonia.Interactivity;
using Avalonia.Threading;
using Lucent.Video;

namespace Lucent.Mac;

/// <summary>
/// Videos: the player plays their sound, and the video window shows their pictures, beside the visuals. Kept in its own file.
/// </summary>
public partial class MainWindow
{
    // The video window opened itself for a video, so it closes itself again once no video plays.
    private bool _videoWindowOpenedItself;
    private DispatcherTimer? _closeVideoTimer;

    /// <summary>A video has started (or stopped) being the track: its pictures go up in the video window.</summary>
    private void OnVideoChanged()
    {
        var video = _player.Video;
        VideoWindow.ShowVideo(video, video?.IsLive == true ? _station?.Name : null);
        _closeVideoTimer?.Stop();

        if (video is { HasPicture: true })
        {
            // A video is for watching: the video window opens for it if it was closed.
            if (!VideoWindow.IsOpen)
            {
                ToggleVideo();
                _videoWindowOpenedItself = true;
            }
        }
        else if (video is null && _videoWindowOpenedItself && VideoWindow.IsOpen)
        {
            // Not straight away: the next track in the queue may well be another video.
            _closeVideoTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _closeVideoTimer.Tick -= OnCloseVideoTimer;
            _closeVideoTimer.Tick += OnCloseVideoTimer;
            _closeVideoTimer.Start();
        }
    }

    private void OnCloseVideoTimer(object? sender, EventArgs e)
    {
        _closeVideoTimer?.Stop();
        if (_player.Video is null && _videoWindowOpenedItself && VideoWindow.IsOpen)
        {
            ToggleVideo();
        }
    }

    private void OnToggleVideo(object? sender, RoutedEventArgs e) => ToggleVideo();

    private void ToggleVideo() => VideoToggle.IsChecked = VideoWindow.Toggle(this, _settings);

    private void OnVideoOpenChanged(object? sender, EventArgs e)
    {
        VideoToggle.IsChecked = VideoWindow.IsOpen;
        // Opened or closed by hand from here on: it's the person's window now.
        _videoWindowOpenedItself = false;
    }
}
