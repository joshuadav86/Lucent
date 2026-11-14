using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using Lucent.Settings;
using MeltySynth;

namespace Lucent.Audio.Midi;

/// <summary>
/// The instruments MIDI files play with: a SoundFont (.sf2). Lucent's own is GeneralUser GS by S. Christian Collins
/// (about 32 MB, free to use and pass on), too big to ship with every copy for files not everyone has, so it's
/// downloaded from Lucent's GitHub releases the first time a MIDI file plays, checked against its SHA-256, and kept in
/// Lucent's data folder. People can pick a SoundFont of their own instead (Options > Sound).
/// </summary>
/// <remarks>
/// CI fetches the file from its author's GitHub (tools/SoundFont/fetch.sh, pinned to one commit and checked against
/// <see cref="Sha256"/>) and publishes it once under a release of its own, midi-soundfont-v1. A different SoundFont
/// gets a new version, so copies of Lucent already out there keep downloading the one they were built for.
/// LUCENT_SOUNDFONT points at a file to use instead (the self-test uses that).
/// </remarks>
public static class SoundFonts
{
    public const string Version = "v1";

    /// <summary>What Lucent's own SoundFont is called, for the options.</summary>
    public const string OwnName = "GeneralUser GS";

    /// <summary>Roughly how big the download is, for saying so.</summary>
    public const int DownloadMegabytes = 32;

    /// <summary>GeneralUser GS 2.0.3's SHA-256, the same as in tools/SoundFont/fetch.sh.</summary>
    public const string Sha256 = "9575028c7a1f589f5770fccc8cff2734566af40cd26ed836944e9a5152688cfe";

    private const string ReleaseUrl = "https://github.com/joshuadav86/Lucent/releases/download/midi-soundfont-" + Version + "/";
    private const string FileName = "GeneralUser-GS.sf2";

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly object Gate = new();
    private static readonly SemaphoreSlim Downloading = new(1, 1);

    // The SoundFont loaded last, held while a MIDI file is open (it's 30 MB or so in memory) and only weakly after,
    // so it goes once no MIDI file has played for a while but comes straight back if one plays again meanwhile.
    private static string? _loadedPath;
    private static SoundFont? _held;
    private static WeakReference<SoundFont>? _kept;
    private static int _users;

    /// <summary>The SoundFont people chose for themselves, or null for Lucent's own. Each app sets this from its settings.</summary>
    public static string? Chosen { get; set; }

    /// <summary>Where Lucent's own SoundFont is kept once it's downloaded.</summary>
    public static string OwnPath => Path.Combine(AppDataFolder.Path, "SoundFonts", $"GeneralUser-GS-{Version}.sf2");

    public static bool IsOwnDownloaded => File.Exists(OwnPath);

    /// <summary>The SoundFont MIDI files play with now, or null when there isn't one yet (Lucent's own isn't downloaded).</summary>
    public static string? InUse
    {
        get
        {
            if (Environment.GetEnvironmentVariable("LUCENT_SOUNDFONT") is { Length: > 0 } custom)
            {
                return custom;
            }

            if (Chosen is { Length: > 0 } chosen && File.Exists(chosen))
            {
                return chosen;
            }

            return IsOwnDownloaded ? OwnPath : null;
        }
    }

    /// <summary>
    /// The SoundFont to play with, loaded (or the one loaded already). Pair with <see cref="Release"/>. Throws
    /// <see cref="SoundFontNeededException"/> when there's none yet.
    /// </summary>
    public static SoundFont Use()
    {
        var path = InUse ?? throw new SoundFontNeededException();
        lock (Gate)
        {
            if (!string.Equals(path, _loadedPath, StringComparison.Ordinal))
            {
                _held = null;
                _kept = null;
            }

            var soundFont = _held ?? (_kept is not null && _kept.TryGetTarget(out var kept) ? kept : null);
            if (soundFont is null)
            {
                try
                {
                    soundFont = new SoundFont(path);
                }
                catch (Exception ex) when (ex is not IOException || File.Exists(path))
                {
                    throw new InvalidDataException($"The SoundFont {Path.GetFileName(path)} couldn't be read: {ex.Message}", ex);
                }

                _loadedPath = path;
                _kept = new WeakReference<SoundFont>(soundFont);
            }

            _held = soundFont;
            _users++;
            return soundFont;
        }
    }

    /// <summary>A MIDI file playing with the SoundFont from <see cref="Use"/> has closed.</summary>
    public static void Release()
    {
        lock (Gate)
        {
            if (--_users <= 0)
            {
                _users = 0;
                _held = null;
            }
        }
    }

    /// <summary>Downloads Lucent's own SoundFont if it isn't here yet. Reports progress from 0 to 1.</summary>
    public static async Task DownloadAsync(Action<double>? progress, CancellationToken cancellation)
    {
        await Downloading.WaitAsync(cancellation);
        try
        {
            if (IsOwnDownloaded)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(OwnPath)!);
            var partial = OwnPath + ".part";
            try
            {
                using (var response = await Http.GetAsync(ReleaseUrl + FileName, HttpCompletionOption.ResponseHeadersRead, cancellation))
                {
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength ?? DownloadMegabytes * 1_000_000L;
                    await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                    await using var target = File.Create(partial);
                    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    var buffer = new byte[1 << 16];
                    long done = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                    {
                        await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                        hash.AppendData(buffer, 0, read);
                        done += read;
                        progress?.Invoke(Math.Min(1, done / (double)total));
                    }

                    if (!string.Equals(Convert.ToHexString(hash.GetHashAndReset()), Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new IOException("The instruments for MIDI files didn't download properly (they came out different from the original). Try again.");
                    }
                }

                File.Move(partial, OwnPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(partial))
                {
                    File.Delete(partial);
                }
            }
        }
        finally
        {
            Downloading.Release();
        }
    }

    /// <summary>Whether <paramref name="path"/> is a SoundFont that can be played with, and if not, why.</summary>
    public static string? Check(string path)
    {
        try
        {
            _ = new SoundFont(path);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}

/// <summary>A MIDI file was opened before there were any instruments to play it with: Lucent's own need downloading first.</summary>
public sealed class SoundFontNeededException : Exception
{
    public SoundFontNeededException()
        : base($"MIDI files play with a set of instruments Lucent downloads the first time (about {SoundFonts.DownloadMegabytes} MB). Play a MIDI file in the player to get them, or choose a SoundFont of your own under Options > Sound.")
    {
    }
}
