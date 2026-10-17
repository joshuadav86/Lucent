using Lucent.MetalShaders;
using WinampInspiredVisualizer.Visualizer;

// Usage: MetalShaders <app folder with Shaders/ and Presets/> <output folder>
// Writes <output>/Engine.<entry>.metal, <output>/Particles.PSSprite.metal and
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
var engine = common + "\n" + Read("Engine.hlsl");

Directory.CreateDirectory(Path.Combine(output, "Presets"));
var jobs = new List<(string Name, string Source, string Entry, string OutFile)>
{
    ("Engine.hlsl", engine, "VSMain", "Engine.VSMain.metal"),
    ("Engine.hlsl", engine, "PSPresent", "Engine.PSPresent.metal"),
    ("Engine.hlsl", engine, "PSBlend", "Engine.PSBlend.metal"),
    ("Engine.hlsl", engine, "PSDownsample", "Engine.PSDownsample.metal"),
    ("Particles.hlsli", common + "\n" + particles, "PSSprite", "Particles.PSSprite.metal"),
};

foreach (var path in Directory.EnumerateFiles(Path.Combine(appFolder, "Presets"), "*.hlsl").Order())
{
    // Same assembly as the Windows renderer (Renderer.CompileLoop) and the build's fxc check.
    var preset = PresetSource.Load(path, File.ReadAllText(path));
    var fileName = Path.GetFileName(path);
    var api = preset.IsParticles ? common + "\n" + particles : common;
    var code = api + "\n#line 1\n" + preset.Code + "\n";
    jobs.Add((fileName, code + presetMain, "PSMain", $"Presets/{fileName}.PSMain.metal"));
    if (preset.IsParticles)
    {
        jobs.Add((fileName, code + particleMain, "CSMain", $"Presets/{fileName}.CSMain.metal"));
        jobs.Add((fileName, code + particleMain, "VSParticle", $"Presets/{fileName}.VSParticle.metal"));
    }
}

// Load SPIRV-Cross up front, so if it can't load that fails once, with the whole error.
_ = ShaderTranslator.Dxc;

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
