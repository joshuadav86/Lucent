using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace Lucent.Video;

/// <summary>
/// Reads every picture of a video, in the order they show, as fast as libVLC decodes them, each with when it shows,
/// for saving as images, a GIF or a smaller copy. Pictures come as 32-bit BGRA (blue, green, red, then a byte that's
/// always opaque), top row first, turned the right way up for a video filmed on its side.
/// </summary>
/// <remarks>
/// <para>
/// The pictures don't go to a screen: libVLC's transcoder decodes them, scales them, and hands each over through its
/// "smem" output with its timestamp. Nothing there waits for a clock, so it goes as fast as the processor decodes, and
/// every picture says when it shows, so nothing has to be counted. (Pictures drawn for a screen, as before, were
/// sometimes shown twice or skipped as playback started, and could only be told apart by counting, which on a long
/// stretch of a big video almost never came out the same twice.)
/// </para>
/// <para>
/// The times are from the first picture, which is the video's first (a keyframe, always decoded). libVLC can still
/// leave out a picture now and then (the first after the keyframe, as it starts, and the last few, as it ends), so
/// callers read from a keyframe early and past the end where they can, and check for gaps by time.
/// </para>
/// </remarks>
public static class VideoPictureReader
{
    // How long libVLC may go without a picture before it counts as stuck.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);

    // After libVLC says the video's ended, a last picture may still be on its way.
    private static readonly TimeSpan EndQuiet = TimeSpan.FromMilliseconds(300);

    /// <summary>The size libVLC shows a video's pictures at (square pixels, no bigger than 3840 × 2160), or (0, 0).</summary>
    public static (int Width, int Height) SizeOf(LibVLC libVlc, string path)
    {
        var (width, height, orientation) = Describe(libVlc, path);
        return Turned(orientation) ? (height, width) : (width, height);
    }

    /// <summary>
    /// Reads the video's pictures, scaled to <paramref name="width"/> × <paramref name="height"/> (as it shows, so
    /// already turned for a video filmed on its side), handing each to <paramref name="picture"/> with when it shows in
    /// microseconds after the first, its pixels and the bytes from one row to the next. Called on libVLC's thread,
    /// which waits for it; it returns false to stop reading. Returns how many pictures were handed over.
    /// </summary>
    public static int Read(LibVLC libVlc, string path, int width, int height, Func<long, nint, int, bool> picture,
        CancellationToken cancellation)
    {
        width = Math.Max(2, width / 2 * 2);
        height = Math.Max(2, height / 2 * 2);
        var (_, _, orientation) = Describe(libVlc, path);
        // libVLC scales, then turns: so a picture on its side is scaled to the size it is before it's turned.
        var (scaleWidth, scaleHeight) = Turned(orientation) ? (height, width) : (width, height);
        var turn = orientation switch
        {
            VideoOrientation.TopRight => "hflip",
            VideoOrientation.BottomLeft => "vflip",
            VideoOrientation.BottomRight => "180",
            VideoOrientation.LeftTop => "transpose",
            VideoOrientation.LeftBottom => "270",
            VideoOrientation.RightTop => "90",
            VideoOrientation.RightBottom => "antitranspose",
            _ => null,
        };

        var reading = new Reading(width, height, picture);
        var handle = GCHandle.Alloc(reading);
        try
        {
            unsafe
            {
                delegate* unmanaged[Cdecl]<nint, nint*, nuint, void> prerender = &Prerender;
                delegate* unmanaged[Cdecl]<nint, nint, int, int, int, nuint, long, void> postrender = &Postrender;
                var filter = turn is null ? string.Empty : $",vfilter=transform{{type={turn}}}";
                using var media = new Media(libVlc, Path.GetFullPath(path), FromType.FromPath);
                media.AddOption(":no-audio");
                media.AddOption(":no-spu");
                // Decoded, scaled (and turned) to BGRA, and handed over without waiting for a clock.
                media.AddOption($":sout=#transcode{{vcodec=RV32,width={scaleWidth},height={scaleHeight},acodec=none{filter}}}"
                    + $":smem{{video-prerender-callback={(long)(nint)prerender},video-postrender-callback={(long)(nint)postrender},"
                    + $"video-data={(long)GCHandle.ToIntPtr(handle)},no-time-sync}}");
                Run(media, reading, cancellation);
            }
        }
        finally
        {
            handle.Free();
            reading.Free();
        }

        cancellation.ThrowIfCancellationRequested();
        if (reading.Failure is { } failure)
        {
            throw failure is InvalidDataException or IOException ? failure : new IOException(failure.Message, failure);
        }

        return reading.Count;
    }

