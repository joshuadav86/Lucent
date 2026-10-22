using Avalonia.Controls;
using Lucent.Mac.Audio;
using SkiaSharp;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace Lucent.Mac;

/// <summary>
/// The track info and artwork windows for <see cref="Screenshots"/>, showing two small tagged FLAC files
/// made on the spot (a few seconds of tone each, with a drawn front and back cover).
/// </summary>
internal static class TrackInfoPreviews
{
    private static string? _folder;

    public static Window TrackInfo() => TrackInfoWindow.CreatePreview(new AudioPlayer(), new AppSettings(), new[] { NightDrive });

    public static Window TrackInfoLyrics() => TrackInfoWindow.CreatePreview(new AudioPlayer(), new AppSettings(), new[] { NightDrive }, "Lyrics");

    public static Window TrackInfoFile() => TrackInfoWindow.CreatePreview(new AudioPlayer(), new AppSettings(), new[] { NightDrive }, "File");

    public static Window TrackInfoSeveral() => TrackInfoWindow.CreatePreview(new AudioPlayer(), new AppSettings(), new[] { NightDrive, LowTide });

    public static Window Artwork() => ArtworkWindow.CreatePreview(new AudioPlayer(), new AppSettings(), Track.FromFile(NightDrive));

    public static Window ArtworkEmpty() => ArtworkWindow.CreatePreview(new AudioPlayer(), new AppSettings(), null);

    private static string NightDrive => Path.Combine(Folder, "03 Night Drive.flac");

    private static string LowTide => Path.Combine(Folder, "04 Low Tide.flac");

    private static string Folder => _folder ??= MakeSamples();

    private static string MakeSamples()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lucent-screenshots", "samples");
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        var front = Cover(1200, new SKColor(0x2B, 0x1B, 0x5E), new SKColor(0xF2, 0x6B, 0x8A), "COASTAL LIGHTS");
        var back = Cover(800, new SKColor(0x10, 0x2A, 0x43), new SKColor(0x4F, 0xC3, 0xC9), "SIDE B");
        var otherFront = Cover(1000, new SKColor(0x0E, 0x3B, 0x2E), new SKColor(0xE8, 0xC5, 0x47), "LOW TIDE");

        MakeTrack(Path.Combine(folder, "03 Night Drive.flac"), 220, file =>
        {
            file.Tag.Title = "Night Drive";
            file.Tag.Track = 3;
            file.Tag.BeatsPerMinute = 104;
            file.Tag.Comment = "Recorded live to tape in one take, late at night.";
            file.Tag.Lyrics = "Headlights on the water\nThe city going slow\nWe follow the coast road\nWherever the lights go\n\nTurn the radio up now\nThe night is long and wide";
            file.Tag.Pictures = new TagLib.IPicture[]
            {
                new TagLib.Picture(new TagLib.ByteVector(front)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg" },
                new TagLib.Picture(new TagLib.ByteVector(back)) { Type = TagLib.PictureType.BackCover, MimeType = "image/jpeg" },
            };
        });
        MakeTrack(Path.Combine(folder, "04 Low Tide.flac"), 330, file =>
        {
            file.Tag.Title = "Low Tide";
            file.Tag.Track = 4;
            file.Tag.BeatsPerMinute = 92;
            file.Tag.Pictures = new TagLib.IPicture[]
            {
                new TagLib.Picture(new TagLib.ByteVector(otherFront)) { Type = TagLib.PictureType.FrontCover, MimeType = "image/jpeg" },
            };
        });
        return folder;
    }

    private static void MakeTrack(string path, double pitch, Action<TagLib.File> tag)
    {
        const int rate = 44100;
        using (var stream = File.Create(path))
        using (var encoder = new FlacEncoder(stream, rate, 2, 16, FlacCompression.Fast))
        {
            var samples = new int[rate * 3 * 2];
            for (var i = 0; i < samples.Length / 2; i++)
            {
                var value = (int)(Math.Sin(2 * Math.PI * pitch * i / rate) * 6000);
                samples[2 * i] = value;
                samples[2 * i + 1] = value;
            }

            encoder.Write(samples);
            encoder.Finish();
        }

        using var file = TagLib.File.Create(path);
        file.Tag.Performers = new[] { "Neon Harbor" };
        file.Tag.AlbumArtists = new[] { "Neon Harbor" };
        file.Tag.Album = "Coastal Lights";
        file.Tag.Year = 2021;
        file.Tag.TrackCount = 11;
        file.Tag.Disc = 1;
        file.Tag.DiscCount = 1;
        file.Tag.Genres = new[] { "Synthwave" };
        file.Tag.Composers = new[] { "M. Calder" };
        tag(file);
        file.Save();
    }

    /// <summary>A square cover: a diagonal gradient, a low sun and the album's name.</summary>
    internal static byte[] Cover(int size, SKColor from, SKColor to, string text)
    {
        using var bitmap = new SKBitmap(size, size);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var background = new SKPaint
            {
                Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size), new[] { from, to }, SKShaderTileMode.Clamp),
            };
            canvas.DrawRect(0, 0, size, size, background);
            using var sun = new SKPaint { Color = new SKColor(0xFF, 0xE3, 0xA3, 0xD0), IsAntialias = true };
            canvas.DrawCircle(size * 0.5f, size * 0.62f, size * 0.22f, sun);
            using var sea = new SKPaint { Color = from.WithAlpha(0xE0) };
            canvas.DrawRect(0, size * 0.66f, size, size * 0.34f, sea);
            using var lines = new SKPaint { Color = to.WithAlpha(0x90), StrokeWidth = size / 160f, IsAntialias = true };
            for (var i = 0; i < 6; i++)
            {
                var y = size * (0.7f + i * 0.045f);
                canvas.DrawLine(size * (0.2f + i * 0.03f), y, size * (0.8f - i * 0.03f), y, lines);
            }

            using var label = new SKPaint { Color = SKColors.White, TextSize = size / 14f, IsAntialias = true, TextAlign = SKTextAlign.Center };
            canvas.DrawText(text, size / 2f, size * 0.16f, label);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 88);
        return data.ToArray();
    }
}
