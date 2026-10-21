using System.IO;
using LibVLCSharp.Shared;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer.Editor;

/// <summary>How frames are saved: PNG (exact) or JPEG (much smaller).</summary>
public enum FrameImageFormat
{
    Png,
    Jpeg,
}

/// <summary>A picture of a video open in the editor: when it shows in the original file, and in the edit.</summary>
public readonly record struct VideoExportPicture(double Original, double Edited, int Stretch);

/// <summary>What to save of a video's pictures, as chosen in the editor's Save frames or Save a GIF box.</summary>
public sealed record FrameExportChoice
{
    /// <summary>An animated GIF; otherwise a folder of images.</summary>
    public bool Gif { get; init; }

    /// <summary>Images: so many a second, or null for every frame.</summary>
    public double? PerSecond { get; init; }

    public FrameImageFormat Format { get; init; } = FrameImageFormat.Png;

    /// <summary>Images: the folder their own new folder goes in. Empty for the video's own folder.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>GIF: its width (one of <see cref="VideoExport.GifWidths"/>; 0 for the video's own).</summary>
    public int GifWidth { get; init; } = 480;

    /// <summary>GIF: frames a second (one of <see cref="VideoExport.GifRates"/>).</summary>
    public int GifRate { get; init; } = 15;

    /// <summary>GIF: plays over and over, or once.</summary>
    public bool Loop { get; init; } = true;

    /// <summary>GIF: dithered, for smoother gradients in its 256 colours, at the cost of a bigger file.</summary>
    public bool Dither { get; init; } = true;

    /// <summary>GIF: the file it's saved as.</summary>
    public string GifPath { get; init; } = string.Empty;
}

/// <summary>
/// Saves part of a video open in the editor as pictures: every frame (or so many a second) as PNG or JPEG files, or
/// an animated GIF. The pictures are the original video's, decoded by libVLC (<see cref="VideoPictureReader"/>),
/// and follow the edit: a range across a cut has the pictures either side of it, as the edit plays.
/// </summary>
public static class VideoExport
{
    /// <summary>GIF sizes on offer, by width; 0 is the video's own.</summary>
    public static readonly IReadOnlyList<int> GifWidths = new[] { 320, 480, 640, 0 };

    /// <summary>GIF frame rates on offer, in frames a second.</summary>
    public static readonly IReadOnlyList<int> GifRates = new[] { 10, 15, 24 };

    /// <summary>The most memory a GIF's pictures may take while it's made.</summary>
    public const long GifMemoryLimit = 1L << 30;

    /// <summary>The choices made last time, offered again (this session).</summary>
    public static FrameExportChoice LastChoice { get; set; } = new();

    /// <summary>
    /// Encodes a 32-bit BGRA picture (pixels, width, height, stride, quality 1-100) as a JPEG file's bytes, with the
    /// system's own encoder: each app sets it. Null where there isn't one, which leaves PNG only.
    /// </summary>
    public static Func<nint, int, int, int, int, byte[]>? EncodeJpeg { get; set; }

    /// <summary>
    /// The pictures between <paramref name="from"/> and <paramref name="to"/> (seconds of the edit): every one that
    /// shows in the range, or with <paramref name="perSecond"/>, the one showing at each of so many moments a second.
    /// </summary>
    public static IReadOnlyList<VideoExportPicture> Pictures(VideoEdit video, double from, double to, double? perSecond = null)
    {
        var times = video.Movie.PictureTimes;
        // Every picture of the edit, in the order it plays, with the moment it starts in the edit.
        var all = new List<VideoExportPicture>();
        var offset = 0.0;
        for (var stretch = 0; stretch < video.Kept.Count; stretch++)
        {
            var range = video.Kept[stretch];
            var first = LowerBound(times, range.Start - 1e-6);
            for (var i = first; i < times.Count && times[i] < range.End - 1e-6; i++)
            {
                all.Add(new VideoExportPicture(times[i], offset + Math.Max(0, times[i] - range.Start), stretch));
            }

            offset += range.Length;
        }

        var end = offset;
        double Ends(int i) => i + 1 < all.Count ? all[i + 1].Edited : end;
        if (perSecond is not { } rate || rate <= 0)
        {
            return all.Where((picture, i) => picture.Edited < to - 1e-6 && Ends(i) > from + 1e-6).ToList();
        }

        var chosen = new List<VideoExportPicture>();
        var at = 0;
        for (var k = 0; ; k++)
        {
            var moment = from + k / rate;
            if (moment >= to - 1e-6 || all.Count == 0)
            {
                break;
            }

            while (at + 1 < all.Count && all[at + 1].Edited <= moment + 1e-6)
            {
                at++;
            }

            chosen.Add(all[at]);
        }

        return chosen;
    }

