using System.Diagnostics;
using System.Globalization;
using WinampInspiredVisualizer.Streaming;

namespace Lucent.Mac.Streaming;

/// <summary>
/// The Mac's Music app, through AppleScript: what's playing, its cover, and play, pause, skip and seek.
/// No Apple sign-in is needed; whoever is signed in to the Music app is who's playing, Apple Music
/// subscription and library alike. macOS asks once whether Lucent may control Music (Privacy &amp; Security,
/// Automation), with the reason in Info.plist's NSAppleEventsUsageDescription.
/// </summary>
/// <remarks>
/// Apple's MusicKit framework is only for Swift, and its player plays inside the app that uses it rather
/// than controlling the Music app, so Lucent talks to the Music app the way Mac apps always have. Windows
/// uses Windows' media controls to reach the Apple Music app (see Windows' Streaming/AppleMusicRemote.cs).
/// </remarks>
public sealed class AppleMusicRemote : IRemotePlayer, IRemoteLists, IRemoteVolume
{
    private const string Osascript = "/usr/bin/osascript";

    // Fields between tabs: state, then the song's ID, name, artist, album, length and the position, in seconds, and
    // the ID of the playlist it's playing from (empty when there isn't one, as for a radio station), and Music's volume (0 to 100).
    // "is running" doesn't open Music, so Lucent never starts it by itself.
    private const string ReadScript = """
        if application "Music" is not running then return "closed"
        tell application "Music"
            set playerState to player state as string
            if playerState is "stopped" then return "stopped"
            try
                set t to current track
                set playlistId to ""
                try
                    set playlistId to persistent ID of current playlist
                end try
                return playerState & tab & (persistent ID of t) & tab & (name of t) & tab & (artist of t) & tab & (album of t) & tab & (duration of t) & tab & player position & tab & playlistId & tab & sound volume
            on error
                return playerState
            end try
        end tell
        """;

    private const string ArtworkScript = """
        tell application "Music"
            try
                return raw data of artwork 1 of current track
            on error
                return ""
            end try
        end tell
        """;

    // The playlist's name, then its songs' IDs, names, artists, albums and lengths, up to 1,000 songs. Each property comes
    // for every song at once (one request to Music, not one a song), each a list split by character 30, and they're
    // split from one another by character 31: characters no song name has.
    private const string ListScript = """
        if application "Music" is not running then return ""
        tell application "Music"
            try
                set p to current playlist
            on error
                return ""
            end try
            set n to count of tracks of p
            if n > 1000 then set n to 1000
            set sep to character id 31
            if n is 0 then return (name of p) & sep
            set AppleScript's text item delimiters to character id 30
            set ids to (persistent ID of tracks 1 thru n of p) as text
            set names to (name of tracks 1 thru n of p) as text
            set artists to (artist of tracks 1 thru n of p) as text
            set albums to (album of tracks 1 thru n of p) as text
            set lengths to (duration of tracks 1 thru n of p) as text
            set AppleScript's text item delimiters to ""
            return (name of p) & sep & ids & sep & names & sep & artists & sep & albums & sep & lengths
        end tell
        """;

    public string Name => "Apple Music";

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var fields = (await RunAsync(ReadScript, cancel)).Split('\t');
        var readAt = DateTime.UtcNow;
        switch (fields[0])
        {
            case "closed":
                throw new RemoteProblem("Open the Music app and play something, then Lucent can show and control it.");
            case "stopped":
                return null;
        }

        var playing = fields[0] == "playing";
        if (fields.Length < 7)
        {
            return new RemoteState(null, playing, TimeSpan.Zero, readAt);
        }

