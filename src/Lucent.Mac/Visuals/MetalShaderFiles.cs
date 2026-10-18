using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Finds the visuals and their Metal shaders, like the Windows app's PresetLibrary. The built-in visuals'
/// shaders are translated from the Windows app's HLSL when the Mac app is built (tools/MetalShaders), one
/// file per entry point: Shaders/Metal/Engine.VSMain.metal, ... and Shaders/Metal/Presets/LavaLamp.hlsl.PSMain.metal.
/// The user's own visuals, in ~/Library/Application Support/Lucent/Presets, load alongside them (one with the
/// same file name replaces a built-in one) and are translated as the app runs, as are built-in visuals whose
/// HLSL was edited after the app was built (see <see cref="ShaderCache"/>).
/// </summary>
public sealed class MetalShaderFiles
{
    private readonly ShaderCache _cache;
    private readonly Lazy<EngineSources?> _sources;

    public MetalShaderFiles(string? appFolder = null, string? userPresetsFolder = null, ShaderCache? cache = null)
    {
        var root = appFolder ?? AppContext.BaseDirectory;
        PresetsFolder = Path.Combine(root, "Presets");
        ShadersFolder = Path.Combine(root, "Shaders");
        MetalFolder = Path.Combine(root, "Shaders", "Metal");
        UserPresetsFolder = userPresetsFolder ?? Path.Combine(AppDataFolder.Path, "Presets");
        _cache = cache ?? new ShaderCache();
        _sources = new Lazy<EngineSources?>(ReadEngineSources);
    }

    /// <summary>The built-in visuals' HLSL, beside the app.</summary>
    public string PresetsFolder { get; }

    /// <summary>The engine's own HLSL (Common.hlsli, Engine.hlsl...), beside the app.</summary>
    public string ShadersFolder { get; }

    public string MetalFolder { get; }

    /// <summary>
    /// The user's own visuals: ~/Library/Application Support/Lucent/Presets. Unlike the app's folder,
    /// they survive updates.
    /// </summary>
    public string UserPresetsFolder { get; }

    /// <summary>
    /// Where the user's MilkDrop presets go, ~/Library/Application Support/Lucent/Presets/MilkDrop ("Import MilkDrop
    /// presets…" copies them here). .milk files anywhere under either Presets folder load too, as on Windows.
    /// </summary>
    public string MilkDropFolder => Path.Combine(UserPresetsFolder, "MilkDrop");