    /// <summary>
    /// Saves <paramref name="pictures"/> into a new folder in <paramref name="folder"/>, named by when each shows in
    /// the edit so they sort in order. Returns the new folder.
    /// </summary>
    public static string SaveFrames(VideoEdit video, IReadOnlyList<VideoExportPicture> pictures, string folder, FrameImageFormat format,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        if (format == FrameImageFormat.Jpeg && EncodeJpeg is null)
        {
            format = FrameImageFormat.Png;
        }

        var name = Path.GetFileNameWithoutExtension(video.Movie.Path);
        var target = Unique(Path.Combine(folder, $"{name} frames"));
        var extension = format == FrameImageFormat.Png ? ".png" : ".jpg";
        using var libVlc = VideoEngine.CreateForFrames();
        var (width, height) = VideoPictureReader.SizeOf(libVlc, video.Movie.Path);
        if (width == 0)
        {
            throw new InvalidDataException("This video's pictures couldn't be read.");
        }

        Directory.CreateDirectory(target);
        try
        {
            var saved = 0;
            Read(libVlc, video, pictures, width, height, (picture, pixels, stride) =>
            {
                var file = Path.Combine(target, $"{name} {TimeCode(picture.Edited)}{extension}");
                if (format == FrameImageFormat.Png)
                {
                    using var stream = File.Create(file);
                    PngWriter.Write(stream, pixels, width, height, stride);
                }
                else
                {
                    File.WriteAllBytes(file, EncodeJpeg!(pixels, width, height, stride, 92));
                }

                progress?.Report(++saved / (double)pictures.Count);
            }, cancellation);
        }
        catch (Exception)
        {
            // Cancelled or failed: no half-saved folder is left behind.
            try
            {
                Directory.Delete(target, recursive: true);
            }
            catch (IOException)
            {
            }

            throw;
        }

        return target;
    }

