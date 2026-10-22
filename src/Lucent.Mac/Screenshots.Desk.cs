using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using SkiaSharp;
using WinampInspiredVisualizer.Layout;
using WinampInspiredVisualizer.Radio;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac;

/// <summary>
/// The website's shots of several windows at once: every window in Lucent full screen, and a station playing beside the
/// radio. Each window is drawn on its own at the size it'd have, then put together into one picture.
/// Two settings fill in what the headless renderer can't: LUCENT_PREVIEW_COVERS, a folder of covers named by artist
/// or station ("Bicep.png", "Groove Salad.jpg"; made-up covers without it), and LUCENT_PREVIEW_VISUAL, a picture of a
/// visual to show in the visuals window (the visuals themselves need the graphics chip).
/// </summary>
public static partial class Screenshots
{
    private const string VisualName = "Cosmic Jellyfish";

    private static RemoteTrack NowPlaying => new("preview", "Atlas", "Bicep", "Isles", TimeSpan.FromSeconds(352));

    private static RadioStation GrooveSalad => FeaturedStations.Stations.First(station => station.Name == "Groove Salad");

    private const string StationSong = "Thievery Corporation - Lebanese Blonde";

    /// <summary>The cover for an artist or station from LUCENT_PREVIEW_COVERS, or a made-up one.</summary>
    private static byte[] CoverFor(string name)
    {
        if (Environment.GetEnvironmentVariable("LUCENT_PREVIEW_COVERS") is { Length: > 0 } folder)
        {
            foreach (var extension in new[] { ".png", ".jpg", ".jpeg" })
            {
                var path = Path.Combine(folder, name + extension);
                if (File.Exists(path))
                {
                    return File.ReadAllBytes(path);
                }
            }
        }

        return TrackInfoPreviews.Cover(800, new SKColor(0x1E, 0x2A, 0x5E), new SKColor(0xF2, 0x5C, 0x4B), name.ToUpperInvariant());
    }

    private static Window PreviewArtworkCover() =>
        ArtworkWindow.CreatePreview(new AudioPlayer(), new AppSettings(), NowPlaying, CoverFor(NowPlaying.Artist));

    /// <summary>The visuals window with a picture of a visual in it, if LUCENT_PREVIEW_VISUAL gives one.</summary>
    private static Window PreviewVisualsLive()
    {
        var window = PreviewVisuals(VisualName);
        if (Environment.GetEnvironmentVariable("LUCENT_PREVIEW_VISUAL") is { Length: > 0 } path && File.Exists(path))
        {
            window.VisualizerFrame.Child = new Image { Source = new Bitmap(path), Stretch = Stretch.UniformToFill };
        }

        window.Opened += (_, _) => Dispatcher.UIThread.Post(() => window.PresetName.Text = VisualName);
        return window;
    }

    private static MainWindow PlayerOnStation()
    {
        var player = new MainWindow();
        player.PreviewSkin("Lucent");
        var station = GrooveSalad;
        player.PreviewStation(station, StationSong, $"{station.Name} · {station.Bitrate} kbps {station.Codec} · live", TimeSpan.FromMinutes(47.4));
        return player;
    }

    private static Window PreviewPlayerRadio() => PlayerOnStation();

    private static Window RadioWindowFor(MainWindow player)
    {
        var settings = new AppSettings { RadioSection = RadioGuide.Featured };
        var featured = FeaturedStations.Stations;
        RadioGuide.SetFavorite(settings, featured[0], true);
        RadioGuide.SetFavorite(settings, featured[2], true);
        RadioGuide.Remember(settings, featured[0]);
        return new RadioWindow(player, settings);
    }

    private static Window PreviewRadioPlaying() => RadioWindowFor(PlayerOnStation());

