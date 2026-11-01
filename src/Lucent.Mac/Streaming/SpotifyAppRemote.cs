using System.Diagnostics;
using System.Globalization;
using Lucent.Settings;
using Lucent.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// The Spotify app on this Mac, through AppleScript: what's playing, its cover, and play, pause, skip, seek,
/// shuffle and repeat (on or off: its AppleScript can't repeat just one song, which SpotifyRemote asks the Web API for).
/// Lucent.Core's SpotifyRemote reads this first, so the song shows and the buttons work even while Spotify's
/// Web API has asked Lucent to wait. macOS asks once whether Lucent may control Spotify (Privacy &amp; Security,
/// Automation).
/// </summary>
/// <remarks>
/// Windows reads the Spotify app through Windows' media controls instead (see Windows' Streaming/SpotifyAppRemote.cs).
/// </remarks>
public sealed class SpotifyAppRemote : IRemotePlayer, IRemoteModes, IRemoteVolume, IRemoteOpener, IRemoteNotifier
{
    private const string Osascript = "/usr/bin/osascript";

    private const string NotOpen = "Open Spotify and play something, then Lucent can show and control it.";

    // Fields between tabs: state, then the song's spotify:track: URI, name, artist, album, length in milliseconds,
    // the position in seconds, its cover's address, whether it shuffles and repeats (true or false, empty when
    // it won't say), and its volume from 0 to 100.
    private const string ReadScript = """
        if application "Spotify" is not running then return "closed"
        tell application "Spotify"
            set playerState to player state as string
            if playerState is "stopped" then return "stopped"
            try
                set t to current track
                set modes to "" & tab & ""
                try
                    set modes to (shuffling as string) & tab & (repeating as string)
                end try
                set level to ""
                try
                    set level to (sound volume as string)
                end try
                return playerState & tab & (id of t) & tab & (name of t) & tab & (artist of t) & tab & (album of t) & tab & (duration of t) & tab & player position & tab & (artwork url of t) & tab & modes & tab & level
            on error
                return playerState
            end try
        end tell
        """;

    public string Name => "Spotify";

    /// <summary>Raised when Spotify tells the Mac its song changed, or that it played or paused.</summary>
    public event EventHandler? Changed
    {
        add => AppAnnouncements.Listen(AppAnnouncements.Spotify, value);
        remove => AppAnnouncements.StopListening(AppAnnouncements.Spotify, value);
    }

    public bool IsOpen => IsRunning();

    public Task OpenAsync(CancellationToken cancel) =>
        AppOpener.OpenAppAsync("com.spotify.client", "Lucent couldn't find Spotify on this Mac. Install it from spotify.com.", cancel);

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var fields = (await RunAsync(ReadScript, cancel)).Split('\t');
        var readAt = DateTime.UtcNow;
        switch (fields[0])
        {
            case "closed":
                throw new RemoteProblem(NotOpen);
            case "stopped":
                return null;
        }

        var playing = fields[0] == "playing";
        if (fields.Length < 7)
        {
            return new RemoteState(null, playing, TimeSpan.Zero, readAt);
        }

        // "spotify:track:4uLU6hMCjMI75M1A2tKUQC": the Web API's ID is the last part.
        var uri = fields[1];
        var id = uri[(uri.LastIndexOf(':') + 1)..];
        var art = fields.Length > 7 && fields[7].StartsWith("https://", StringComparison.Ordinal) ? fields[7] : null;
        var track = new RemoteTrack(id, fields[2], fields[3], fields[4], Seconds(fields[5]) / 1000, art, uri);
        bool? shuffle = fields.Length > 8 ? Flag(fields[8]) : null;
        RepeatMode? repeat = fields.Length > 9 ? Flag(fields[9]) switch { true => RepeatMode.All, false => RepeatMode.Off, null => null } : null;
        double? volume = fields.Length > 10 && int.TryParse(fields[10], NumberStyles.None, CultureInfo.InvariantCulture, out var percent)
            ? SpotifyVolume.FromSpotify(percent / 100.0)
            : null;
        return new RemoteState(track, playing, Seconds(fields[6]), readAt, Volume: volume, Shuffle: shuffle, Repeat: repeat);
    }

    // Spotify's own volume slider, which is also its Spotify Connect volume for this Mac. Its curve is much steeper
    // than Lucent's bar, so the bar's place is converted (SpotifyVolume).
    public Task SetVolumeAsync(double volume, CancellationToken cancel) =>
        TellAsync($"set sound volume to {SpotifyVolume.ToSpotifyPercent(volume)}", cancel);

    private static bool? Flag(string text) => text switch { "true" => true, "false" => false, _ => null };

    public Task SetShuffleAsync(bool on, CancellationToken cancel) => TellAsync($"set shuffling to {(on ? "true" : "false")}", cancel);

    public Task SetRepeatAsync(RepeatMode mode, CancellationToken cancel) => mode == RepeatMode.One
        ? throw new NotSupportedException("Spotify's AppleScript can't repeat one song.")
        : TellAsync($"set repeating to {(mode == RepeatMode.All ? "true" : "false")}", cancel);

    public Task PlayAsync(CancellationToken cancel) => TellAsync("play", cancel);

    public Task PauseAsync(CancellationToken cancel) => TellAsync("pause", cancel);

    public Task NextAsync(CancellationToken cancel) => TellAsync("next track", cancel);

    public Task PreviousAsync(CancellationToken cancel) => TellAsync("previous track", cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        TellAsync($"set player position to {Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)}", cancel);

    // SpotifyRemote fetches the cover from the track's address; there's nothing more here.
    public Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) => Task.FromResult<byte[]?>(null);

    // AppleScript writes numbers the way the Mac's region does, so "245,5" is as likely as "245.5".
    private static TimeSpan Seconds(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    private async Task TellAsync(string command, CancellationToken cancel)
    {
        var result = await RunAsync($"""
            if application "Spotify" is not running then return "closed"
            tell application "Spotify" to {command}
            return "ok"
            """, cancel);
        if (result == "closed")
        {
            throw new RemoteProblem(NotOpen);
        }
    }

    private static async Task<string> RunAsync(string script, CancellationToken cancel)
    {
        // A script naming an app that isn't installed makes macOS ask the user where it is, so Spotify has to be
        // running before one is run. Looked for away from the UI thread, as a read comes every second or so.
        if (!await Task.Run(IsRunning, cancel))
        {
            return "closed";
        }

        // With no script file, osascript reads the script from its input.
        var start = new ProcessStartInfo(Osascript)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new RemoteProblem("Lucent couldn't run AppleScript to reach Spotify.");
        try
        {
            await process.StandardInput.WriteAsync(script);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync(cancel);
            var error = process.StandardError.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            if (process.ExitCode != 0)
            {
                var message = await error;
                // -1743: the user said no (or hasn't been asked yet) in Privacy & Security, Automation.
                throw new RemoteProblem(message.Contains("-1743", StringComparison.Ordinal)
                    ? "Lucent isn't allowed to control Spotify. Turn it on in System Settings, Privacy & Security, Automation."
                    : "Spotify didn't answer just now.");
            }

            return (await output).TrimEnd('\n', '\r');
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill();
            }
            catch (InvalidOperationException)
            {
                // Already finished.
            }

            throw;
        }
    }

    private static bool IsRunning() => AppOpener.IsRunning("Spotify");

    public void Dispose()
    {
        // Nothing to let go of: each request is its own osascript run.
    }
}
