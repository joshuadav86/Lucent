using System.Diagnostics;
using Lucent.Mac.Visuals;

namespace Lucent.MetalShaders;

/// <summary>
/// Turns one HLSL entry point into Metal Shading Language with the Mac app's own rules (see
/// <see cref="MetalTranslation"/>, shared with the app), running DXC's command line tool for the
/// first half of the translation.
/// </summary>
public static class ShaderTranslator
{
    /// <summary>The DXC command line compiler: $DXC, or dxc on the PATH.</summary>
    public static string Dxc { get; set; } = Environment.GetEnvironmentVariable("DXC") is { Length: > 0 } dxc ? dxc : "dxc";

    /// <summary>Translates assembled HLSL and marks the result with the hash of what it came from.</summary>
    public static string ToMetal(string hlsl, string entryPoint, string sourceName)
    {
        var spirv = ToSpirv(hlsl, entryPoint, sourceName);
        var metal = MetalTranslation.SpirvToMetal(spirv, entryPoint);
        return MetalTranslation.WithSourceHash(metal, MetalTranslation.SourceHash(hlsl, entryPoint));
    }

    private static byte[] ToSpirv(string hlsl, string entryPoint, string sourceName)
    {
        var folder = Directory.CreateTempSubdirectory("lucent-metal-");
        try
        {
            var input = Path.Combine(folder.FullName, sourceName);
            var output = Path.Combine(folder.FullName, "shader.spv");
            File.WriteAllText(input, hlsl);
            var start = new ProcessStartInfo(Dxc)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in MetalTranslation.DxcArguments(entryPoint).Concat(new[] { "-Fo", output, input }))
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(errors.Result.Trim());
            }

            return File.ReadAllBytes(output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