        var track = new RemoteTrack(fields[1], fields[2], fields[3], fields[4], Seconds(fields[5]));
        var playlist = fields.Length > 7 && fields[7].Length > 0 ? fields[7] : null;
        double? volume = fields.Length > 8 && int.TryParse(fields[8], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent)
            ? Math.Clamp(percent / 100.0, 0, 1)
            : null;
        return new RemoteState(track, playing, Seconds(fields[6]), readAt, playlist, Volume: volume);
    }

    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel) =>
        state.ListId is null ? null : ParseList(state.ListId, await RunAsync(ListScript, cancel));

    // Music's own volume, which it plays at on this Mac or an AirPlay speaker alike.
    public Task SetVolumeAsync(double volume, CancellationToken cancel) =>
        TellAsync($"set sound volume to {(int)Math.Round(Math.Clamp(volume, 0, 1) * 100)}", cancel);

    public Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel) =>
        // Played from its playlist, Music carries on through the playlist after it.
        TellAsync($"play (first track of (first playlist whose persistent ID is \"{Hex(list.Id)}\") whose persistent ID is \"{Hex(track.Id)}\")", cancel);

    /// <summary>The list <see cref="ListScript"/> prints, for the playlist with the ID <paramref name="id"/>, or null when there's none.</summary>
    internal static RemoteList? ParseList(string id, string text)
    {
        var sections = text.Split('\u001f');
        if (sections.Length < 2 || sections[0].Length == 0)
        {
            return null;
        }

        var tracks = new List<RemoteTrack>();
        if (sections.Length >= 6)
        {
            var columns = sections[1..6].Select(section => section.Split('\u001e')).ToArray();
            var count = columns.Min(column => column.Length);
            for (var i = 0; i < count; i++)
            {
                tracks.Add(new RemoteTrack(columns[0][i], columns[1][i], columns[2][i], columns[3][i], Seconds(columns[4][i])));
            }
        }

        return new RemoteList(id, sections[0], tracks);
    }

    // Music's persistent IDs are hexadecimal; anything else is left out, so an ID can't end the script's quotes.
    private static string Hex(string? id) => new((id ?? string.Empty).Where(Uri.IsHexDigit).ToArray());

    public Task PlayAsync(CancellationToken cancel) => TellAsync("play", cancel);

    public Task PauseAsync(CancellationToken cancel) => TellAsync("pause", cancel);

    public Task NextAsync(CancellationToken cancel) => TellAsync("next track", cancel);

    public Task PreviousAsync(CancellationToken cancel) => TellAsync("previous track", cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        TellAsync($"set player position to {Math.Max(0, position.TotalSeconds).ToString("0.###", CultureInfo.InvariantCulture)}", cancel);

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) => ParseData(await RunAsync(ArtworkScript, cancel));

    /// <summary>
    /// Raw data as osascript prints it, «data JPEG FFD8…» or «data PNGf 8950…» (the type, then the bytes in
    /// hexadecimal), as the bytes. Null when there aren't any.
    /// </summary>
    internal static byte[]? ParseData(string text)
    {
        const string Start = "«data ";
        var at = text.IndexOf(Start, StringComparison.Ordinal);
        var end = text.LastIndexOf('»');
        if (at < 0 || end < at + Start.Length + 4)
        {
            return null;
        }

        var hex = text[(at + Start.Length + 4)..end].Trim();
        try
        {
            return hex.Length > 0 ? Convert.FromHexString(hex) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // AppleScript writes numbers the way the Mac's region does, so "245,5" is as likely as "245.5".
    private static TimeSpan Seconds(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    private async Task TellAsync(string command, CancellationToken cancel)
    {
        var result = await RunAsync($"""
            if application "Music" is not running then return "closed"
            tell application "Music" to {command}
            return "ok"
            """, cancel);
        if (result == "closed")
        {
            throw new RemoteProblem("Open the Music app and play something, then Lucent can control it.");
        }
    }

    private static async Task<string> RunAsync(string script, CancellationToken cancel)
    {
        // With no script file, osascript reads the script from its input.
        var start = new ProcessStartInfo(Osascript)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(start) ?? throw new RemoteProblem("Lucent couldn't run AppleScript to reach the Music app.");
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
                    ? "Lucent isn't allowed to control Music. Turn it on in System Settings, Privacy & Security, Automation."
                    : "The Music app didn't answer just now.");
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

    public void Dispose()
    {
        // Nothing to let go of: each request is its own osascript run.
    }
}
