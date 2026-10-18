using System.IO;
using System.Text.Json;

namespace WinampInspiredVisualizer.Stores;

/// <summary>How a sync is going, for showing in the window.</summary>
/// <param name="Done">Purchases finished so far in this sync.</param>
/// <param name="Total">Purchases this sync is downloading.</param>
/// <param name="Current">What's downloading now, like "Artist - Album".</param>
/// <param name="CurrentProgress">How far through the current download, 0 to 1.</param>
public sealed record BandcampSyncProgress(int Done, int Total, string Current, double CurrentProgress);

/// <summary>What a sync did.</summary>
public sealed record BandcampSyncResult(int Downloaded, int Failed, IReadOnlyList<string> Problems);

/// <summary>
/// Brings a Bandcamp collection into the library: downloads each purchase not synced before and hands it
/// to <see cref="StoreImporter"/>, which unpacks it into the Bandcamp folder. Purchases are remembered
/// once they're in, so each one only downloads once, even if its files are later moved or deleted.
/// </summary>
public sealed class BandcampSync
{
    private readonly StoreImporter _importer;
    private readonly string _recordPath;
    private readonly HashSet<string> _synced;

    /// <param name="recordPath">A file listing the purchases already synced.</param>
    public BandcampSync(StoreImporter importer, string recordPath)
    {
        _importer = importer;
        _recordPath = recordPath;
        _synced = Load(recordPath);
    }

    /// <summary>Whether any purchase has been synced (or skipped) yet; false before the first sync.</summary>
    public bool HasSynced => _synced.Count > 0;

    /// <summary>The purchases in the collection that haven't been synced yet. Pre-orders wait until they're released.</summary>
    public async Task<IReadOnlyList<BandcampPurchase>> FindNewAsync(BandcampClient client, CancellationToken token)
    {
        var username = await client.GetUsernameAsync(token);
        var purchases = await client.GetPurchasesAsync(username, token);
        return purchases.Where(purchase => !purchase.IsPreorder && !_synced.Contains(purchase.Id)).ToList();
    }

    /// <summary>Marks purchases as synced without downloading them, for "only new purchases from now on".</summary>
    public void Skip(IEnumerable<BandcampPurchase> purchases)
    {
        foreach (var purchase in purchases)
        {
            _synced.Add(purchase.Id);
        }

        Save();
    }

    /// <summary>Downloads and imports these purchases, one at a time, in <paramref name="format"/> where it's offered.</summary>
    public async Task<BandcampSyncResult> DownloadAsync(BandcampClient client, IReadOnlyList<BandcampPurchase> purchases, string format,
        IProgress<BandcampSyncProgress>? progress, CancellationToken token)
    {
        var downloaded = 0;
        var problems = new List<string>();
        for (var i = 0; i < purchases.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var purchase = purchases[i];
            var name = $"{purchase.Artist} - {purchase.Title}";
            progress?.Report(new BandcampSyncProgress(i, purchases.Count, name, 0));
            try
            {
                var download = await client.GetDownloadAsync(purchase, format, token);
                if (download is null)
                {
                    // Nothing to download (merch, or a refund): nothing to try again either.
                    Remember(purchase.Id);
                    continue;
                }

                var file = await DownloadToTemporaryAsync(client, download, new Progress<double>(fraction =>
                    progress?.Report(new BandcampSyncProgress(i, purchases.Count, name, fraction))), token);
                try
                {
                    var result = await Task.Run(() => _importer.Import(file, MusicStore.Bandcamp), token);
                    if (result.Outcome is StoreImportOutcome.Imported or StoreImportOutcome.AlreadyImported)
                    {
                        Remember(purchase.Id);
                        downloaded += result.Outcome == StoreImportOutcome.Imported ? 1 : 0;
                    }
                    else
                    {
                        problems.Add($"{name}: the download couldn't be unpacked.");
                    }
                }
                finally
                {
                    TryDelete(file);
                }
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                // One failed purchase shouldn't stop the rest, whatever went wrong; it's tried again next sync.
                problems.Add($"{name}: {ex.Message}");
            }
        }

        progress?.Report(new BandcampSyncProgress(purchases.Count, purchases.Count, string.Empty, 1));
        return new BandcampSyncResult(downloaded, problems.Count, problems);
    }

    /// <summary>Downloads into a hidden folder in the music folder, so the library never sees it half-written.</summary>
    private async Task<string> DownloadToTemporaryAsync(BandcampClient client, BandcampDownload download, IProgress<double> progress,
        CancellationToken token)
    {
        var extension = download.IsZip ? ".zip" : BandcampClient.TrackExtension(download.Format);
        var name = StoreImporter.SafeName($"{download.Artist} - {download.Title}");
        var folder = Path.Combine(_importer.MusicFolder, ".lucent-downloading");
        var info = Directory.CreateDirectory(folder);
        try
        {
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception)
        {
            // The dot hides it everywhere but Windows.
        }

        var path = Path.Combine(folder, name + extension);
        try
        {
            await client.DownloadAsync(download, path, progress, token);
        }
        catch
        {
            TryDelete(path);
            throw;
        }

        return path;
    }

    private void Remember(string id)
    {
        _synced.Add(id);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
            File.WriteAllText(_recordPath, JsonSerializer.Serialize(_synced.Order().ToList()));
        }
        catch (Exception)
        {
            // Forgetting means a purchase downloads again next time; the importer then finds it already in place.
        }
    }

    private static HashSet<string> Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new List<string>());
            }
        }
        catch (Exception)
        {
            // A damaged record: purchases are offered again, and the importer skips any already in place.
        }

        return new HashSet<string>();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Left in the hidden downloading folder, which the library doesn't list.
        }
    }
}
