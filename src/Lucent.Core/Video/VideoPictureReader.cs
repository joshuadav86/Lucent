using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// Reads the first pictures of a video, in order, as fast as libVLC decodes them, for saving as images or a GIF.
/// Pictures come as 32-bit BGRA (blue, green, red, then a byte that's always opaque), top row first.
/// </summary>
/// <remarks>
/// <para>
/// libVLC doesn't say which moment a picture it shows belongs to, so pictures are counted from the start of the
/// video: its own libVLC (from <see cref="VideoEngine.CreateForFrames"/>) shows every picture in turn, dropping none
/// for coming late. It's always read from the start because where libVLC starts after a jump into a video varies by
/// a picture or two; to read pictures from the middle of a video, those are copied into a short video of their own
/// first (<see cref="Mp4Cutter.WritePictures"/>).
/// </para>
/// <para>
/// When decoding stalls for a moment, libVLC shows the last picture again (it does so every 80 ms with nothing new).
/// Such a repeat is the same picture, shown that long after it, so a picture identical to the one before that comes
/// as late as that is taken for a repeat and skipped.
/// </para>
/// </remarks>
public static class VideoPictureReader
{
    // libVLC shows the last picture again when nothing new has come for 80 ms; a repeat comes at least about that late.
    private const double RepeatAfterMs = 60;

    // How many pictures libVLC can have made at once (its decoder's and converter's own, plus the one shown).
    private const int Buffers = 6;

    /// <summary>The size libVLC shows a video's pictures at (square pixels, no bigger than 3840 × 2160), or (0, 0).</summary>
    public static (int Width, int Height) SizeOf(LibVLC libVlc, string path)
    {
        using var media = new Media(libVlc, Path.GetFullPath(path), FromType.FromPath);
        Task.Run(() => media.Parse(MediaParseOptions.ParseLocal, 10_000)).GetAwaiter().GetResult();
        var video = media.Tracks.Where(track => track.TrackType == TrackType.Video).Select(track => track.Data.Video).FirstOrDefault();
        var (width, height) = VideoFrames.SizeFor((int)video.Width, (int)video.Height, video.SarNum, video.SarDen);
        return (int)video.Orientation >= 4 ? (height, width) : (width, height);
    }

    /// <summary>
    /// Reads the first <paramref name="count"/> pictures of the video, scaled to <paramref name="width"/> ×
    /// <paramref name="height"/>, handing each to <paramref name="picture"/> with its place (0 for the first), its
    /// pixels and the bytes from one row to the next. Called on libVLC's thread, which waits for it; it returns false
    /// to stop reading, and then -1 is returned. Otherwise returns how many came, which is fewer than asked only when
    /// the video ended first.
    /// </summary>
    public static int Read(LibVLC libVlc, string path, int count, int width, int height,
        Func<int, nint, int, bool> picture, CancellationToken cancellation) =>
        Read(libVlc, path, count, width, height, picture, cancellation, step: false);

    /// <summary>
    /// The first picture of a video at this size, as <see cref="Read"/> gives it, or null if libVLC shows none. Read
    /// from a paused start, asking for one picture: playing, libVLC now and then skips the first as it starts.
    /// </summary>
    public static byte[]? ReadFirst(LibVLC libVlc, string path, int width, int height, CancellationToken cancellation)
    {
        byte[]? copy = null;
        Read(libVlc, path, 1, width, height, (_, pixels, stride) =>
        {
            copy = new byte[stride * Math.Max(2, height / 2 * 2)];
            Marshal.Copy(pixels, copy, 0, copy.Length);
            return true;
        }, cancellation, step: true);
        return copy;
    }

