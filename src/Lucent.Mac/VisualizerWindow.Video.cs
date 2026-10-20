using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WinampInspiredVisualizer.Video;

namespace Lucent.Mac;

/// <summary>
/// Videos, shown in place of the visuals while one plays: the same window, the same fullscreen, and the same
/// clicks (play or pause, double-click for fullscreen, right-click or Control-click for the menu). Kept in its own
/// file, as on Windows.
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
    public bool ShowingVideo => _videoImage is { IsVisible: true };

    /// <summary>
    /// Puts the visuals in a frame with room for a video in front of them. The visuals' Metal view would cover
    /// anything over it, so it's hidden (and stops drawing) while a video shows.
    /// </summary>
    private Control WithVideoLayer(Control visuals)
    {
        _videoImage = new Image { Stretch = Stretch.Uniform, Focusable = false, IsVisible = false };
        RenderOptions.SetBitmapInterpolationMode(_videoImage, BitmapInterpolationMode.MediumQuality);
        // The same clicks as on the visuals: a click plays or pauses, a double-click goes fullscreen.
        _videoImage.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(_videoImage).Properties.IsLeftButtonPressed && e.ClickCount == 2 && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                OnVisualsDoubleClicked();
                e.Handled = true;
            }
        };
        _videoImage.PointerReleased += (_, e) =>
        {
            switch (e.InitialPressMouseButton)
            {
                // On a Mac, Control-click is a right-click.
                case MouseButton.Left when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                case MouseButton.Right:
                    ShowVisualsMenu();
                    e.Handled = true;
                    break;
                case MouseButton.Left:
                    OnVisualsClicked();
                    break;
            }
        };
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
        if (_videoImage is null)
        {
            return;
        }

        var show = _video is { HasPicture: true } && !_visualsInsteadOfVideo;
        _videoImage.IsVisible = show;
        if (_host is not null)
        {
            _host.IsVisible = !show;
        }

        if (show && !_renderingVideo)
        {
            _renderingVideo = true;
            RequestAnimationFrame(OnVideoFrame);
        }
        else if (!show)
        {
            _renderingVideo = false;
            _videoImage.Source = null;
            _videoBitmap?.Dispose();
            _videoBitmap = null;
        }
    }

    // With every frame the screen shows: puts up the video's newest picture, when there's a new one.
    private void OnVideoFrame(TimeSpan time)
    {
        if (!_renderingVideo)
        {
            return;
        }

        RequestAnimationFrame(OnVideoFrame);
        if (_video is not { } video || _videoImage is null || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var frames = video.Frames;
        if (frames.Count == _videoFrameShown)
        {
            return;
        }

        var shown = frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_videoBitmap is null || _videoBitmap.PixelSize.Width != width || _videoBitmap.PixelSize.Height != height)
            {
                _videoBitmap?.Dispose();
                _videoBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                _videoImage.Source = _videoBitmap;
            }

            using (var target = _videoBitmap.Lock())
            {
                CopyRows(pixels, stride, target.Address, target.RowBytes, Math.Min(stride, target.RowBytes), height);
            }

            _videoFrameShown = frames.Count;
        });
        if (shown)
        {
            _videoImage.InvalidateVisual();
        }
    }

    private static unsafe void CopyRows(nint source, int sourceStride, nint target, int targetStride, int rowBytes, int rows)
    {
        if (sourceStride == targetStride)
        {
            Buffer.MemoryCopy((void*)source, (void*)target, (long)targetStride * rows, (long)sourceStride * rows);
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            Buffer.MemoryCopy((byte*)source + (long)row * sourceStride, (byte*)target + (long)row * targetStride, rowBytes, rowBytes);
        }
    }
}