    /// <summary>Every visual, built-in and the user's own, by name, like the Windows app's list.</summary>
    public IReadOnlyList<PresetSource> LoadPresets()
    {
        var files = Read(PresetsFolder);
        var own = Read(UserPresetsFolder);
        if (own.Count > 0)
        {
            var ownNames = own.Select(file => Path.GetFileName(file.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            files = files.Where(file => !ownNames.Contains(Path.GetFileName(file.Path))).Concat(own).ToList();
        }

        return PresetSource.WithUniqueNames(files
            .Select(file => PresetSource.Load(file.Path, file.Code))
            .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(preset => preset.Author, StringComparer.OrdinalIgnoreCase)
            .ToList());
    }

    /// <summary>The .hlsl visuals in a folder, and the MilkDrop presets in it and in the folders inside it.</summary>
    private static List<(string Path, string Code)> Read(string folder)
    {
        var files = new List<(string Path, string Code)>();
        try
        {
            if (Directory.Exists(folder))
            {
                files.AddRange(Directory.EnumerateFiles(folder, "*.hlsl").Select(path => (Path: path, Code: File.ReadAllText(path))));
                files.AddRange(Directory.EnumerateFiles(folder, "*.milk", SearchOption.AllDirectories)
                    .Select(path => (Path: path, Code: File.ReadAllText(path))));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A preset pack that can't be read: the visuals that could be still load.
            ErrorLog.Write("Visuals", ex);
        }

        return files;
    }

    /// <summary>True if the visual came with the app (rather than from the user's own folder).</summary>
    public bool IsBuiltIn(PresetSource preset) =>
        !string.Equals(
            Path.GetFullPath(Path.GetDirectoryName(preset.FilePath) ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(UserPresetsFolder).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>The Metal source for one of the engine's own entry points (VSMain, PSPresent, ...).</summary>
    public string Engine(string entryPoint)
    {
        var shipped = Path.Combine(MetalFolder, entryPoint == "PSSprite" ? "Particles.PSSprite.metal" : $"Engine.{entryPoint}.metal");
        var sources = _sources.Value;
        var hlsl = sources is null ? null
            : entryPoint == "PSSprite" ? MetalTranslation.SpriteSource(sources.Common, sources.Particles)
            : MetalTranslation.EngineSource(sources.Common, sources.Engine);
        return Find(shipped, hlsl, entryPoint, entryPoint == "PSSprite" ? "Particles.hlsli" : "Engine.hlsl");
    }

    /// <summary>The Metal source for one of a visual's entry points (PSMain, CSMain, VSParticle).</summary>
    public string Preset(PresetSource preset, string entryPoint)
    {
        var fileName = Path.GetFileName(preset.FilePath);
        var shipped = Path.Combine(MetalFolder, "Presets", $"{fileName}.{entryPoint}.metal");
        var sources = _sources.Value;
        var hlsl = sources is null ? null
            : MetalTranslation.PresetSource(sources.Common, sources.Particles, sources.PresetMain, sources.ParticleMain,
                preset.Code, preset.IsParticles, entryPoint);

        // A visual of the user's own never uses a shipped translation, even one with the same file name.
        return Find(IsBuiltIn(preset) ? shipped : null, hlsl, entryPoint, fileName);
    }

    /// <summary>MilkDrop.hlsli: what MilkDrop presets' shaders are given, put in front of each of them.</summary>
    public string MilkDropPrelude() =>
        _sources.Value?.MilkDropPrelude ?? throw new FileNotFoundException("MilkDrop.hlsli is missing: the visuals' shaders weren't included with the app.");

    /// <summary>The Metal source for one of the MilkDrop engine's entry points (VSWarp, PSBlurAcross, ...).</summary>
    public string MilkDropEngine(string entryPoint)
    {
        var shipped = Path.Combine(MetalFolder, $"MilkDrop.{entryPoint}.metal");
        var sources = _sources.Value;
        var hlsl = sources is null ? null : MetalTranslation.MilkDropEngineSource(sources.MilkDropPrelude, sources.MilkDropEngine);
        return Find(shipped, hlsl, entryPoint, "MilkDrop.hlsl");
    }

    /// <summary>
    /// The Metal source for a MilkDrop preset's warp or composite shader, already made into HLSL by
    /// MilkShaderTranslator: the build's translation for a preset that came with the app, otherwise one made now.
    /// </summary>
    public string MilkDropPreset(PresetSource preset, MilkTranslatedShader shader)
    {
        var fileName = Path.GetFileName(preset.FilePath);
        var shipped = Path.Combine(MetalFolder, "Presets", "MilkDrop", $"{fileName}.{shader.EntryPoint}.metal");
        return Find(IsBuiltIn(preset) ? shipped : null, MetalTranslation.MilkDropPresetSource(shader.Source), shader.EntryPoint, fileName);
    }

    /// <summary>Deletes translations nothing asked for this session (visuals that were edited or removed).</summary>
    public void RemoveUnusedTranslations() => _cache.RemoveUnused();

    /// <summary>
    /// The shipped translation when it was made from this very HLSL; otherwise a translation made now
    /// (or earlier, from the cache).
    /// </summary>
    private string Find(string? shipped, string? hlsl, string entryPoint, string sourceName)
    {
        if (shipped is not null && File.Exists(shipped))
        {
            var metal = File.ReadAllText(shipped);
            var madeFrom = MetalTranslation.ReadSourceHash(metal);
            // Without the HLSL to compare against (or a hash to compare with), the shipped file is all there is.
            if (hlsl is null || madeFrom is null || madeFrom == MetalTranslation.SourceHash(hlsl, entryPoint))
            {
                return metal;
            }
        }

        if (hlsl is null)
        {
            throw new FileNotFoundException(
                $"{Path.GetFileName(shipped ?? sourceName)} is missing: the visuals' shaders weren't included with the app.", shipped ?? sourceName);
        }

        return _cache.GetOrTranslate(hlsl, entryPoint, sourceName);
    }

    private EngineSources? ReadEngineSources()
    {
        string? Read(string name)
        {
            var path = Path.Combine(ShadersFolder, name);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        var common = Read("Common.hlsli");
        var engine = Read("Engine.hlsl");
        var particles = Read("Particles.hlsli");
        var presetMain = Read("PresetMain.hlsli");
        var particleMain = Read("ParticleMain.hlsli");
        var milkDropPrelude = Read("MilkDrop.hlsli");
        var milkDropEngine = Read("MilkDrop.hlsl");
        return common is null || engine is null || particles is null || presetMain is null || particleMain is null
            || milkDropPrelude is null || milkDropEngine is null
            ? null
            : new EngineSources(common, engine, particles, presetMain, particleMain, milkDropPrelude, milkDropEngine);
    }

    /// <summary>The shared shader files a visual is assembled from.</summary>
    private sealed record EngineSources(
        string Common, string Engine, string Particles, string PresetMain, string ParticleMain, string MilkDropPrelude, string MilkDropEngine);
}
