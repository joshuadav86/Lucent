using System.IO;
using System.Text.RegularExpressions;
using Lucent.Visualizer.Avs;
using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer;

/// <summary>What importing visuals did.</summary>
/// <param name="Added">How many visuals were copied in.</param>
/// <param name="Skipped">The files that weren't visuals, or couldn't be copied, and why, for the error log.</param>
/// <param name="First">The name of the first visual copied in, to show it straight away.</param>
public sealed record VisualImportResult(int Added, IReadOnlyList<(string File, string Reason)> Skipped, string? First)
{
    /// <summary>The line shown under the visuals once the import is done.</summary>
    public string Message
    {
        get
        {
            var message = Added switch
            {
                0 => "No visuals were found there.",
                1 => "Added 1 visual.",
                _ => $"Added {Added} visuals.",
            };
            var skipped = Skipped.Count;
            if (skipped > 0)
            {
                message += $" {skipped} file{(skipped == 1 ? " wasn't a visual and was" : "s weren't visuals and were")} left out.";
            }

            return message;
        }
    }
}

/// <summary>
/// "Import visuals…" in the visuals' right-click menu and Options › Visuals: copies Lucent shaders (.hlsl),
/// MilkDrop presets (.milk) and AVS presets (.avs) into the user's own Presets folder, each kind where it
/// loads from, so they show in the picker from then on and survive updates. Shared by Windows and Mac.
/// </summary>
/// <remarks>
/// A shader with the same file name as a built-in visual replaces it, the same as an edited copy does
/// (<see cref="ShaderEditing"/>). Each file is read first, so one that isn't a visual is left behind and listed
/// for the error log rather than turning up broken in the list. A shader that doesn't compile still comes in:
/// its error shows when it's picked, and it can be fixed with Advanced › Edit shader.
/// </remarks>
public static partial class VisualImport
{
    /// <summary>The file types the picker offers, without the dot.</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[] { "hlsl", "milk", "avs" };

    [GeneratedRegex(@"\bfloat3\s+Render\s*\(")]
    private static partial Regex RenderPattern();

    /// <summary>Copies the given files into <paramref name="userFolder"/> (the user's Presets folder) and its MilkDrop and AVS folders.</summary>
    public static VisualImportResult ImportFiles(IEnumerable<string> paths, string userFolder, string milkDropFolder, string avsFolder)
    {
        var added = 0;
        var skipped = new List<(string File, string Reason)>();
        string? first = null;
        var milk = new List<string>();
        var avs = new List<string>();
        foreach (var path in paths)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".milk":
                    milk.Add(path);
                    break;
                case ".avs":
                    avs.Add(path);
                    break;
                case ".hlsl":
                    if (ImportShader(path, userFolder, skipped) is { } name)
                    {
                        added++;
                        first ??= name;
                    }

                    break;
                default:
                    skipped.Add((Path.GetFileName(path), "Not a .hlsl, .milk or .avs file"));
                    break;
            }
        }

        if (milk.Count > 0)
        {
            var result = MilkDropImport.ImportFiles(milk, milkDropFolder);
            added += result.Added;
            skipped.AddRange(result.Skipped);
            first ??= result.First;
        }

        if (avs.Count > 0)
        {
            var result = AvsImport.ImportFiles(avs, avsFolder);
            added += result.Added;
            skipped.AddRange(result.Skipped);
            first ??= result.First;
        }

        return new VisualImportResult(added, skipped, first);
    }

    /// <summary>Copies one shader in and returns the name it shows under, or null (with the reason in skipped) if it isn't one.</summary>
    private static string? ImportShader(string source, string userFolder, List<(string File, string Reason)> skipped)
    {
        try
        {
            var code = File.ReadAllText(source);
            if (!RenderPattern().IsMatch(code))
            {
                skipped.Add((Path.GetFileName(source), "No float3 Render(float2 uv, float2 p) function"));
                return null;
            }

            var target = Path.GetFullPath(Path.Combine(userFolder, Path.GetFileName(source)));
            if (!string.Equals(Path.GetFullPath(source), target, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(userFolder);
                File.Copy(source, target, overwrite: true);
            }

            return PresetSource.Load(target, code, own: true).Name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add((Path.GetFileName(source), ex.Message));
            return null;
        }
    }
}
