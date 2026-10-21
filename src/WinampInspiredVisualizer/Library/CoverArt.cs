using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TagLib;

namespace WinampInspiredVisualizer.Library;

/// <summary>A decoded cover picture, plus the original file bytes for copying or saving it.</summary>
public sealed class CoverImage
{
    public CoverImage(BitmapSource image, byte[] data, string mimeType, int pixelWidth, int pixelHeight, string? filePath)
    {
        Image = image;
        Data = data;
        MimeType = mimeType;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FilePath = filePath;
    }

    /// <summary>The picture, frozen so it can be shown from any thread. May be smaller than the original.</summary>
    public BitmapSource Image { get; }

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
/// Everything here is safe to call off the UI thread, and every bitmap it returns is frozen.
/// </summary>
public static class CoverArt
{
    /// <summary>Covers bigger than this (on their longest side) are shrunk before being stored in files.</summary>
    public const int MaxEmbeddedPixels = 1600;

    /// <summary>Image files that Windows can open without extra codecs.</summary>
    public static readonly IReadOnlySet<string> ImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff",
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
            var (width, height) = PixelSize(data);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            // Broken colour profiles are common in downloaded covers and would otherwise stop them loading.
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.StreamSource = new MemoryStream(data, writable: false);
            if (maxPixels > 0 && Math.Max(width, height) > maxPixels)
            {
                // Setting only the longer side keeps the shape.
                if (width >= height)
                {
                    image.DecodePixelWidth = maxPixels;
                }
                else
                {
                    image.DecodePixelHeight = maxPixels;
                }
            }

            image.EndInit();
            image.Freeze();
            return new CoverImage(image, data, mimeType ?? ImageFormats.MimeTypeOf(data), width, height, filePath);
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

        var decoded = Decode(data, MaxEmbeddedPixels) ?? throw new InvalidDataException("That isn't a picture Lucent can read.");
        var jpeg = EncodeJpeg(decoded.Image);
        return new EmbeddableImage(jpeg, "image/jpeg", decoded.Image.PixelWidth, decoded.Image.PixelHeight, width, height);
    }

    /// <summary>Saves a bitmap as JPEG bytes (transparent areas become black).</summary>
    public static byte[] EncodeJpeg(BitmapSource source, int quality = 90)
    {
        var opaque = source.Format == PixelFormats.Bgr24 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    /// <summary>Saves 32-bit BGRA pixels (the fourth byte is ignored) as JPEG bytes.</summary>
    public static byte[] EncodeJpeg(nint bgra, int width, int height, int stride, int quality) =>
        EncodeJpeg(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, bgra, stride * height, stride), quality);

    /// <summary>Saves a bitmap as PNG bytes, keeping every pixel and any transparency.</summary>
    public static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
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

    /// <summary>An image's full size, read from its header without decoding it.</summary>
    private static (int Width, int Height) PixelSize(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile | BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
        var frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }
}
