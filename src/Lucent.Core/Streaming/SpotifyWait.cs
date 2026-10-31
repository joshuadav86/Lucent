using System.Globalization;
using Lucent.Settings;

namespace Lucent.Streaming;

/// <summary>
/// How long Spotify last asked Lucent to wait (a 429 with Retry-After), shared by every connection to it and kept on
/// disk, so nothing asks again until then: not a connection made just to list playlists, and not Lucent started again.
/// Spotify counts all of Lucent's requests together, and asking while it has said to wait only makes it wait longer.
/// </summary>
public static class SpotifyWait
{
    private static readonly object Gate = new();
    private static DateTime? _until;

    // Longer than Spotify has ever been seen to ask, so a clock set wrong can't stop Lucent asking for days.
    private static readonly TimeSpan Longest = TimeSpan.FromHours(24);

    private static string FilePath => Path.Combine(AppDataFolder.Path, "Spotify wait.txt");

    /// <summary>When Lucent may ask Spotify again (UTC), or a time already past.</summary>
    public static DateTime Until
    {
        get
        {
            lock (Gate)
            {
                _until ??= Load();
                return _until.Value;
            }
        }
    }

    /// <summary>True while Spotify has asked Lucent to wait.</summary>
    public static bool IsWaiting => DateTime.UtcNow < Until;

    /// <summary>How much longer Spotify asked Lucent to wait, or zero.</summary>
    public static TimeSpan Left => Until - DateTime.UtcNow is { Ticks: > 0 } left ? left : TimeSpan.Zero;

    /// <summary>Spotify asked Lucent to wait this long, from now. A shorter wait never cuts a longer one short.</summary>
    public static void WaitFor(TimeSpan wait)
    {
        if (wait <= TimeSpan.Zero)
        {
            return;
        }

        var until = DateTime.UtcNow + (wait > Longest ? Longest : wait);
        lock (Gate)
        {
            _until ??= Load();
            if (until <= _until.Value)
            {
                return;
            }

            _until = until;
        }

        // Only a long wait is worth remembering past a restart.
        if (wait >= TimeSpan.FromMinutes(1))
        {
            Save(until);
        }
    }

    /// <summary>"about 8 hours", "about 12 minutes" or "a moment", for the notes that say why.</summary>
    public static string LeftText()
    {
        var left = Left;
        return left > TimeSpan.FromHours(1.5) ? $"about {left.TotalHours:0} hours"
            : left > TimeSpan.FromMinutes(1.5) ? $"about {left.TotalMinutes:0} minutes"
            : "a moment";
    }

    private static DateTime Load()
    {
        try
        {
            if (File.Exists(FilePath)
                && DateTime.TryParse(File.ReadAllText(FilePath).Trim(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var until)
                && until.Kind == DateTimeKind.Utc && until - DateTime.UtcNow < Longest)
            {
                return until;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Asked again, then; Spotify says to wait again if it still minds.
        }

        return DateTime.MinValue;
    }

    private static void Save(DateTime until)
    {
        try
        {
            Directory.CreateDirectory(AppDataFolder.Path);
            File.WriteAllText(FilePath, until.ToString("O", CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept for this run only.
        }
    }
}