    /// <summary>Groove Salad on: the player over the artwork window with the station's art, beside the radio.</summary>
    private static Window PreviewRadioDesk()
    {
        var player = PlayerOnStation();
        var playerShot = Draw(player, null);
        var radioShot = Draw(RadioWindowFor(player), null);
        var artworkHeight = radioShot.Size.Height - playerShot.Size.Height;
        var artwork = ArtworkWindow.CreatePreview(new AudioPlayer(), new AppSettings(),
            new RemoteTrack(GrooveSalad.Id, "Lebanese Blonde", "Thievery Corporation", GrooveSalad.Name, TimeSpan.Zero), CoverFor(GrooveSalad.Name));
        var artworkShot = Draw(artwork, new Size(playerShot.Size.Width, artworkHeight));
        return Compose(new Size(playerShot.Size.Width + radioShot.Size.Width, radioShot.Size.Height), new[]
        {
            (playerShot, new Point(0, 0)),
            (artworkShot, new Point(0, playerShot.Size.Height)),
            (radioShot, new Point(playerShot.Size.Width, 0)),
        });
    }

    /// <summary>Every window, laid out by Lucent full screen on a 2560 × 1440 screen, with Bicep playing.</summary>
    private static Window PreviewFullScreen()
    {
        var player = PreviewPlayer("Lucent");
        var windows = new (Window Window, TileKind Kind)[]
        {
            (player, TileKind.Player),
            (PreviewEqualizer(Draw(player, null, close: false).Size.Width), TileKind.Equalizer),
            (PreviewQueue(), TileKind.Queue),
            (PreviewLibrary(), TileKind.Library),
            (PreviewArtworkCover(), TileKind.Artwork),
            (PreviewVisualsLive(), TileKind.Visuals),
            (PreviewAnalyzer(), TileKind.Analyzer),
            (RadioWindowFor(new MainWindow()), TileKind.Radio),
        };

        // The player and the equalizer size themselves: they're measured first and keep their size.
        var tiles = windows.Select(entry =>
        {
            var fixedSize = entry.Kind is TileKind.Player or TileKind.Equalizer;
            var size = fixedSize ? Draw(entry.Window, null, close: false).Size : new Size(entry.Window.Width, entry.Window.Height);
            return new TileWindow(entry.Kind.ToString(), entry.Kind, fixedSize ? size.Width : entry.Window.MinWidth,
                fixedSize ? size.Height : entry.Window.MinHeight, size.Width, size.Height, FixedSize: fixedSize);
        }).ToList();
        var screen = new TileRect(0, 0, 2560, 1440);
        var layout = FullScreenLayout.Arrange(screen, tiles);
        if (layout.LeftOut.Count > 0)
        {
            Console.WriteLine($"FullScreen: left out {string.Join(", ", layout.LeftOut)}");
        }

        var shots = windows
            .Where(entry => layout.Places.ContainsKey(entry.Kind.ToString()))
            .Select(entry =>
            {
                var place = layout.Places[entry.Kind.ToString()];
                var fixedSize = entry.Kind is TileKind.Player or TileKind.Equalizer;
                return (Draw(entry.Window, fixedSize ? null : new Size(place.Width, place.Height)), new Point(place.Left, place.Top));
            }).ToList();
        foreach (var (window, _) in windows)
        {
            window.Close();
        }

        return Compose(new Size(screen.Width, screen.Height), shots);
    }

    /// <summary>Draws a window, at <paramref name="size"/> if given, the way the catalog draws one.</summary>
    private static Bitmap Draw(Window window, Size? size, bool close = true)
    {
        if (size is { } wanted)
        {
            window.Width = wanted.Width;
            window.Height = wanted.Height;
        }

        window.Show();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{window.GetType().Name} drew nothing.");
        if (close)
        {
            window.Close();
        }

        return frame;
    }

    /// <summary>A window showing the pictures put together, so the catalog saves it like any other.</summary>
    private static Window Compose(Size size, IEnumerable<(Bitmap Picture, Point At)> pictures)
    {
        var canvas = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height));
        using (var context = canvas.CreateDrawingContext())
        {
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x10)), new Rect(size));
            foreach (var (picture, at) in pictures)
            {
                context.DrawImage(picture, new Rect(at, picture.Size));
            }
        }

        return new Window
        {
            Width = size.Width,
            Height = size.Height,
            SystemDecorations = SystemDecorations.None,
            Content = new Image { Source = canvas, Width = size.Width, Height = size.Height },
        };
    }
}
