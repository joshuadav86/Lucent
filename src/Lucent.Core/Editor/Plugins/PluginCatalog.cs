using System.IO;
using System.Text.Json;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Editor.Plugins;

/// <summary>An effect the editor can use: which .vst3 file it's in, and which effect in that file.</summary>
/// <remarks><see cref="Categories"/> is the plugin's own VST3 subcategories, such as "Fx|Dynamics"; <see cref="PluginBrowser"/> turns them into a type.</remarks>
public sealed record PluginInfo(string Path, string ClassId, string Name, string Vendor, string Version, string Categories);

/// <summary>
/// The VST3 effects installed on this computer. Plugin files are looked inside once and remembered in
/// %AppData%\Lucent\Plugins\plugins.json, so the list is quick next time; only new or changed files are
/// looked at again. The settings last used with each effect are kept beside it.
/// </summary>
public sealed class PluginCatalog
{
    private const int ParallelScans = 4;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private CatalogFile _file = new();

    public static string Folder => Path.Combine(AppDataFolder.Path, "Plugins");

    private static string FilePath => Path.Combine(Folder, "plugins.json");

    private static string SettingsFolder => Path.Combine(Folder, "Settings");

    /// <summary>The standard places VST3 plugins are installed.</summary>
    public static IReadOnlyList<string> StandardFolders
    {
        get
        {
            if (OperatingSystem.IsWindows())
            {
                return new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3"),
                };
            }

            if (OperatingSystem.IsMacOS())
            {
                return new[]
                {
                    "/Library/Audio/Plug-Ins/VST3",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Audio/Plug-Ins/VST3"),
                };
            }

            return new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".vst3"),
                "/usr/lib/vst3",
                "/usr/local/lib/vst3",
            };
        }
    }

    /// <summary>Folders the user added, besides the standard ones.</summary>
    public IReadOnlyList<string> ExtraFolders => _file.ExtraFolders;

    /// <summary>Every effect found, by name.</summary>
    public IReadOnlyList<PluginInfo> Plugins =>
        _file.Files.SelectMany(file => file.Effects.Select(e => new PluginInfo(file.Path, e.ClassId, e.Name, e.Vendor, e.Version, e.Categories)))
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Vendor, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Plugin files that couldn't be loaded, and why.</summary>
    public IReadOnlyList<(string Path, string Error)> Failures =>
        _file.Files.Where(f => f.Error is not null).Select(f => (f.Path, f.Error!)).ToList();

    /// <summary>Reads the remembered list (empty if there isn't one yet or it can't be read).</summary>
    public static PluginCatalog Load()
    {
        var catalog = new PluginCatalog();
        try
        {
            if (File.Exists(FilePath))
            {
                catalog._file = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(FilePath), JsonOptions) ?? new CatalogFile();
            }
        }
        catch (Exception)
        {
            // A damaged list is rebuilt by looking again.
        }

        return catalog;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_file, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // Not worth bothering the user about; the plugins are looked for again next time.
        }
    }

    public void AddFolder(string folder)
    {
        if (!_file.ExtraFolders.Concat(StandardFolders).Any(f => SamePath(f, folder)))
        {
            _file.ExtraFolders.Add(folder);
        }
    }

    /// <summary>The plugin files in the plugin folders that are new or changed since they were last looked at.</summary>
    public IReadOnlyList<string> FindUnscanned() =>
        FindPluginFiles().Where(path => Known(path) is not { } known || known.Modified != Modified(path)).ToList();

    /// <summary>
    /// Looks inside plugin files, a few at a time, and remembers what they hold. With
    /// <paramref name="everything"/>, looks at every file again (including ones that failed before);
    /// otherwise only at new and changed ones. Files that have been removed are forgotten.
    /// </summary>
    public async Task ScanAsync(bool everything, IProgress<double>? progress, CancellationToken cancellation)
    {
        var present = FindPluginFiles();
        _file.Files.RemoveAll(known => !present.Any(path => SamePath(path, known.Path)));
        var toScan = everything ? present : FindUnscanned();
        var done = 0;
        using var gate = new SemaphoreSlim(ParallelScans);
        var scans = toScan.Select(async path =>
        {
            await gate.WaitAsync(cancellation);
            try
            {
                var result = await PluginHost.ScanAsync(path, cancellation);
                return (Path: path, Result: result);
            }
            finally
            {
                gate.Release();
                progress?.Report((double)Interlocked.Increment(ref done) / toScan.Count);
            }
        }).ToList();

        foreach (var (path, result) in await Task.WhenAll(scans))
        {
            _file.Files.RemoveAll(known => SamePath(known.Path, path));
            _file.Files.Add(new CatalogEntry
            {
                Path = path,
                Modified = Modified(path),
                Error = result.Error,
                Effects = result.Effects.Select(e => new CatalogEffect
                {
                    ClassId = e.ClassId,
                    Name = e.Name,
                    Vendor = e.Vendor,
                    Version = e.Version,
                    Categories = e.Categories,
                }).ToList(),
            });
        }

        Save();
    }

    // ---- Settings last used with each effect ----

    public static byte[]? LoadSettings(PluginInfo plugin)
    {
        try
        {
            var path = SettingsPath(plugin);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void SaveSettings(PluginInfo plugin, byte[] settings)
    {
        try
        {
            Directory.CreateDirectory(SettingsFolder);
            File.WriteAllBytes(SettingsPath(plugin), settings);
        }
        catch (Exception)
        {
            // The effect just starts with its own defaults next time.
        }
    }

    private static string SettingsPath(PluginInfo plugin) => Path.Combine(SettingsFolder, $"{plugin.ClassId}.state");

    // ---- Finding plugin files ----

    private List<string> FindPluginFiles()
    {
        var found = new List<string>();
        foreach (var folder in StandardFolders.Concat(_file.ExtraFolders))
        {
            Collect(folder, found, depth: 0);
        }

        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Adds the .vst3 plugins in a folder and the folders inside it. A plugin is either a .vst3 file or,
    /// more often now, a .vst3 folder (a "bundle") whose insides aren't searched.
    /// </summary>
    private static void Collect(string folder, List<string> found, int depth)
    {
        if (depth > 6 || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                if (entry.EndsWith(".vst3", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(entry);
                }
                else if (Directory.Exists(entry))
                {
                    Collect(entry, found, depth + 1);
                }
            }
        }
        catch (Exception)
        {
            // A folder Lucent can't read is skipped.
        }
    }

    private CatalogEntry? Known(string path) => _file.Files.FirstOrDefault(f => SamePath(f.Path, path));

    /// <summary>When a plugin was last changed: for a bundle, its newest file, since installers update the insides.</summary>
    private static long Modified(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return File.GetLastWriteTimeUtc(path).Ticks;
            }

            return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => File.GetLastWriteTimeUtc(file).Ticks)
                .DefaultIfEmpty(Directory.GetLastWriteTimeUtc(path).Ticks)
                .Max();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed class CatalogFile
    {
        public List<string> ExtraFolders { get; set; } = new();

        public List<CatalogEntry> Files { get; set; } = new();
    }

    private sealed class CatalogEntry
    {
        public string Path { get; set; } = string.Empty;

        public long Modified { get; set; }

        public string? Error { get; set; }

        public List<CatalogEffect> Effects { get; set; } = new();
    }

    private sealed class CatalogEffect
    {
        public string ClassId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Vendor { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public string Categories { get; set; } = string.Empty;
    }
}
