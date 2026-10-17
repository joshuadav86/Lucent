using System.Runtime.Versioning;
using Avalonia;
using Lucent.Mac.Audio;
using Lucent.Mac.Export;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;

// Metal, Core Audio and AppKit: this app only runs on macOS.
[assembly: SupportedOSPlatform("macos")]

namespace Lucent.Mac;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The shared code leaves file decoding to each platform. On the Mac, Core Audio decodes
        // everything that isn't Ogg or Opus (those are decoded in Lucent.Core on every platform).
        AudioReaderFactory.OpenWithSystemDecoder = path => new MacAudioFileReader(path);
        // MP3 encoding is left to each platform too: on the Mac it's LAME, shipped inside the app.
        AudioEncoder.Mp3Encoder = Mp3Writer.Encode;

        if (args.Length > 0 && args[0] == "--self-test")
        {
            return SelfTest.Run(args[1..]);
        }

        if (args.Length > 0 && args[0] == "--screenshots")
        {
            return Screenshots.Run(args[1..]);
        }

        ErrorLog.Watch();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
