using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer;

/// <summary>
/// Videos, shown in place of the visuals while one plays: the same window, the same fullscreen, and the same
/// clicks (play or pause, double-click for fullscreen, right-click for the menu). Kept in its own file.
/// </summary>
public partial class VisualizerWindow
{
    private Image? _videoImage;
    private WriteableBitmap? _videoBitmap;
    private VideoStream? _video;
    private long _videoFrameShown = -1;
    private bool _renderingVideo;

    // The visuals stand in for the video while this is on (from the menu), until the next video.
    private bool _visualsInsteadOfVideo;

    /// <summary>True while a video's pictures are showing here.</summary>
    public bool ShowingVideo => _videoImage is { Visibility: Visibility.Visible };

    /// <summary>
    /// Puts the visuals in a frame with room for a video in front of them. The visuals' native window would cover
    /// anything over it, so it's hidden (and stops drawing) while a video shows.
    /// </summary>
    private UIElement WithVideoLayer(UIElement visuals)
    {
        _videoImage = new Image { Stretch = Stretch.Uniform, Focusable = false, Visibility = Visibility.Collapsed };
        RenderOptions.SetBitmapScalingMode(_videoImage, BitmapScalingMode.Linear);
        // The same clicks as on the visuals: a click plays or pauses, a double-click goes fullscreen.
        _videoImage.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                OnVisualsDoubleClicked();
            }
        };
        _videoImage.MouseLeftButtonUp += (_, _) => OnVisualsClicked();
        _videoImage.MouseRightButtonUp += (_, _) => ShowVisualsMenu();
        var layers = new Grid { Background = Brushes.Black };
        layers.Children.Add(visuals);
        layers.Children.Add(_videoImage);
        return layers;
    }

    /// <summary>The video playing, or null: its pictures are shown here in place of the visuals.</summary>
    public void ShowVideo(VideoStream? video)
    {
        if (ReferenceEquals(video, _video))
        {
            return;
        }

        _video = video;
        _videoFrameShown = -1;
        _visualsInsteadOfVideo = false;
        UpdateVideo();
    }

    /// <summary>Switches between the video and the visuals, while a video plays.</summary>
    public void ToggleVisualsInsteadOfVideo()
    {
        _visualsInsteadOfVideo = !_visualsInsteadOfVideo;
        UpdateVideo();
    }

    private void UpdateVideo()
    {
        var show = _video is { HasPicture: true } && !_visualsInsteadOfVideo;
        if (_videoImage is null)
        {
            return;
        }

        _videoImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (_host is not null)
        {
            _host.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        }

        if (show && !_renderingVideo)
        {
            _renderingVideo = true;
            CompositionTarget.Rendering += OnVideoFrame;
        }
        else if (!show && _renderingVideo)
        {
            _renderingVideo = false;
            CompositionTarget.Rendering -= OnVideoFrame;
            _videoImage.Source = null;
            _videoBitmap = null;
        }
    }

    // With every frame the screen shows: puts up the video's newest picture, when there's a new one.
    private void OnVideoFrame(object? sender, EventArgs e)
    {
        if (_video is not { } video || _videoImage is null || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var frames = video.Frames;
        if (frames.Count == _videoFrameShown)
        {
            return;
        }

        frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_videoBitmap is null || _videoBitmap.PixelWidth != width || _videoBitmap.PixelHeight != height)
            {
                _videoBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                _videoImage.Source = _videoBitmap;
            }

            _videoBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride * height, stride);
            _videoFrameShown = frames.Count;
        });
    }
}
