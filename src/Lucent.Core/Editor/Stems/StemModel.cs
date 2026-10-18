using System.Net.Http;
using System.Security.Cryptography;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>
/// The stem splitter's model file. It's too big to ship with every copy of Lucent for a tool not everyone
/// uses, so it's downloaded from Lucent's GitHub releases the first time it's needed, checked against its
/// SHA-256, and kept in Lucent's data folder.
/// </summary>
/// <remarks>
/// CI makes the file (tools/StemModel) and publishes it once under a release of its own, stem-model-v1.
/// A different model gets a new version, so copies of Lucent already out there keep downloading the one
/// they were built for. LUCENT_STEM_MODEL points at a file to use instead (the self-test uses that).
/// </remarks>
public static class StemModel
{
    public const string Version = "v1";

    private const string ReleaseUrl = "https://github.com/joshuadav86/Lucent/releases/download/stem-model-" + Version + "/";
    private const string FileName = "htdemucs.onnx";

    /// <summary>Roughly how big the download is, for asking first.</summary>
    public const int DownloadMegabytes = 85;

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    private static readonly object Gate = new();
    private static StemSplitter? _splitter;

    public static string FilePath => Environment.GetEnvironmentVariable("LUCENT_STEM_MODEL") is { Length: > 0 } custom
        ? custom
        : Path.Combine(AppDataFolder.Path, "Stems", $"htdemucs-{Version}.onnx");

    public static bool IsDownloaded => File.Exists(FilePath);

    /// <summary>Downloads the model if it isn't here yet. Reports progress from 0 to 1.</summary>
    public static async Task DownloadAsync(Action<double>? progress, CancellationToken cancellation)
    {
        if (IsDownloaded)
        {
            return;
        }

        var expected = (await Http.GetStringAsync(ReleaseUrl + FileName + ".sha256", cancellation)).Split(' ', '\n')[0].Trim();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var partial = FilePath + ".part";
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

                var actual = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("The stem splitter didn't download properly (it came out different from the original). Try again.");
                }
            }

            File.Move(partial, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(partial))
            {
                File.Delete(partial);
            }
        }
    }

    /// <summary>
    /// The splitter, loaded the first time it's asked for and kept for the rest of the session, since loading
    /// it onto the graphics card takes a few seconds. The model must have been downloaded.
    /// </summary>
    public static StemSplitter Splitter
    {
        get
        {
            lock (Gate)
            {
                return _splitter ??= StemSplitter.Open(FilePath);
            }
        }
    }
}
