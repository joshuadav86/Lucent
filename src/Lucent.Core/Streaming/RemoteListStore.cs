using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Settings;

namespace Lucent.Streaming;

/// <summary>
/// Long playlists (and Liked Songs) read whole, kept on disk with the version they were read at, so the next time
/// Lucent starts a playlist of thousands of songs shows whole straight away, rather than its first thousand while the
/// rest is read again over a minute or more. A playlist changed since is read afresh.
/// </summary>
public static class RemoteListStore
{
    // The ones played from most recently; older ones are let go.
    private const int Kept = 12;

    private static readonly JsonSerializerOptions Options = new() { IgnoreReadOnlyProperties = true };

    private static string Folder => Path.Combine(AppDataFolder.Path, "Streaming lists");

    /// <summary>The list kept for <paramref name="context"/> at <paramref name="version"/>, or null when there's none, or an older one.</summary>
    public static Task<RemoteList?> LoadAsync(string context, string version) => Task.Run<RemoteList?>(async () =>
    {
        try
        {
            var path = PathOf(context);
            if (!File.Exists(path))
            {
                return null;
            }

            Stored? stored;
            await using (var file = File.OpenRead(path))
            {
                stored = await JsonSerializer.DeserializeAsync<Stored>(file, Options);
            }

            if (stored is null || stored.Context != context || stored.Version != version || stored.Tracks is null)
            {
                return null;
            }

            // Played from again, so it's among the last to be let go.
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
            return new RemoteList(context, stored.Name, stored.Tracks);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    });

    /// <summary>Keeps <paramref name="list"/>, read whole at <paramref name="version"/>, in the background.</summary>
    public static void Save(string context, string version, RemoteList list) => _ = Task.Run(() =>
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var path = PathOf(context);
            var temp = path + ".saving";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Stored(context, version, list.Name, [.. list.Tracks]), Options));
            File.Move(temp, path, overwrite: true);
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

    /// <summary>Lets every kept list go, as when the user signs out.</summary>
    public static void ForgetAll()
    {
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

    // Named by a hash of what Spotify calls the list, as spotify:playlist:… can't be a file name on Windows.
    private static string PathOf(string context) =>
        Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)))[..24].ToLowerInvariant() + ".json");

    internal sealed record Stored(string Context, string Version, string Name, List<RemoteTrack> Tracks);
}
