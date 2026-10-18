using Avalonia.Threading;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;

namespace Lucent.Mac.Library;

/// <summary>
/// Every music file inside the user's library folders. Scans in the background, reads tags
/// afterwards, and rescans by itself when files are added, removed or renamed.
/// </summary>
public sealed class MusicLibrary : IDisposable
{
    private const int TagBatchSize = 100;

    private readonly Dictionary<string, LibraryItem> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly DispatcherTimer _rescanDelay;
    private CancellationTokenSource? _scan;
    private IReadOnlyList<string> _folders = Array.Empty<string>();
    private IReadOnlyList<string> _hidden = Array.Empty<string>();

    public MusicLibrary()
    {
        // File copies arrive as bursts of events; wait for them to settle before rescanning.
        _rescanDelay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _rescanDelay.Tick += async (_, _) =>
        {
            _rescanDelay.Stop();
            await RescanAsync();
        };
    }

    /// <summary>Raised on the UI thread when files have been added or removed.</summary>
    public event EventHandler? ItemsChanged;

    /// <summary>Raised on the UI thread when every file's tags have been read.</summary>
    public event EventHandler? TagsLoaded;

    /// <summary>Raised on the UI thread when a scan starts or finishes.</summary>
    public event EventHandler? ScanningChanged;

    public IReadOnlyList<LibraryItem> Items { get; private set; } = Array.Empty<LibraryItem>();

    public IReadOnlyList<string> Folders => _folders;

    public bool IsScanning { get; private set; }

    /// <summary>Lists the music in these folders, leaving out what's in the hidden folders inside them.</summary>
    public void SetFolders(IEnumerable<string> folders, IEnumerable<string> hidden)
    {
        _folders = folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _hidden = hidden
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .ToList();
        WatchFolders();
        _ = RescanAsync();
    }

    public async Task RescanAsync()
    {
        _scan?.Cancel();
        var scan = new CancellationTokenSource();
        _scan = scan;
        SetScanning(true);

        try
        {
            var folders = _folders;
            var hidden = _hidden;
            var found = await Task.Run(() => FindFiles(folders, hidden, scan.Token), scan.Token);
            if (scan.IsCancellationRequested)
            {
                return;
            }

            var items = new List<LibraryItem>(found.Count);
            foreach (var file in found)
            {
                // Keep items (and their tags) for files that haven't changed since the last scan.
                if (!_byPath.TryGetValue(file.Path, out var item) || item.Added != file.Added || item.Folder != file.Folder
                    || item.Directory != file.Directory)
                {
                    item = new LibraryItem(file.Path, file.Folder, file.Directory, file.Added);
                    _byPath[file.Path] = item;
                }

                items.Add(item);
            }

            var present = new HashSet<string>(found.Select(file => file.Path), StringComparer.OrdinalIgnoreCase);
            foreach (var gone in _byPath.Keys.Where(path => !present.Contains(path)).ToList())
            {
                _byPath.Remove(gone);
            }

            Items = items;
            ItemsChanged?.Invoke(this, EventArgs.Empty);

            var untagged = items.Where(item => !item.HasTags).ToList();
            await Task.Run(() => ReadTags(untagged, scan.Token), scan.Token);
            if (!scan.IsCancellationRequested)
            {
                TagsLoaded?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            // A newer scan took over.
        }
        finally
        {
            if (_scan == scan)
            {
                SetScanning(false);
            }
        }
    }

    /// <summary>
    /// Reads these files' tags again and updates their rows, after their info was edited. Files that keep
    /// their dates aren't picked up as changed by a rescan, so this is what shows the new info.
    /// </summary>
    public async Task RereadTagsAsync(IEnumerable<string> paths)
    {
        var items = paths
            .Select(path => _byPath.TryGetValue(path, out var item) ? item : null)
            .OfType<LibraryItem>()
            .Distinct()
            .ToList();
        if (items.Count == 0)
        {
            return;
        }

        var tracks = await Task.Run(() => items.Select(item => Track.FromFile(item.Path)).ToList());
        for (var i = 0; i < items.Count; i++)
        {
            items[i].ApplyTags(tracks[i]);
        }

        // Lets the list re-sort when it's in title or artist order.
        TagsLoaded?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _scan?.Cancel();
        _rescanDelay.Stop();
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void SetScanning(bool scanning)
    {
        if (IsScanning != scanning)
        {
            IsScanning = scanning;
            ScanningChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ReadTags(List<LibraryItem> items, CancellationToken token)
    {
        for (var start = 0; start < items.Count; start += TagBatchSize)
        {
            token.ThrowIfCancellationRequested();
            var batch = items.Skip(start).Take(TagBatchSize).Select(item => (item, Track.FromFile(item.Path))).ToList();
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var (item, track) in batch)
                {
                    item.ApplyTags(track);
                }
            }, DispatcherPriority.Background);
        }
    }

    private static List<FoundFile> FindFiles(IReadOnlyList<string> folders, IReadOnlyList<string> hidden, CancellationToken token)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };

        var results = new Dictionary<string, FoundFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in folders)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var hiddenHere = LibraryFolderList.HiddenIn(root, hidden);
            try
            {
                foreach (var path in Directory.EnumerateFiles(root, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (!AudioReaderFactory.IsSupported(path) || results.ContainsKey(path)
                        || LibraryFolderList.IsInsideAny(path, hiddenHere))
                    {
                        continue;
                    }

                    DateTime added;
                    try
                    {
                        var info = new FileInfo(path);
                        added = info.CreationTime > info.LastWriteTime ? info.CreationTime : info.LastWriteTime;
                    }
                    catch (Exception)
                    {
                        added = DateTime.MinValue;
                    }

                    var directory = System.IO.Path.GetDirectoryName(path) ?? root;
                    results[path] = new FoundFile(path, FolderLabel(root, directory), directory, added);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A drive that disappeared mid-scan; list what was found.
            }
        }

        return results.Values.ToList();
    }

    /// <summary>"Music", or "Music / Albums / Some Album" for files in subfolders.</summary>
    private static string FolderLabel(string root, string directory)
    {
        var rootName = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(root));
        if (string.IsNullOrEmpty(rootName))
        {
            rootName = root; // a whole disk, like /
        }

        var relative = System.IO.Path.GetRelativePath(root, directory);
        return relative == "."
            ? rootName
            : rootName + " / " + relative.Replace(System.IO.Path.DirectorySeparatorChar.ToString(), " / ");
    }

    private void WatchFolders()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        foreach (var folder in _folders.Where(Directory.Exists))
        {
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    InternalBufferSize = 64 * 1024,
                };
                watcher.Created += OnFolderChanged;
                watcher.Changed += OnFolderChanged;
                watcher.Deleted += OnFolderChanged;
                watcher.Renamed += OnFolderChanged;
                watcher.Error += (_, _) => ScheduleRescan();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception)
            {
                // Some network drives can't be watched; the refresh button still works for them.
            }
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRescan();

    private void ScheduleRescan() => Dispatcher.UIThread.Post(() =>
    {
        _rescanDelay.Stop();
        _rescanDelay.Start();
    });

    /// <summary>
    /// Shows these items as the library without looking on disk, for the screenshots (Lucent --screenshots).
    /// </summary>
    internal void ShowPreview(IEnumerable<string> folders, IReadOnlyList<LibraryItem> items)
    {
        _folders = folders.ToList();
        foreach (var item in items)
        {
            _byPath[item.Path] = item;
        }

        Items = items;
        ItemsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record FoundFile(string Path, string Folder, string Directory, DateTime Added);
}
