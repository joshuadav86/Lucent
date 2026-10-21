using NAudio.Wave;
using Velopack;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Starts Lucent. Velopack runs first: when the installer or an update launches Lucent.exe to set
/// up shortcuts or clean up, it does that and exits before any window opens. Uninstalling takes Lucent
/// out of Default apps.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => FileAssociations.Unregister())
            .Run();

        // The shared code leaves these to each platform. On Windows, NAudio's readers handle MP3,
        // WAV and AIFF, Media Foundation handles the rest, and MP3 is written with LAME.
        AudioReaderFactory.OpenWithSystemDecoder = path => new AudioFileReader(path);
        AudioEncoder.Mp3Encoder = Mp3Writer.Encode;
        // A video's frames saved as JPEG, by WPF's encoder.
        Editor.VideoExport.EncodeJpeg = Library.CoverArt.EncodeJpeg;

        // Checks decoding and every visual without opening a window, for CI (see SelfTest.cs).
        if (args.Length > 0 && args[0] == "--self-test")
        {
            return SelfTest.Run(args[1..]);
        }

        // Songs opened from Explorer (Lucent as the default player, or Open with). If Lucent is
        // already running, they go to it instead of opening a second one.
        var paths = args.Where(arg => !arg.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (paths.Count > 0 && OpenRequests.SendToRunning(paths))
        {
            return 0;
        }

        App.StartupPaths = paths;
        AppDataFolder.CopyFromOldName();

        var app = new App();
        ErrorLog.Watch(app);
        CrashReports.Start();
        app.InitializeComponent();
        app.Run();
        return 0;
    }
}
