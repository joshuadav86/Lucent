using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// A video's pictures without its sound, for the editor: shows the picture at any moment, or plays along with the
/// editor's own playback. Throws from the constructor when libVLC can't be loaded or can't read the file.
/// </summary>
public sealed class VideoPreview : IDisposable
{
    private readonly LibVLC _libVlc;
    private readonly Media _media;
    private readonly MediaPlayer _player;
    private readonly (int Width, int Height) _pictureSize;
    private readonly bool _rotated;
    private bool _started;
    private bool _disposed;
    private volatile bool _pauseOnPicture;
    private volatile bool _holding;

    public VideoPreview(string path)
    {
        _libVlc = VideoEngine.Get();
        _media = new Media(_libVlc, Path.GetFullPath(path), FromType.FromPath);
        // Off the UI thread: Parse finishes on libVLC's thread, and awaiting it here could wait on the UI thread forever.
        var parsed = Task.Run(() => _media.Parse(MediaParseOptions.ParseLocal, 10_000)).GetAwaiter().GetResult();
        if (parsed == MediaParsedStatus.Failed)
        {
            _media.Dispose();
            throw new InvalidDataException("This file can't be played: it may be damaged, or not a video.");
        }

        _media.AddOption(":no-audio");
        var video = _media.Tracks.Where(track => track.TrackType == TrackType.Video).Select(track => track.Data.Video).FirstOrDefault();
        _pictureSize = VideoFrames.SizeFor((int)video.Width, (int)video.Height, video.SarNum, video.SarDen);
        _rotated = (int)video.Orientation >= 4;
        _player = new MediaPlayer(_media);
        _player.SetVideoFormatCallbacks(OnPictureFormat, OnPictureCleanup);
        _player.SetVideoCallbacks(OnPictureLock, null, OnPictureShown);
    }

    /// <summary>The pictures, as they're shown.</summary>
    public VideoFrames Frames { get; } = new();

    /// <summary>True while it plays along.</summary>
    public bool IsPlaying => _player.IsPlaying;

    /// <summary>Where it is, in seconds.</summary>
    public double Time => Math.Max(0, _player.Time) / 1000.0;

    /// <summary>Shows the picture at <paramref name="seconds"/>, paused.</summary>
    public void Show(double seconds)
    {
        if (!Start(seconds, playing: false))
        {
            // libVLC doesn't show a picture for a move while paused, so it plays until the next picture shows.
            _holding = false;
            _pauseOnPicture = true;
            _player.Time = (long)(seconds * 1000);
            _player.SetPause(false);
        }
    }

    /// <summary>Plays from <paramref name="seconds"/>.</summary>
    public void Play(double seconds)
    {
        _pauseOnPicture = false;
        _holding = false;
        if (!Start(seconds, playing: true))
        {
            _player.Time = (long)(seconds * 1000);
            _player.SetPause(false);
        }
    }

    /// <summary>Stops where it is, keeping the picture.</summary>
    public void Pause()
    {
        if (_started)
        {
            _player.SetPause(true);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _player.Stop();
        _player.Dispose();
        _media.Dispose();
        Frames.Dispose();
    }

    // The first time, playing starts from the moment asked for. Returns false once it's already started.
    private bool Start(double seconds, bool playing)
    {
        if (_started || _disposed)
        {
            return _disposed;
        }

        _started = true;
        _media.AddOption($":start-time={seconds:0.###}".Replace(',', '.'));
        // To show one picture, it plays until that picture shows (see Show).
        _pauseOnPicture = !playing;

        _player.Play();
        return true;
    }

    // ---- The pictures (as in VideoStream) ----

    private uint OnPictureFormat(ref nint opaque, nint chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var (w, h) = _pictureSize;
        if (w == 0 || h == 0)
        {
            (w, h) = VideoFrames.SizeFor((int)width, (int)height, 1, 1);
        }
        else if (_rotated)
        {
            (w, h) = (h, w);
        }

        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        width = (uint)w;
        height = (uint)h;
        pitches = (uint)(w * 4);
        lines = (uint)h;
        Frames.SetSize(w, h);
        return 1;
    }

    private void OnPictureCleanup(ref nint opaque)
    {
    }

    private nint OnPictureLock(nint opaque, nint planes)
    {
        Marshal.WriteIntPtr(planes, Frames.BeginPicture());
        return 0;
    }

    private void OnPictureShown(nint opaque, nint picture)
    {
        if (_holding)
        {
            // Shown on the way to pausing, after the picture asked for: that one stays.
            return;
        }

        Frames.FinishPicture();
        if (_pauseOnPicture)
        {
            // Not on libVLC's own thread, which pausing waits for.
            _pauseOnPicture = false;
            _holding = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (!_disposed && !_pauseOnPicture)
                {
                    _player.SetPause(true);
                }
            });
        }
    }
}
