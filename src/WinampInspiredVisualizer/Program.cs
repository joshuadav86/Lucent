using NAudio.Wave;
using Velopack;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Starts Lucent. Velopack runs first: when the installer or an update launches Lucent.exe to set
/// up shortcuts or clean up, it does that and exits before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        AppDataFolder.CopyFromOldName();

        // The shared code leaves these to each platform. On Windows, NAudio's readers handle MP3,
        // WAV and AIFF, Media Foundation handles the rest, and MP3 is written with LAME.
        AudioReaderFactory.OpenWithSystemDecoder = path => new AudioFileReader(path);
        AudioEncoder.Mp3Encoder = Mp3Writer.Encode;

        var app = new App();
        ErrorLog.Watch(app);
        app.InitializeComponent();
        app.Run();
    }
}
