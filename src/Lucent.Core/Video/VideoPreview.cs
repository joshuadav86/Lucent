using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Lucent.Video;

/// <summary>
/// A video's pictures without its sound, for the editor: shows the picture at any moment, or plays along with the
/// editor's own playback. Throws from the constructor when libVLC can't be loaded or can't read the file.
/// </summary>
/// <remarks>
/// libVLC doesn't show a picture for a move while paused, and a move made by playing on can show a picture or two
/// from before it (they're already on their way when it starts again). So to show a moment, it starts the video
/// again from that moment and pauses on the first picture. That's done on its own thread, which takes only the
/// newest moment asked for, so dragging across a video doesn't pile up starts.
/// </remarks>
public sealed class VideoPreview : IDisposable
{
    private enum Want
    {
        Show,
        Play,
        Pause,
    }

    private readonly LibVLC _libVlc;
    private readonly string _path;
    private readonly MediaPlayer _player;
    private readonly (int Width, int Height) _pictureSize;
    private readonly bool _rotated;
    private readonly object _gate = new();
    private readonly Thread _worker;
    private readonly ManualResetEventSlim _pictureCame = new();
    private Media _media;
    private (Want Kind, double Seconds)? _request;
    private bool _quit;
    private bool _disposed;
    private volatile bool _finding;
    private volatile bool _playing;
    private volatile bool _pauseOnPicture;
    private volatile bool _holding;

    public VideoPreview(string path)
    {
        _libVlc = VideoEngine.Get();
        _path = Path.GetFullPath(path);
        _media = new Media(_libVlc, _path, FromType.FromPath);
        // Off the UI thread: Parse finishes on libVLC's thread, and awaiting it here could wait on the UI thread forever.
        var parsed = Task.Run(() => _media.Parse(MediaParseOptions.ParseLocal, 10_000)).GetAwaiter().GetResult();
        if (parsed == MediaParsedStatus.Failed)
        {
            _media.Dispose();
            throw new InvalidDataException("This file can't be played: it may be damaged, or not a video.");
        }

        var video = _media.Tracks.Where(track => track.TrackType == TrackType.Video).Select(track => track.Data.Video).FirstOrDefault();
        _pictureSize = VideoFrames.SizeFor((int)video.Width, (int)video.Height, video.SarNum, video.SarDen);
        _rotated = (int)video.Orientation >= 4;
        _player = new MediaPlayer(_libVlc);
        _player.SetVideoFormatCallbacks(OnPictureFormat, OnPictureCleanup);
        _player.SetVideoCallbacks(OnPictureLock, null, OnPictureShown);
        _worker = new Thread(Work) { IsBackground = true, Name = "Video preview" };
        _worker.Start();
    }

    /// <summary>The pictures, as they're shown.</summary>
    public VideoFrames Frames { get; } = new();

    /// <summary>True while it plays along.</summary>
    public bool IsPlaying => _playing && _player.IsPlaying;

    /// <summary>True while it's still on its way to the picture <see cref="Show"/> asked for.</summary>
    public bool IsFinding => _finding;

    /// <summary>Where it is, in seconds.</summary>
    public double Time => Math.Max(0, _player.Time) / 1000.0;

    /// <summary>Shows the picture at <paramref name="seconds"/>, paused.</summary>
    public void Show(double seconds) => Ask(Want.Show, seconds);

    /// <summary>Plays from <paramref name="seconds"/>.</summary>
    public void Play(double seconds) => Ask(Want.Play, seconds);

    /// <summary>Stops where it is, keeping the picture.</summary>
    public void Pause() => Ask(Want.Pause, 0);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _quit = true;
            Monitor.Pulse(_gate);
        }

        _pictureCame.Set();
        _worker.Join();
        _player.Stop();
        _player.Dispose();
        _media.Dispose();
        _pictureCame.Dispose();
        Frames.Dispose();
    }

    private void Ask(Want kind, double seconds)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _request = (kind, seconds);
            _playing = kind == Want.Play;
            _finding = kind == Want.Show;
            Monitor.Pulse(_gate);
        }
    }

    // ---- Its own thread: does what was asked last ----

    private void Work()
    {
        while (true)
        {
            (Want Kind, double Seconds) request;
            lock (_gate)
            {
                while (_request is null && !_quit)
                {
                    Monitor.Wait(_gate);
                }

                if (_quit)
                {
                    return;
                }

                request = _request!.Value;
                _request = null;
            }

            try
            {
                switch (request.Kind)
                {
                    case Want.Show:
                        ShowNow(request.Seconds);
                        break;
                    case Want.Play:
                        PlayNow(request.Seconds);
                        break;
                    default:
                        _pauseOnPicture = false;
                        if (_player.State == VLCState.Playing)
                        {
                            _player.SetPause(true);
                        }

                        break;
                }
            }
            catch (Exception)
            {
                // libVLC refused (the file went away while saving, say): the last picture stays.
            }

            lock (_gate)
            {
                if (_request is null && request.Kind == Want.Show)
                {
                    _finding = false;
                }
            }
        }
    }

    // Starts again from the moment, and pauses on its first picture.
    private void ShowNow(double seconds)
    {
        _pictureCame.Reset();
        StartAt(seconds, pauseOnPicture: true);
        _pictureCame.Wait(TimeSpan.FromSeconds(3));
        if (!_quit)
        {
            _player.SetPause(true);
        }
    }

    private void PlayNow(double seconds)
    {
        _pauseOnPicture = false;
        _holding = false;
        if (_player.State is VLCState.Playing or VLCState.Paused)
        {
            // While playing, a picture from before the move doesn't matter: the next is a moment away.
            _player.Time = (long)(seconds * 1000);
            _player.SetPause(false);
        }
        else
        {
            StartAt(seconds, pauseOnPicture: false);
        }
    }

    private void StartAt(double seconds, bool pauseOnPicture)
    {
        _player.Stop();
        var media = new Media(_libVlc, _path, FromType.FromPath);
        media.AddOption(":no-audio");
        media.AddOption($":start-time={seconds:0.###}".Replace(',', '.'));
        _player.Media = media;
        _media.Dispose();
        _media = media;
        _holding = false;
        _pauseOnPicture = pauseOnPicture;
        _player.Play();
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
            // The preview's thread pauses it, not libVLC's own, which pausing waits for.
            _pauseOnPicture = false;
            _holding = true;
            _pictureCame.Set();
        }
    }
}
