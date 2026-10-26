using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using TagLib;
using Lucent.Library;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace Lucent.Mac.Library;

/// <summary>A decoded cover picture, plus the original file bytes for copying or saving it.</summary>
public sealed class CoverImage
{
    public CoverImage(Bitmap image, byte[] data, string mimeType, int pixelWidth, int pixelHeight, string? filePath)
    {
        Image = image;
        Data = data;
        MimeType = mimeType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FilePath = filePath;
    }

    /// <summary>The picture, ready to show from any thread. May be smaller than the original.</summary>
    public Bitmap Image { get; }

    /// <summary>The picture exactly as stored, for saving it out unchanged.</summary>
    public byte[] Data { get; }

    public string MimeType { get; }

    /// <summary>The original size, before any shrinking for display.</summary>
    public int PixelWidth { get; }

    public int PixelHeight { get; }

    /// <summary>The image file in the track's folder it came from, or null when it's stored inside the music file.</summary>
    public string? FilePath { get; }

    public string SizeText => $"{PixelWidth} × {PixelHeight}";
}

/// <summary>A picture ready to be stored in music files, shrunk first if it was very large.</summary>
public sealed record EmbeddableImage(byte[] Data, string MimeType, int Width, int Height, int OriginalWidth, int OriginalHeight)
{
    public bool WasShrunk => Width != OriginalWidth || Height != OriginalHeight;
}

/// <summary>
/// Finding, decoding and preparing cover art. Used by the artwork window and the track info window.
/// Everything here is safe to call off the UI thread. Pictures are decoded with Skia (the library
/// Avalonia draws with); the few formats it can't read, such as TIFF and HEIC, go through macOS's own
/// image tools instead, as Windows' imaging codecs read them on Windows.
/// </summary>
public static class CoverArt
{
    /// <summary>Covers bigger than this (on their longest side) are shrunk before being stored in files.</summary>
    public const int MaxEmbeddedPixels = 1600;

