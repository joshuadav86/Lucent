using Lucent.Mac.Visuals;
using Lucent.MetalShaders;
using Lucent.Visualizer;
using Lucent.Visualizer.MilkDrop;

// Usage: MetalShaders <app folder with Shaders/ and Presets/> <output folder>
// Writes <output>/Engine.<entry>.metal, <output>/Particles.PSSprite.metal, <output>/AvsDisplay.PSMain.metal and
// <output>/Presets/<preset file name>.<entry>.metal, and fails if any shader doesn't translate.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: MetalShaders <app folder> <output folder>");
    return 2;
}

var appFolder = args[0];
var output = args[1];
string Read(string name) => File.ReadAllText(Path.Combine(appFolder, "Shaders", name));
var common = Read("Common.hlsli");
var particles = Read("Particles.hlsli");
var presetMain = Read("PresetMain.hlsli");
var particleMain = Read("ParticleMain.hlsli");
var engine = MetalTranslation.EngineSource(common, Read("Engine.hlsl"));

Directory.CreateDirectory(Path.Combine(output, "Presets"));
var jobs = new List<(string Name, string Source, string Entry, string OutFile)>
{
    ("Engine.hlsl", engine, "VSMain", "Engine.VSMain.metal"),
    ("Engine.hlsl", engine, "PSPresent", "Engine.PSPresent.metal"),
    ("Engine.hlsl", engine, "PSBlend", "Engine.PSBlend.metal"),
    ("Engine.hlsl", engine, "PSDownsample", "Engine.PSDownsample.metal"),
    ("Particles.hlsli", MetalTranslation.SpriteSource(common, particles), "PSSprite", "Particles.PSSprite.metal"),
};

foreach (var path in Directory.EnumerateFiles(Path.Combine(appFolder, "Presets"), "*.hlsl").Order())
{
    // Same assembly as the Windows renderer (Renderer.CompileLoop) and the build's fxc check.
    var preset = PresetSource.Load(path, File.ReadAllText(path));
    var fileName = Path.GetFileName(path);
    foreach (var entry in preset.IsParticles ? new[] { "PSMain", "CSMain", "VSParticle" } : new[] { "PSMain" })
    {
        var code = MetalTranslation.PresetSource(common, particles, presetMain, particleMain, preset.Code, preset.IsParticles, entry);
        jobs.Add((fileName, code, entry, $"Presets/{fileName}.{entry}.metal"));
    }
}

// AVS presets draw on the CPU; one shader shows all of their pictures.
var avsDisplay = Read("AvsDisplay.hlsl");
jobs.Add(("AvsDisplay.hlsl", MetalTranslation.PresetSource(common, particles, presetMain, particleMain, avsDisplay, false, "PSMain"),
    "PSMain", "AvsDisplay.PSMain.metal"));

// MilkDrop presets: the engine's own passes, then the warp and composite shaders of the presets that
// come with the app, made into HLSL as the app does (MilkShaderTranslator). Presets of the user's own are
// translated as the app runs.
var milkDropPrelude = Read("MilkDrop.hlsli");
var milkDropEngine = MetalTranslation.MilkDropEngineSource(milkDropPrelude, Read("MilkDrop.hlsl"));
foreach (var entry in MetalTranslation.MilkDropEngineEntryPoints)
{
    jobs.Add(("MilkDrop.hlsl", milkDropEngine, entry, $"MilkDrop.{entry}.metal"));
}

var milkDropPresets = Path.Combine(appFolder, "Presets", "MilkDrop");
if (Directory.Exists(milkDropPresets))
{
    Directory.CreateDirectory(Path.Combine(output, "Presets", "MilkDrop"));
    foreach (var path in Directory.EnumerateFiles(milkDropPresets, "*.milk").Order())
    {
        var fileName = Path.GetFileName(path);
        var preset = MilkPreset.Parse(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        foreach (var (wanted, code, kind) in new[]
        {
            (preset.HasWarpShader, preset.WarpShader, MilkShaderKind.Warp),
            (preset.HasCompShader, preset.CompShader, MilkShaderKind.Composite),
        })
        {
            if (wanted)
            {
                var shader = MilkShaderTranslator.Translate(milkDropPrelude, code, kind);
                jobs.Add((fileName, MetalTranslation.MilkDropPresetSource(shader.Source), shader.EntryPoint,
                    $"Presets/MilkDrop/{fileName}.{shader.EntryPoint}.metal"));
            }
        }
    }
}

// Load SPIRV-Cross up front, so if it can't load that fails once, with the whole error.
MetalTranslation.LoadSpirvCross();

var failures = 0;
var translated = jobs.AsParallel().AsOrdered().Select(job =>
{
    try
    {
        return (job, Metal: ShaderTranslator.ToMetal(job.Source, job.Entry, job.Name), Error: (string?)null);
    }
    catch (Exception ex)
    {
        return (job, Metal: (string?)null, Error: ex.GetBaseException().Message);
    }
}).ToList();

foreach (var (job, metal, error) in translated)
{
    if (metal is null)
    {
        failures++;
        Console.Error.WriteLine($"::error::{job.Name} ({job.Entry}) didn't translate to Metal: {error}");
        continue;
    }

    File.WriteAllText(Path.Combine(output, job.OutFile), metal);
}

Console.WriteLine($"Translated {jobs.Count - failures} of {jobs.Count} shaders to Metal in {output}.");
return failures == 0 ? 0 : 1;