    private static int Read(LibVLC libVlc, string path, int count, int width, int height,
        Func<int, nint, int, bool> picture, CancellationToken cancellation, bool step)
    {
        if (count <= 0)
        {
            return 0;
        }

        width = Math.Max(2, width / 2 * 2);
        height = Math.Max(2, height / 2 * 2);
        var stride = width * 4;
        var bytes = stride * height;
        // Each picture libVLC makes gets the next of a few buffers, so it never writes the next into the one being
        // read. Padded, as libVLC's converters may write a little beyond the last pixel.
        var buffers = Enumerable.Range(0, Buffers).Select(_ => Marshal.AllocHGlobal(bytes + 64)).ToArray();
        var next = 0;
        var previous = new byte[bytes];
        var hasPrevious = false;
        var shown = 0;
        var clock = Stopwatch.StartNew();
        var lastShown = 0.0;
        Exception? failure = null;
        using var done = new ManualResetEventSlim();
        var stopped = false;
        try
        {
            using var media = new Media(libVlc, Path.GetFullPath(path), FromType.FromPath);
            media.AddOption(":no-audio");
            // As fast as it decodes: no picture is dropped for coming late, so the clock only sets the order.
            media.AddOption(":rate=16");
            if (step)
            {
                media.AddOption(":start-paused");
            }

            using var player = new MediaPlayer(media);
            player.SetVideoFormatCallbacks(
                (ref nint opaque, nint chroma, ref uint w, ref uint h, ref uint pitches, ref uint lines) =>
                {
                    Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
                    w = (uint)width;
                    h = (uint)height;
                    pitches = (uint)stride;
                    lines = (uint)height;
                    return 1;
                },
                (ref nint opaque) => { });
            player.SetVideoCallbacks(
                (opaque, planes) =>
                {
                    var which = next;
                    next = (next + 1) % Buffers;
                    Marshal.WriteIntPtr(planes, buffers[which]);
                    return (nint)which;
                },
                null,
                (opaque, which) =>
                {
                    if (done.IsSet)
                    {
                        return;
                    }

                    var buffer = buffers[(int)which];

                    var now = clock.Elapsed.TotalMilliseconds;
                    var late = now - lastShown >= RepeatAfterMs;
                    lastShown = now;
                    unsafe
                    {
                        var pixels = new ReadOnlySpan<byte>((void*)buffer, bytes);
                        if (hasPrevious && late && pixels.SequenceEqual(previous))
                        {
                            // libVLC showing the last picture again while it waits for the next.
                            return;
                        }

                        pixels.CopyTo(previous);
                    }

                    hasPrevious = true;
                    try
                    {
                        if (!picture(shown, buffer, stride))
                        {
                            stopped = true;
                            done.Set();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                        done.Set();
                        return;
                    }

                    if (++shown == count)
                    {
                        done.Set();
                    }

                    // The time spent saving the picture isn't a stall in decoding.
                    lastShown = clock.Elapsed.TotalMilliseconds;
                });
            player.EndReached += (_, _) => done.Set();
            player.EncounteredError += (_, _) => done.Set();
            if (!player.Play())
            {
                throw new InvalidDataException("This video couldn't be read.");
            }

            if (step)
            {
                // Paused, libVLC shows nothing until asked for the next picture, and doesn't say when a video ends.
                using var paused = new ManualResetEventSlim();
                player.Paused += (_, _) => paused.Set();
                if (player.State == VLCState.Paused)
                {
                    paused.Set();
                }

                WaitHandle.WaitAny(new[] { paused.WaitHandle, done.WaitHandle }, 10_000);
                Task.Run(player.NextFrame).GetAwaiter().GetResult();
                for (var waited = 0; waited < 10_000 && !done.Wait(50) && !cancellation.IsCancellationRequested; waited += 50)
                {
                }

                done.Set();
            }

            while (!done.Wait(50))
            {
                if (cancellation.IsCancellationRequested)
                {
                    done.Set();
                }
            }

            // Stopped off libVLC's own threads, which stopping waits for.
            Task.Run(player.Stop).GetAwaiter().GetResult();
        }
        finally
        {
            foreach (var buffer in buffers)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        cancellation.ThrowIfCancellationRequested();
        if (failure is not null)
        {
            throw new IOException(failure.Message, failure);
        }

        return stopped ? -1 : shown;
    }
}