    /// <summary>Image files that the Mac can open without anything extra.</summary>
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic",
    };

    // Names of cover images that sit next to the music, in the order they're looked for.
    private static readonly string[] FolderImageNames = { "folder", "cover", "front" };

    private const long LargestStoredBytes = 4 * 1024 * 1024;

    public static bool IsImageFile(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// The cover for a track: the front cover stored in the file first (or any picture it has), then
    /// folder.jpg, cover.jpg or front.* in the same folder. Null when there's none.
    /// </summary>
    public static Task<CoverImage?> LoadForTrackAsync(string path, int maxPixels, CancellationToken token = default) =>
        Task.Run(() => LoadForTrack(path, maxPixels, token), token);

    public static CoverImage? LoadForTrack(string path, int maxPixels, CancellationToken token = default)
    {
        foreach (var picture in EmbeddedPictures(path))
        {
            token.ThrowIfCancellationRequested();
            if (Decode(picture.Data, maxPixels, picture.MimeType, null) is { } embedded)
            {
                return embedded;
            }
        }

        foreach (var file in FolderImages(path))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (Decode(System.IO.File.ReadAllBytes(file), maxPixels, null, file) is { } image)
                {
                    return image;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Try the next one.
            }
        }

        return null;
    }

    /// <summary>
    /// Decodes picture bytes, shrinking to fit <paramref name="maxPixels"/> on the longest side when it's
    /// bigger (0 keeps the full size). Returns null for anything that isn't a readable image.
    /// </summary>
    public static CoverImage? Decode(byte[] data, int maxPixels, string? mimeType = null, string? filePath = null)
    {
        try
        {
            using var pixels = DecodePixels(data, maxPixels, out var width, out var height);
            if (pixels is null)
            {
                return null;
            }

            return new CoverImage(ToAvalonia(pixels), data, mimeType ?? ImageFormats.MimeTypeOf(data), width, height, filePath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets an image ready to store in music files. Small JPEG and PNG images are kept exactly as they
    /// are; very large ones, and other formats that players don't all understand, become a JPEG.
    /// Throws if the bytes aren't an image.
    /// </summary>
    public static EmbeddableImage PrepareForEmbedding(byte[] data)
    {
        var (width, height) = PixelSize(data);
        var mime = ImageFormats.MimeTypeOf(data);
        var longest = Math.Max(width, height);
        if (longest <= MaxEmbeddedPixels && data.LongLength <= LargestStoredBytes && mime is "image/jpeg" or "image/png")
        {
            return new EmbeddableImage(data, mime, width, height, width, height);
        }

        using var pixels = DecodePixels(data, MaxEmbeddedPixels, out _, out _)
            ?? throw new InvalidDataException("That isn't a picture Lucent can read.");
        var jpeg = EncodeJpeg(pixels);
        return new EmbeddableImage(jpeg, "image/jpeg", pixels.Width, pixels.Height, width, height);
    }

    /// <summary>Saves a bitmap as JPEG bytes (transparent areas become black).</summary>
    public static byte[] EncodeJpeg(Bitmap source, int quality = 90)
    {
        using var pixels = DecodePixels(EncodePng(source), 0, out _, out _)
            ?? throw new InvalidDataException("That isn't a picture Lucent can read.");
        return EncodeJpeg(pixels, quality);
    }

    /// <summary>Saves 32-bit BGRA pixels (the fourth byte is ignored) as JPEG bytes.</summary>
    public static byte[] EncodeJpeg(nint bgra, int width, int height, int stride, int quality)
    {
        using var pixels = new SKBitmap();
        pixels.InstallPixels(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque), bgra, stride);
        using var image = SKImage.FromBitmap(pixels);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }

    /// <summary>Saves a bitmap as PNG bytes, keeping every pixel and any transparency.</summary>
    public static byte[] EncodePng(Bitmap source)
    {
        using var stream = new MemoryStream();
        source.Save(stream);
        return stream.ToArray();
    }

    private static byte[] EncodeJpeg(SKBitmap pixels, int quality = 90)
    {
        // Drawn onto black first, so see-through parts come out black as they do on Windows.
        using var opaque = new SKBitmap(new SKImageInfo(pixels.Width, pixels.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        using (var canvas = new SKCanvas(opaque))
        {
            canvas.Clear(SKColors.Black);
            canvas.DrawBitmap(pixels, 0, 0);
        }

        using var image = SKImage.FromBitmap(opaque);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded.ToArray();
    }

    /// <summary>The pictures stored inside a music file, front cover first.</summary>
    private static IEnumerable<TagPicture> EmbeddedPictures(string path)
    {
        List<TagPicture> pictures;
        try
        {
            using var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), ReadStyle.None);
            pictures = (file.Tag.Pictures ?? Array.Empty<IPicture>())
                .Where(picture => picture.Data is { Count: > 0 })
                .Select(TagPicture.From)
                .ToList();
        }
        catch (Exception)
        {
            // Unreadable tags just mean looking in the folder instead.
            return Array.Empty<TagPicture>();
        }

        return pictures.OrderBy(picture => picture.Type == PictureType.FrontCover ? 0 : 1);
    }

    private static IEnumerable<string> FolderImages(string trackPath)
    {
        var folder = Path.GetDirectoryName(trackPath);
        if (string.IsNullOrEmpty(folder))
        {
            return Array.Empty<string>();
        }

        List<string> images;
        try
        {
            images = Directory.EnumerateFiles(folder).Where(IsImageFile).ToList();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        return FolderImageNames
            .SelectMany(name => images.Where(image => string.Equals(Path.GetFileNameWithoutExtension(image), name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    /// <summary>An image's full size, read from its header without decoding it. Throws if it isn't an image.</summary>
    private static (int Width, int Height) PixelSize(byte[] data)
    {
        using (var skData = SKData.CreateCopy(data))
        using (var codec = SKCodec.Create(skData))
        {
            if (codec is not null)
            {
                return (codec.Info.Width, codec.Info.Height);
            }
        }

        using var converted = DecodeWithMacImageTools(data) ?? throw new InvalidDataException("That isn't a picture Lucent can read.");
        return (converted.Width, converted.Height);
    }

    /// <summary>
    /// Decodes to premultiplied BGRA (no colour profile conversion, like WPF's IgnoreColorProfile), shrunk to
    /// fit <paramref name="maxPixels"/> on its longest side. Gives the original size too. Null when it can't be read.
    /// </summary>
    private static SKBitmap? DecodePixels(byte[] data, int maxPixels, out int width, out int height)
    {
        var bitmap = DecodeWithSkia(data) ?? DecodeWithMacImageTools(data);
        if (bitmap is null)
        {
            width = height = 0;
            return null;
        }

        width = bitmap.Width;
        height = bitmap.Height;
        if (maxPixels <= 0 || Math.Max(width, height) <= maxPixels)
        {
            return bitmap;
        }

        // Fitting the longer side keeps the shape.
        var (newWidth, newHeight) = width >= height
            ? (maxPixels, Math.Max(1, (int)Math.Round((double)height * maxPixels / width)))
            : (Math.Max(1, (int)Math.Round((double)width * maxPixels / height)), maxPixels);
        var resized = bitmap.Resize(new SKImageInfo(newWidth, newHeight, SKColorType.Bgra8888, SKAlphaType.Premul), SKFilterQuality.High);
        bitmap.Dispose();
        return resized;
    }

    private static SKBitmap? DecodeWithSkia(byte[] data)
    {
        using var skData = SKData.CreateCopy(data);
        using var codec = SKCodec.Create(skData);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0)
        {
            return null;
        }

        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);
        var result = codec.GetPixels(info, bitmap.GetPixels());
        if (result is SKCodecResult.Success or SKCodecResult.IncompleteInput)
        {
            return bitmap;
        }

        bitmap.Dispose();
        return null;
    }

    /// <summary>
    /// Pictures Skia can't read (TIFF, HEIC) are turned into PNG by macOS's sips tool, which uses the
    /// system's image codecs. Null elsewhere, or when the Mac can't read it either.
    /// </summary>
    private static SKBitmap? DecodeWithMacImageTools(byte[] data)
    {
        if (!OperatingSystem.IsMacOS() || !System.IO.File.Exists("/usr/bin/sips"))
        {
            return null;
        }

        var folder = Path.Combine(Path.GetTempPath(), "lucent-picture-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var input = Path.Combine(folder, "picture");
            var output = Path.Combine(folder, "picture.png");
            System.IO.File.WriteAllBytes(input, data);
            var start = new ProcessStartInfo("/usr/bin/sips")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in new[] { "-s", "format", "png", input, "--out", output })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(15000))
            {
                process.Kill();
                return null;
            }

            return process.ExitCode == 0 && System.IO.File.Exists(output) ? DecodeWithSkia(System.IO.File.ReadAllBytes(output)) : null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // A leftover temporary file does no harm.
            }
        }
    }

    /// <summary>Copies Skia's pixels into an Avalonia bitmap, which can be shown from any thread.</summary>
    private static Bitmap ToAvalonia(SKBitmap pixels) =>
        new(PixelFormat.Bgra8888, AlphaFormat.Premul, pixels.GetPixels(), new PixelSize(pixels.Width, pixels.Height), new Vector(96, 96), pixels.RowBytes);
}
