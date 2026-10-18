using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Stores;

/// <summary>
/// Brings music bought on Bandcamp and Beatport into the library: by watching the Downloads folder for
/// their downloads and unpacking them, and (once signed in to Bandcamp) by downloading new Bandcamp
/// purchases from the account. Each store's music lands in its own folder, which is linked into the library.
/// </summary>
public sealed class StoreService : IDisposable
{
    // A download is tried again a few times while the browser or virus scanner still has it open.
    private const int MaxAttempts = 20;

    private readonly AppSettings _settings;
    private readonly Action<string> _linkFolder;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _pendingTimer;
    private readonly Dictionary<string, int> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _importing = new(1, 1);
    private FileSystemWatcher? _watcher;
    private StoreImporter _importer;
    private CancellationTokenSource? _sync;

    /// <param name="linkFolder">Puts a folder into the library (if it isn't already).</param>
    public StoreService(AppSettings settings, Action<string> linkFolder)
    {
        _settings = settings;
        _linkFolder = linkFolder;
        _importer = NewImporter();
        _pendingTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _pendingTimer.Tick += async (_, _) => await ImportPendingAsync();
    }

    /// <summary>Raised on the UI thread when <see cref="Status"/>, <see cref="Progress"/> or the sign-in changes.</summary>
    public event EventHandler? Changed;

    /// <summary>What happened last, like "Added Artist - Album to your library".</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>How the running sync is going, or null when none is running.</summary>
    public BandcampSyncProgress? Progress { get; private set; }

    public bool IsSyncing => _sync is not null;

    public bool IsSignedIn => BandcampSignIn.Load() is not null;

    /// <summary>The signed-in fan's username, kept in the settings for showing; null when signed out.</summary>
    public string? Username => IsSignedIn ? _settings.BandcampUsername : null;

    public string WatchFolder => _settings.DownloadsWatchFolder ?? DownloadsFolder();

