using System.Collections.Concurrent;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Translates HLSL to Metal as the app runs, and keeps the results in ~/Library/Caches/Lucent/ShaderCache,
/// so each of the user's own visuals is only translated the first time it's used (or after it changes).
/// The Mac's version of the Windows app's ShaderCache, which keeps compiled HLSL the same way.
/// </summary>
public sealed class ShaderCache
{
    private readonly string _folder;
    private readonly ConcurrentDictionary<string, bool> _used = new(StringComparer.OrdinalIgnoreCase);

    public ShaderCache(string? folder = null)
    {
        _folder = folder ?? DefaultFolder;
    }

    /// <summary>~/Library/Caches/Lucent/ShaderCache: macOS may empty it when space runs short, which only costs a translation.</summary>
    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Caches", "Lucent", "ShaderCache");

    /// <summary>Returns the Metal for assembled HLSL, from the cache when possible. Throws with the compiler's message on errors.</summary>
    public string GetOrTranslate(string hlsl, string entryPoint, string sourceName)
    {
        var hash = MetalTranslation.SourceHash(hlsl, entryPoint);
        var file = Path.Combine(_folder, hash + ".metal");
        _used[Path.GetFileName(file)] = true;
        try
        {
            if (File.Exists(file))
            {
                var cached = File.ReadAllText(file);
                if (MetalTranslation.ReadSourceHash(cached) == hash)
                {
                    return cached;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable cache entry: just translate again.
        }

        var spirv = DxcCompiler.ToSpirv(hlsl, entryPoint, sourceName);
        var metal = MetalTranslation.WithSourceHash(MetalTranslation.SpirvToMetal(spirv, entryPoint), hash);
        try
        {
            Directory.CreateDirectory(_folder);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, metal);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is only a speed-up; carry on without it.
        }

        return metal;
    }

    /// <summary>Deletes results nothing asked for this session (visuals that were edited or removed).</summary>
    public void RemoveUnused()
    {
        try
        {
            if (!Directory.Exists(_folder))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_folder))
            {
                if (!_used.ContainsKey(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tidying up can wait for another day.
        }
    }
}
