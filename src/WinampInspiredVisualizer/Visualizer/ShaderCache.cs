using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Vortice.D3DCompiler;
using Vortice.Direct3D;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Compiles HLSL and keeps the results in %LocalAppData%\Lucid\ShaderCache, so each visual
/// is only compiled the first time it's used (or after it changes).
/// </summary>
public sealed class ShaderCache
{
    // Bump when the compiler settings change, so old results aren't reused.
    private const string Version = "fxc-o3-1";

    private readonly string _folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lucid", "ShaderCache");

    private readonly ConcurrentDictionary<string, bool> _used = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns compiled bytecode, from the cache when possible. Throws with the compiler's message on errors.</summary>
    public byte[] GetOrCompile(string source, string entryPoint, string sourceName, string profile, bool useCache = true)
    {
        var file = Path.Combine(_folder, Key(source, entryPoint, profile) + ".cso");
        _used[Path.GetFileName(file)] = true;
        if (useCache)
        {
            try
            {
                if (File.Exists(file))
                {
                    return File.ReadAllBytes(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable cache entry: just compile again.
            }
        }

        var bytecode = Compile(source, entryPoint, sourceName, profile);
        try
        {
            Directory.CreateDirectory(_folder);
            var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytecode);
            File.Move(temporary, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The cache is only a speed-up; carry on without it.
        }

        return bytecode;
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

    private static string Key(string source, string entryPoint, string profile)
    {
        var bytes = Encoding.UTF8.GetBytes($"{Version}\n{profile}\n{entryPoint}\n{source}");
        return Convert.ToHexString(SHA256.HashData(bytes))[..32];
    }

    private static byte[] Compile(string source, string entryPoint, string sourceName, string profile)
    {
        var result = Compiler.Compile(
            source, null!, null!, entryPoint, sourceName, profile, ShaderFlags.OptimizationLevel3,
            out Blob blob, out Blob errors);
        try
        {
            if (result.Failure || blob is null)
            {
                throw new InvalidOperationException(errors?.AsString()?.Trim() ?? $"Shader compile failed ({result.Code:X8}).");
            }

            return blob.AsBytes();
        }
        finally
        {
            blob?.Dispose();
            errors?.Dispose();
        }
    }
}