    /// <summary>Where store music goes, with a folder per store inside.</summary>
    public string MusicFolder => _settings.StoreMusicFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);

    private string RecordFolder => Path.Combine(AppDataFolder.Path, "Stores");

    private bool Importing => _settings.BandcampImport || _settings.BeatportImport;

    /// <summary>Starts watching for downloads and, when it's on, syncs new purchases. Called once when Lucent starts.</summary>
    public void Start()
    {
        ApplySettings();
        if (_settings.BandcampSync && IsSignedIn)
        {
            _ = SyncNewAsync();
        }
    }

    /// <summary>Picks up changed folders or switches: re-watches, and links the store folders into the library.</summary>
    public void ApplySettings()
    {
        _importer = NewImporter();
        _watcher?.Dispose();
        _watcher = null;
        _pending.Clear();
        _pendingTimer.Stop();
        LinkLibraryFolders();
        if (!Importing || !Directory.Exists(WatchFolder))
        {
            return;
        }

        try
        {
            // Browsers download to a temporary name and rename it when done, so renames matter most.
            _watcher = new FileSystemWatcher(WatchFolder)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            _watcher.Created += OnDownloadChanged;
            _watcher.Changed += OnDownloadChanged;
            _watcher.Renamed += OnDownloadChanged;
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            SetStatus("Lucent can't watch that folder for downloads.");
            return;
        }

        // Downloads that arrived while Lucent was closed.
        try
        {
            foreach (var file in Directory.EnumerateFiles(WatchFolder).Where(IsCandidate).Where(file => !_importer.WasSeen(file)))
            {
                _pending[file] = 0;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Looked at again when something new arrives.
        }

        if (_pending.Count > 0)
        {
            _pendingTimer.Start();
        }
    }

    /// <summary>Signs in through Bandcamp's own page. Returns false if the window was closed first.</summary>
    public bool SignIn(System.Windows.Window owner)
    {
        if (BandcampSignInWindow.SignIn(owner) is not { } result)
        {
            return false;
        }

        BandcampSignIn.Save(result.Cookies);
        _settings.BandcampUsername = result.Username;
        _settings.Save();
        LinkLibraryFolders();
        SetStatus(string.Empty);
        return true;
    }

    public void SignOut()
    {
        Cancel();
        BandcampSignIn.Clear();
        _settings.BandcampSync = false;
        _settings.BandcampUsername = null;
        _settings.Save();
        SetStatus(string.Empty);
    }

    /// <summary>The purchases not in the library yet, or null if the collection couldn't be read (the status says why).</summary>
    public async Task<IReadOnlyList<BandcampPurchase>?> FindNewAsync()
    {
        if (BandcampSignIn.Load() is not { } cookies)
        {
            return null;
        }

        SetStatus("Looking at your Bandcamp collection…");
        try
        {
            using var client = new BandcampClient(cookies);
            var found = await NewSync().FindNewAsync(client, CancellationToken.None);
            SetStatus(found.Count switch
            {
                0 => "Your library has everything in your Bandcamp collection.",
                1 => "1 purchase isn't in your library yet.",
                var count => $"{count} purchases aren't in your library yet.",
            });
            return found;
        }
        catch (Exception ex) when (Describe(ex) is { } problem)
        {
            SetStatus(problem);
            return null;
        }
        catch (Exception ex)
        {
            SetStatus(Unexpected(ex));
            return null;
        }
    }

    /// <summary>Marks these purchases as synced without downloading them.</summary>
    public void Skip(IEnumerable<BandcampPurchase> purchases) => NewSync().Skip(purchases);

    /// <summary>Downloads these purchases into the library.</summary>
    public async Task DownloadAsync(IReadOnlyList<BandcampPurchase> purchases)
    {
        if (IsSyncing || BandcampSignIn.Load() is not { } cookies)
        {
            return;
        }

        LinkLibraryFolders();
        var cancel = new CancellationTokenSource();
        _sync = cancel;
        Progress = new BandcampSyncProgress(0, purchases.Count, string.Empty, 0);
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            using var client = new BandcampClient(cookies);
            var progress = new Progress<BandcampSyncProgress>(value =>
            {
                Progress = value;
                Changed?.Invoke(this, EventArgs.Empty);
            });
            var result = await NewSync().DownloadAsync(client, purchases, _settings.BandcampFormat, progress, cancel.Token);
            SetStatus(result.Failed > 0
                ? $"Added {Count(result.Downloaded)} to your library. {Count(result.Failed)} didn't download and will be tried again next sync: {result.Problems[0]}"
                : $"Added {Count(result.Downloaded)} to your library.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            SetStatus("Stopped. The rest download next sync.");
        }
        catch (Exception ex) when (Describe(ex) is { } problem)
        {
            SetStatus(problem);
        }
        catch (Exception ex)
        {
            SetStatus(Unexpected(ex));
        }
        finally
        {
            _sync = null;
            Progress = null;
            cancel.Dispose();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Downloads whatever is new in the collection.</summary>
    public async Task SyncNewAsync()
    {
        if (!IsSyncing && await FindNewAsync() is { Count: > 0 } found)
        {
            await DownloadAsync(found);
        }
    }

    public void Cancel() => _sync?.Cancel();

    public void Dispose()
    {
        Cancel();
        _pendingTimer.Stop();
        _watcher?.Dispose();
    }

    private static string Count(int purchases) => purchases == 1 ? "1 purchase" : $"{purchases} purchases";

    /// <summary>A sentence for the problems a sync can run into, or null for anything else (see <see cref="Unexpected"/>).</summary>
    private static string? Describe(Exception ex) => ex switch
    {
        BandcampSignInException => ex.Message,
        BandcampPageException => ex.Message + " Lucent needs an update to sync again; importing downloads still works.",
        HttpRequestException => "Bandcamp couldn't be reached. Check the internet connection and try again.",
        JsonException => "Bandcamp's pages have changed. Lucent needs an update to sync again; importing downloads still works.",
        IOException => $"Couldn't save to the music folder: {ex.Message}",
        UnauthorizedAccessException => "Lucent isn't allowed to save in the music folder. Choose another one.",
        _ => null,
    };

    /// <summary>Anything else: logged for a bug report, and the store window says so, rather than Lucent closing.</summary>
    private static string Unexpected(Exception ex)
    {
        ErrorLog.Write("Bandcamp", ex);
        return $"Something went wrong talking to Bandcamp ({ex.Message}). Try again; if it keeps happening, send a bug report.";
    }

    /// <summary>The stores whose music comes into the library: those importing downloads, and Bandcamp while syncing.</summary>
    private IEnumerable<MusicStore> ActiveStores()
    {
        if (_settings.BandcampImport || (_settings.BandcampSync && IsSignedIn))
        {
            yield return MusicStore.Bandcamp;
        }

        if (_settings.BeatportImport)
        {
            yield return MusicStore.Beatport;
        }
    }

    private StoreImporter NewImporter()
    {
        // Only downloads from the stores switched on are imported; syncing tells the importer it's Bandcamp's itself.
        var importing = new List<MusicStore>();
        if (_settings.BandcampImport)
        {
            importing.Add(MusicStore.Bandcamp);
        }

        if (_settings.BeatportImport)
        {
            importing.Add(MusicStore.Beatport);
        }

        return new StoreImporter(MusicFolder, Path.Combine(RecordFolder, "imported.json"), importing);
    }

    private BandcampSync NewSync() => new(_importer, Path.Combine(RecordFolder, "bandcamp-synced.json"));

    /// <summary>Makes each active store's folder and puts it into the library.</summary>
    private void LinkLibraryFolders()
    {
        foreach (var store in ActiveStores())
        {
            var folder = _importer.FolderFor(store);
            try
            {
                Directory.CreateDirectory(folder);
                _linkFolder(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                SetStatus($"Couldn't make the {store} folder: {ex.Message}");
            }
        }
    }

    private static bool IsCandidate(string path) =>
        string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase) || AudioReaderFactory.IsSupported(path);

    private void OnDownloadChanged(object sender, FileSystemEventArgs e)
    {
        if (!IsCandidate(e.FullPath))
        {
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            _pending[e.FullPath] = 0;
            _pendingTimer.Start();
        });
    }

    /// <summary>Offers each waiting download to the importer, keeping the ones that aren't finished yet for later.</summary>
    private async Task ImportPendingAsync()
    {
        if (!await _importing.WaitAsync(0))
        {
            return;
        }

        try
        {
            var importer = _importer;
            foreach (var (path, attempts) in _pending.ToList())
            {
                StoreImportResult result;
                try
                {
                    result = await Task.Run(() => importer.Import(path));
                }
                catch (Exception ex)
                {
                    // One download Lucent can't handle mustn't take the player down; it's left where it is.
                    ErrorLog.Write("Store import", ex);
                    _pending.Remove(path);
                    SetStatus($"Couldn't import {Path.GetFileName(path)}: {ex.Message}");
                    continue;
                }

                if (result.Outcome == StoreImportOutcome.NotReady && attempts + 1 < MaxAttempts && File.Exists(path))
                {
                    _pending[path] = attempts + 1;
                    continue;
                }

                _pending.Remove(path);
                if (result.Outcome == StoreImportOutcome.Imported)
                {
                    LinkLibraryFolders();
                    SetStatus($"Added {result.Artist} - {result.Title} to your library.");
                }
            }
        }
        finally
        {
            _importing.Release();
            if (_pending.Count == 0)
            {
                _pendingTimer.Stop();
            }
        }
    }

    private void SetStatus(string status)
    {
        Status = status;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>The user's Downloads folder, wherever they've moved it.</summary>
    private static string DownloadsFolder()
    {
        var downloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(downloads, 0, IntPtr.Zero, out var path) == 0)
        {
            try
            {
                return Marshal.PtrToStringUni(path)!;
            }
            finally
            {
                Marshal.FreeCoTaskMem(path);
            }
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
