using System.IO;
using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// The Apple Music app for Windows (or iTunes), through Windows' media controls. No Apple sign-in is
/// needed; whoever is signed in to Apple Music is who's playing.
/// </summary>
/// <remarks>
/// Apple's MusicKit only runs on Apple's own platforms, and MusicKit on the web plays in a browser, not
/// the app, so this is how Windows does it. The Mac controls the Music app with AppleScript instead
/// (see the Mac's Streaming/AppleMusicRemote.cs).
/// </remarks>
public sealed class AppleMusicRemote : MediaControlsRemote, IRemoteOpener
{
    public override string Name => "Apple Music";

    protected override string NotOpen => "Open the Apple Music app and play something";

    public override IReadOnlyList<string> ProcessNames { get; } = ["AppleMusic", "iTunes"];

    public bool IsOpen => AppOpener.IsRunning(ProcessNames);

    // The Apple Music app comes from the Store; iTunes from the Store or Apple's own installer.
    public async Task OpenAsync(CancellationToken cancel)
    {
        if (!await AppOpener.TryStoreAppAsync("AppleInc.AppleMusicWin")
            && !await AppOpener.TryStoreAppAsync("AppleInc.iTunes")
            && !AppOpener.TryProgram(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "iTunes", "iTunes.exe")))
        {
            throw new RemoteProblem("Lucent couldn't find Apple Music on this PC. Install it from the Microsoft Store.");
        }
    }

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions) =>
        sessions.FirstOrDefault(session => IsAppleMusic(session.SourceAppUserModelId));

    // The Apple Music app is "AppleInc.AppleMusicWin_…!App"; iTunes from Apple's site or the Store has iTunes in its name.
    private static bool IsAppleMusic(string? appId) =>
        appId is not null && (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("iTunes", StringComparison.OrdinalIgnoreCase));
}
