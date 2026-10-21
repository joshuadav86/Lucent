using System.Runtime.Versioning;
using Avalonia;
using Lucent.Mac.Audio;
using Lucent.Mac.Export;
using Velopack;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;

// Metal, Core Audio and AppKit: this app only runs on macOS.
[assembly: SupportedOSPlatform("macos")]

namespace Lucent.Mac;

/// <summary>
/// Starts Lucent. Velopack runs first, as on Windows: when the installer or an update launches Lucent
/// to finish setting up or clean up, it does that and exits before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build().Run();

        // The shared code leaves file decoding to each platform. On the Mac, Core Audio decodes
        // everything that isn't Ogg or Opus (those are decoded in Lucent.Core on every platform).
        AudioReaderFactory.OpenWithSystemDecoder = path => new MacAudioFileReader(path);
        // MP3 encoding is left to each platform too: on the Mac it's LAME, shipped inside the app.
        AudioEncoder.Mp3Encoder = Mp3Writer.Encode;
        // A video's frames saved as JPEG, by SkiaSharp's encoder.
        WinampInspiredVisualizer.Editor.VideoExport.EncodeJpeg = Library.CoverArt.EncodeJpeg;

        if (args.Length > 0 && args[0] == "--self-test")
        {
            return SelfTest.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == "--screenshots")
        {
            return Screenshots.Run(args[1..]);
        }

        // Settings, playlists and visuals from before Lucent was renamed come across on the first run.
        AppDataFolder.CopyFromOldName();
        CrashReports.Start();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The player builds the whole app menu itself (About, Hide, Quit), so Avalonia's own items stay out.
            .With(new MacOSPlatformOptions { DisableDefaultApplicationMenuItems = true })
            .LogToTrace();
}