    /// <summary>The size a GIF of this video comes out at, for a width from <see cref="GifWidths"/> (0 for its own).</summary>
    public static (int Width, int Height) GifSize(int videoWidth, int videoHeight, int width)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return (0, 0);
        }

        if (width <= 0 || width >= videoWidth)
        {
            return (videoWidth / 2 * 2, videoHeight / 2 * 2);
        }

        return (width, Math.Max(2, (int)Math.Round(videoHeight * (double)width / videoWidth / 2) * 2));
    }

    /// <summary>
    /// About how big a GIF comes out, in bytes, before it's made. Video GIFs vary a lot with what's in the picture
    /// (a still background compresses far better), so this is a rough guide.
    /// </summary>
    public static long EstimateGifBytes(int width, int height, int frames, bool dither) =>
        (long)(width * (long)height * frames * (dither ? 0.55 : 0.4));

    /// <summary>A size in bytes as people read it: "840 KB", "3.4 MB".</summary>
    public static string DescribeBytes(long bytes) =>
        bytes >= 1 << 20 ? $"{bytes / 1048576.0:0.0} MB" : $"{Math.Max(1, (bytes + 512) / 1024)} KB";

    /// <summary>The video's picture size (square pixels), as libVLC shows it; (0, 0) when it can't be read.</summary>
    public static (int Width, int Height) PictureSize(VideoEdit video)
    {
        using var libVlc = VideoEngine.CreateForFrames();
        return VideoPictureReader.SizeOf(libVlc, video.Movie.Path);
    }

    /// <summary>Makes an animated GIF of <paramref name="pictures"/> (chosen at <paramref name="perSecond"/>).</summary>
    public static void SaveGif(VideoEdit video, IReadOnlyList<VideoExportPicture> pictures, double perSecond, int width, bool loop, bool dither,
        string target, IProgress<double>? progress, CancellationToken cancellation)
    {
        using var libVlc = VideoEngine.CreateForFrames();
        var (videoWidth, videoHeight) = VideoPictureReader.SizeOf(libVlc, video.Movie.Path);
        var (w, h) = GifSize(videoWidth, videoHeight, width);
        if (w == 0)
        {
            throw new InvalidDataException("This video's pictures couldn't be read.");
        }

        if ((long)w * h * 3 * pictures.Count > GifMemoryLimit)
        {
            throw new InvalidDataException("That's too long for a GIF this size. Select a shorter stretch, or pick a smaller width.");
        }

        var gif = new GifEncoder(w, h);
        var read = 0;
        // Each frame shows until the next moment, in hundredths of a second, rounded so the whole keeps time.
        Read(libVlc, video, pictures, w, h, (picture, pixels, stride) =>
        {
            var k = gif.Count;
            var delay = (int)(Math.Round((k + 1) * 100 / perSecond) - Math.Round(k * 100 / perSecond));
            gif.Add(pixels, stride, delay);
            progress?.Report(0.8 * ++read / pictures.Count);
        }, cancellation);

        var temporary = target + ".part";
        try
        {
            using (var stream = File.Create(temporary))
            {
                gif.Write(stream, loop, dither, new Progress<double>(done => progress?.Report(0.8 + 0.2 * done)), cancellation);
            }

            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    // Reads the pictures, a stretch of the original at a time, handing over just the ones asked for, in order (the
    // self-test checks them as they come). Each stretch's pictures are copied into a short video of their own, from
    // the keyframe before them, and read from its start, where libVLC's pictures can be counted exactly.
    internal static void Read(LibVLC libVlc, VideoEdit video, IReadOnlyList<VideoExportPicture> pictures, int width, int height,
        Action<VideoExportPicture, nint, int> save, CancellationToken cancellation)
    {
        var times = video.Movie.PictureTimes;
        var at = 0;
        while (at < pictures.Count)
        {
            // A run of pictures from one stretch of the original, in order.
            var stretch = pictures[at].Stretch;
            var runEnd = at;
            while (runEnd + 1 < pictures.Count && pictures[runEnd + 1].Stretch == stretch && pictures[runEnd + 1].Original >= pictures[runEnd].Original)
            {
                runEnd++;
            }

            // Up to the end of the run's last picture.
            var last = LowerBound(times, pictures[runEnd].Original - 1e-6);
            var through = last + 1 < times.Count ? times[last + 1] : video.Movie.Duration;
            var part = Path.Combine(Path.GetTempPath(), $"lucent-frames-{Guid.NewGuid():N}.mp4");
            try
            {
                var start = Mp4Cutter.WritePictures(video.Movie, pictures[at].Original, through, part, cancellation);
                var partTimes = Mp4Cutter.Read(part).PictureTimes;

                // Which of the short video's pictures are wanted: some may be wanted twice (a slow video at a high rate).
                var wanted = new Dictionary<int, List<VideoExportPicture>>();
                var needed = 0;
                for (var p = at; p <= runEnd; p++)
                {
                    var index = Nearest(partTimes, pictures[p].Original - start);
                    if (!wanted.TryGetValue(index, out var list))
                    {
                        wanted[index] = list = new List<VideoExportPicture>();
                    }

                    list.Add(pictures[p]);
                    needed = Math.Max(needed, index + 1);
                }

                var got = VideoPictureReader.Read(libVlc, part, needed, width, height, (index, pixels, stride) =>
                {
                    if (wanted.TryGetValue(index, out var each))
                    {
                        foreach (var picture in each)
                        {
                            save(picture, pixels, stride);
                        }
                    }
                }, cancellation);
                if (got < needed)
                {
                    throw new InvalidDataException($"The video ended {needed - got} picture(s) early, so the last part couldn't be saved.");
                }
            }
            finally
            {
                try
                {
                    File.Delete(part);
                }
                catch (IOException)
                {
                }
            }

            at = runEnd + 1;
        }
    }

    // The place in a sorted list of the value closest to this one.
    private static int Nearest(IReadOnlyList<double> sorted, double value)
    {
        var i = LowerBound(sorted, value);
        if (i >= sorted.Count)
        {
            return sorted.Count - 1;
        }

        return i > 0 && value - sorted[i - 1] < sorted[i] - value ? i - 1 : i;
    }

    private static int LowerBound(IReadOnlyList<double> sorted, double value)
    {
        int low = 0, high = sorted.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (sorted[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>A moment as hours, minutes, seconds and milliseconds that sort in order in a file name: 00-01-02.345.</summary>
    public static string TimeCode(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds, 3)));
        return $"{(int)time.TotalHours:00}-{time.Minutes:00}-{time.Seconds:00}.{time.Milliseconds:000}";
    }

    private static string Unique(string path)
    {
        var candidate = path;
        for (var n = 2; Directory.Exists(candidate) || File.Exists(candidate); n++)
        {
            candidate = $"{path} ({n})";
        }

        return candidate;
    }
}
