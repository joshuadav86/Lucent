using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Settings;

namespace Lucent.Streaming;

/// <summary>
/// Playlists (and Liked Songs) read whole, kept with the version they were read at, in memory for this run (shared by
/// every connection to the app) and on disk for the next, so opening one again doesn't read it all again: only its
/// version is asked for, and a playlist changed since is read afresh. While the app can't be asked (Spotify has asked
/// Lucent to wait, or there's no internet), the last copy of a playlist shows instead, whatever its version. The
/// account's list of playlists is kept the same way.
/// </summary>
public static class RemoteListStore
{
    // The ones opened most recently; older ones are let go.
    private const int Kept = 40;

    // A few in memory, for going back and forth between playlists.
    private const int KeptInMemory = 16;

    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    private static readonly Dictionary<string, Stored> Memory = new();

    private static string Folder => Path.Combine(AppDataFolder.Path, "Streaming lists");

    /// <summary>The list kept for <paramref name="context"/> at <paramref name="version"/>, or null when there's none, or an older one.</summary>
    public static async Task<RemoteList?> LoadAsync(string context, string version) =>
        await LoadStoredAsync(context) is { } stored && stored.Version == version ? Shown(stored) : null;

    /// <summary>
    /// The last list kept for <paramref name="context"/>, whatever its version, with when it was read, or null when
    /// there's none: for showing while the app can't be asked.
    /// </summary>
    public static async Task<(RemoteList List, DateTime ReadAt)?> LoadAnyAsync(string context) =>
        await LoadStoredAsync(context) is { } stored ? (Shown(stored), stored.ReadAt ?? DateTime.UtcNow) : null;

    /// <summary>Keeps <paramref name="list"/>, read whole at <paramref name="version"/>; it's written to disk in the background.</summary>
    public static void Save(string context, string version, RemoteList list)
    {
        if (list.Tracks.Count == 0)
        {
            return;
        }

        var stored = new Stored(context, version, list.Name, [.. list.Tracks], DateTime.UtcNow);
        Remember(stored);
        _ = Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(Folder);
                Write(PathOf(context), JsonSerializer.Serialize(stored, Options));
                foreach (var old in new DirectoryInfo(Folder).GetFiles("*.json").OrderByDescending(file => file.LastWriteTimeUtc).Skip(Kept))
                {
                    old.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // Read again next time instead.
            }
        });
    }

    /// <summary>Keeps the account's playlists, as listed in the library, for when the app can't be asked.</summary>
    public static void SavePlaylists(string app, IReadOnlyList<RemotePlaylist> playlists) => _ = Task.Run(() =>
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Write(PlaylistsPathOf(app), JsonSerializer.Serialize(playlists.ToList(), Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Listed again next time instead.
        }
    });

    /// <summary>The account's playlists as last listed, or null when there are none kept.</summary>
    public static Task<IReadOnlyList<RemotePlaylist>?> LoadPlaylistsAsync(string app) => Task.Run<IReadOnlyList<RemotePlaylist>?>(async () =>
    {
        try
        {
            var path = PlaylistsPathOf(app);
            if (!File.Exists(path))
            {
                return null;
            }

            await using var file = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<List<RemotePlaylist>>(file, Options) is { Count: > 0 } playlists ? playlists : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    });

    /// <summary>Lets every kept list go, as when the user signs out.</summary>
    public static void ForgetAll()
    {
        lock (Memory)
        {
            Memory.Clear();
        }

        try
        {
            if (Directory.Exists(Folder))
            {
                Directory.Delete(Folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only playlists' songs; they're let go as others are kept.
        }
    }

    private static RemoteList Shown(Stored stored) => new(stored.Context, stored.Name, stored.Tracks);

    private static async Task<Stored?> LoadStoredAsync(string context)
    {
        lock (Memory)
        {
            if (Memory.TryGetValue(context, out var kept))
            {
                return kept;
            }
        }

        var stored = await Task.Run<Stored?>(async () =>
        {
            try
            {
                var path = PathOf(context);
                if (!File.Exists(path))
                {
                    return null;
                }

                Stored? read;
                await using (var file = File.OpenRead(path))
                {
                    read = await JsonSerializer.DeserializeAsync<Stored>(file, Options);
                }

                if (read is null || read.Context != context || read.Tracks is null)
                {
                    return null;
                }

                // Opened again, so it's among the last to be let go. Kept from before Lucent noted when, it's as old as the file.
                var written = File.GetLastWriteTimeUtc(path);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                return read.ReadAt is null ? read with { ReadAt = written } : read;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
            {
                return null;
            }
        });

        if (stored is not null)
        {
            Remember(stored);
        }

        return stored;
    }

    private static void Remember(Stored stored)
    {
        lock (Memory)
        {
            if (Memory.Count >= KeptInMemory && !Memory.ContainsKey(stored.Context))
            {
                Memory.Clear();
            }

            Memory[stored.Context] = stored;
        }
    }

    private static void Write(string path, string json)
    {
        var temp = path + ".saving";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    // Named by a hash of what Spotify calls the list, as spotify:playlist:… can't be a file name on Windows.
    private static string PathOf(string context) =>
        Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)))[..24].ToLowerInvariant() + ".json");

    // Not .json, so it isn't let go with the playlists' songs.
    private static string PlaylistsPathOf(string app) => Path.Combine(Folder, $"{app.ToLowerInvariant()}.playlists");

    internal sealed record Stored(string Context, string Version, string Name, List<RemoteTrack> Tracks, DateTime? ReadAt = null);
}