    private static void Run(Media media, Reading reading, CancellationToken cancellation)
    {
        using var player = new MediaPlayer(media);
        using var ended = new ManualResetEventSlim();
        player.EndReached += (_, _) => ended.Set();
        player.EncounteredError += (_, _) =>
        {
            reading.Failure ??= new InvalidDataException("libVLC couldn't read this video's pictures.");
            ended.Set();
        };
        if (!player.Play())
        {
            throw new InvalidDataException("This video couldn't be read.");
        }

        try
        {
            while (true)
            {
                if (cancellation.IsCancellationRequested || reading.Stopped)
                {
                    break;
                }

                if (ended.Wait(50))
                {
                    // libVLC has read the whole file; the last picture may still be on its way.
                    if (reading.SinceLast >= EndQuiet)
                    {
                        break;
                    }

                    continue;
                }

                if (reading.SinceLast > StallTimeout)
                {
                    reading.Failure ??= new InvalidDataException("libVLC stopped decoding this video's pictures.");
                    break;
                }
            }
        }
        finally
        {
            reading.Stopped = true;
            // Stopped off libVLC's own threads, which stopping waits for.
            Task.Run(player.Stop).GetAwaiter().GetResult();
        }
    }

    private static (int Width, int Height, VideoOrientation Orientation) Describe(LibVLC libVlc, string path)
    {
        using var media = new Media(libVlc, Path.GetFullPath(path), FromType.FromPath);
        Task.Run(() => media.Parse(MediaParseOptions.ParseLocal, 10_000)).GetAwaiter().GetResult();
        var video = media.Tracks.Where(track => track.TrackType == TrackType.Video).Select(track => track.Data.Video).FirstOrDefault();
        var (width, height) = VideoFrames.SizeFor((int)video.Width, (int)video.Height, video.SarNum, video.SarDen);
        return (width, height, video.Orientation);
    }

    // On its side: shown with its width and height swapped.
    private static bool Turned(VideoOrientation orientation) => (int)orientation >= 4;

    // libVLC asks where to put the next picture.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static unsafe void Prerender(nint data, nint* buffer, nuint size)
    {
        var reading = (Reading)GCHandle.FromIntPtr(data).Target!;
        *buffer = reading.Room((long)size);
    }

    // libVLC has put a picture there.
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static void Postrender(nint data, nint pixels, int width, int height, int bitsPerPixel, nuint size, long pts)
    {
        var reading = (Reading)GCHandle.FromIntPtr(data).Target!;
        reading.Arrived(pixels, (long)size, pts);
    }

    private sealed class Reading
    {
        private readonly int _width;
        private readonly int _height;
        private readonly Func<long, nint, int, bool> _picture;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastAt;
        private long _first = long.MinValue;
        private nint _buffer;
        private long _capacity;

        public Reading(int width, int height, Func<long, nint, int, bool> picture)
        {
            _width = width;
            _height = height;
            _picture = picture;
        }

        public volatile bool Stopped;

        public Exception? Failure;

        public int Count;

        public TimeSpan SinceLast => TimeSpan.FromTicks(_clock.Elapsed.Ticks - Interlocked.Read(ref _lastAt));

        public nint Room(long size)
        {
            if (size > _capacity)
            {
                if (_buffer != 0)
                {
                    Marshal.FreeHGlobal(_buffer);
                }

                // Padded, as libVLC may write a little beyond the last pixel.
                _buffer = Marshal.AllocHGlobal((nint)(size + 64));
                _capacity = size;
            }

            return _buffer;
        }

        public void Arrived(nint pixels, long size, long pts)
        {
            Interlocked.Exchange(ref _lastAt, _clock.Elapsed.Ticks);
            if (Stopped)
            {
                return;
            }

            try
            {
                var stride = _width * 4;
                if (size < (long)stride * _height)
                {
                    throw new InvalidDataException("libVLC gave this video's pictures at a size Lucent didn't ask for.");
                }

                if (_first == long.MinValue)
                {
                    _first = pts;
                }

                Count++;
                if (!_picture(pts - _first, pixels, stride))
                {
                    Stopped = true;
                }
            }
            catch (Exception ex)
            {
                Failure ??= ex;
                Stopped = true;
            }

            Interlocked.Exchange(ref _lastAt, _clock.Elapsed.Ticks);
        }

        public void Free()
        {
            if (_buffer != 0)
            {
                Marshal.FreeHGlobal(_buffer);
                _buffer = 0;
            }
        }
    }
}
