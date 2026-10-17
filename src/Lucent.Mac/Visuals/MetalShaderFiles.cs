using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Finds the visuals and their Metal shaders next to the app. The shaders are translated from the
/// Windows app's HLSL when the Mac app is built (tools/MetalShaders), one file per entry point:
/// Shaders/Metal/Engine.VSMain.metal, ... and Shaders/Metal/Presets/LavaLamp.hlsl.PSMain.metal.
/// </summary>
public sealed class MetalShaderFiles
{
    public MetalShaderFiles(string? appFolder = null)
    {
        var root = appFolder ?? AppContext.BaseDirectory;
        PresetsFolder = Path.Combine(root, "Presets");
        MetalFolder = Path.Combine(root, "Shaders", "Metal");
    }

    public string PresetsFolder { get; }

    public string MetalFolder { get; }

    /// <summary>Every visual, by name, like the Windows app's list.</summary>
    public IReadOnlyList<PresetSource> LoadPresets() =>
        Directory.Exists(PresetsFolder)
            ? Directory.EnumerateFiles(PresetsFolder, "*.hlsl")
                .Select(path => PresetSource.Load(path, File.ReadAllText(path)))
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : Array.Empty<PresetSource>();

    /// <summary>The Metal source for one of the engine's own entry points (VSMain, PSPresent, ...).</summary>
    public string Engine(string entryPoint) =>
        Read(Path.Combine(MetalFolder, entryPoint == "PSSprite" ? "Particles.PSSprite.metal" : $"Engine.{entryPoint}.metal"));

    /// <summary>The Metal source for one of a visual's entry points (PSMain, CSMain, VSParticle).</summary>
    public string Preset(PresetSource preset, string entryPoint) =>
        Read(Path.Combine(MetalFolder, "Presets", $"{Path.GetFileName(preset.FilePath)}.{entryPoint}.metal"));

    private static string Read(string path) =>
        File.Exists(path)
            ? File.ReadAllText(path)
            : throw new FileNotFoundException($"{Path.GetFileName(path)} is missing: the Metal shaders weren't built with the app.", path);
}
