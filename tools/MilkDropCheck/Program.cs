using System.Globalization;
using Lucent.Visualizer.MilkDrop;

// Usage: MilkDropCheck [preset files or folders...] [--shaders <output folder>]
// With no presets given it checks the ones that ship with Lucent. Exits with 1 if anything fails.
// With --shaders it also writes each preset's warp and composite shaders as HLSL (and the engine's
// own MilkDrop passes), ready for fxc or dxc, e.g. dxc -T ps_6_0 -E PSWarpPreset <file>.
var repository = FindRepository();
var prelude = File.ReadAllText(Path.Combine(repository, "content/Shaders/MilkDrop.hlsli"));
var inputs = new List<string>();
string? shaderFolder = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--shaders" && i + 1 < args.Length)
    {
        shaderFolder = args[++i];
    }
    else
    {
        inputs.Add(args[i]);
    }
}

if (inputs.Count == 0)
{
    inputs.Add(Path.Combine(repository, "content/Presets/MilkDrop"));
}

var failures = CheckExpressions() + CheckWarp();
var files = inputs.SelectMany<string, string>(input => Directory.Exists(input)
    ? Directory.EnumerateFiles(input, "*.milk", SearchOption.AllDirectories).Order()
    : new[] { input }).ToList();

if (shaderFolder is not null)
{
    Directory.CreateDirectory(shaderFolder);
    var engine = File.ReadAllText(Path.Combine(repository, "content/Shaders/MilkDrop.hlsl"));
    File.WriteAllText(Path.Combine(shaderFolder, "MilkDrop.engine.hlsl"), prelude + "\n" + engine);
}

foreach (var file in files)
{
    failures += CheckPreset(file, prelude, shaderFolder);
}

Console.WriteLine(failures == 0 ? $"All good: {files.Count} preset(s)." : $"{failures} problem(s).");
return failures == 0 ? 0 : 1;

static int CheckPreset(string file, string prelude, string? shaderFolder)
{
    var name = Path.GetFileNameWithoutExtension(file);
    try
    {
        var preset = MilkPreset.Parse(File.ReadAllText(file), name);
        var runner = new MilkPresetRunner(preset, new EelGlobals());
        var audio = new MilkAudio();
        const double dt = 1.0 / 60;
        var started = DateTime.UtcNow;
        var vertices = 0;
        for (var frame = 0; frame < 300; frame++)
        {
            // Made-up music: a kick twice a second over a pair of tones.
            var time = frame * dt;
            var kick = Math.Exp(-(time % 0.5) * 12);
            for (var i = 0; i < MilkAudio.WaveformSamples; i++)
            {
                var t = time + i / 44100.0;
                audio.WaveLeft[i] = (float)(0.4 * Math.Sin(t * 2 * Math.PI * 110) * (0.4 + kick) + 0.1 * Math.Sin(t * 2 * Math.PI * 1760));
                audio.WaveRight[i] = (float)(0.4 * Math.Sin(t * 2 * Math.PI * 165) * (0.4 + kick));
            }

            for (var i = 0; i < MilkAudio.SpectrumSamples; i++)
            {
                audio.Spectrum[i] = (float)(Math.Max(0, 1 - i / 300.0) * (0.5 + 0.5 * kick));
            }

            audio.Update((float)(0.5 + 0.3 * kick), 0.45f, (float)(0.35 + 0.05 * Math.Sin(time * 3)), dt);
            runner.Render(new MilkFrameInput(time, dt, time / 25, 1280, 720, 48, 36), audio);
            vertices = Math.Max(vertices, runner.Geometry.VertexCount);
        }

        var milliseconds = (DateTime.UtcNow - started).TotalMilliseconds / 300;
        var problems = 0;
        if (runner.Mesh.Any(v => !float.IsFinite(v)) || runner.Constants.Any(v => !float.IsFinite(v)))
        {
            Console.WriteLine($"FAIL {name}: the mesh or the shader constants have NaN or infinite values.");
            problems++;
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{(problems == 0 ? "ok  " : "FAIL")} {name}: version {preset.Version}, {preset.Waves.Count} wave(s), {preset.Shapes.Count} shape(s), " +
            $"warp shader {(preset.HasWarpShader ? "yes" : "no")}, comp shader {(preset.HasCompShader ? "yes" : "no")}, " +
            $"up to {vertices} vertices, q1 = {runner.Constants[11 * 4]:0.###}, {milliseconds:0.00} ms a frame"));
        foreach (var warning in runner.Warnings)
        {
            Console.WriteLine($"     note: {warning}");
        }

        if (shaderFolder is not null)
        {
            foreach (var (wanted, code, kind) in new[] { (preset.HasWarpShader, preset.WarpShader, MilkShaderKind.Warp), (preset.HasCompShader, preset.CompShader, MilkShaderKind.Composite) })
            {
                if (wanted)
                {
                    var shader = MilkShaderTranslator.Translate(prelude, code, kind);
                    File.WriteAllText(Path.Combine(shaderFolder, $"{name}.{shader.EntryPoint}.hlsl"), shader.Source);
                }
            }
        }

        return problems;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {name}: {ex.Message}");
        return 1;
    }
}

static int CheckExpressions()
{
    var failures = 0;
    void Expect(string code, double expected, string variable = "x")
    {
        var context = new EelContext(new EelGlobals());
        try
        {
            EelCompiler.Compile(code, context).Run();
            var actual = context[variable];
            if (Math.Abs(actual - expected) > 1e-9)
            {
                Console.WriteLine($"FAIL expression `{code}`: {variable} = {actual}, expected {expected}");
                failures++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL expression `{code}`: {ex.Message}");
            failures++;
        }
    }

    Expect("x = 1 + 2 * 3;", 7);
    Expect("x = (1 + 2) * 3", 9);
    Expect("x = 2 ^ 3 * 2;", 16);
    Expect("x = -2 + 5;", 3);
    Expect("x = 7 % 3;", 1);
    Expect("x = 7 % 0;", 0);
    Expect("x = 5 / 0;", 0);
    Expect("x = above(2, 1) + below(2, 1) * 10 + equal(3, 3) * 100;", 101);
    Expect("x = if(above(bass, 0), 5, 6);", 6);
    Expect("y = 3; y += 2; y *= 2; x = y;", 10);
    Expect("x = sqr(3) + sqrt(16) + abs(-2) + sign(-4);", 14);
    Expect("x = min(3, 4) + max(3, 4);", 7);
    Expect("x = int(2.7) + int(-2.5);", -1);
    Expect("x = band(1, 0) + bor(1, 0) * 2 + bnot(0) * 4;", 6);
    Expect("x = 1 < 2 && 3 > 2;", 1);
    Expect("x = 0 || 0;", 0);
    Expect("x = 2 > 1 ? 10 : 20;", 10);
    Expect("x = !0 + !5;", 1);
    Expect("x = 6 | 1; ", 7);
    Expect("x = 6 & 3;", 2);
    Expect("megabuf(5) = 42; x = megabuf(5);", 42);
    Expect("gmegabuf(10) = 3; gmegabuf(10) += 4; x = gmegabuf(10);", 7);
    Expect("reg05 = 9; x = reg05;", 9);
    Expect("i = 0; loop(10, i = i + 1); x = i;", 10);
    Expect("i = 0; while(i += 1; i < 5); x = i;", 5);
    Expect("x = exec2(y = 3, y * 2);", 6);
    Expect("x = $pi;", Math.PI);
    Expect("x = 0x10 + 1e2 + .5;", 116.5);
    Expect("x = sigmoid(0, 1);", 0.5);
    Expect("// a comment\n x = 1; /* and another */ x = x + 1;", 2);
    Expect("x = 1\ny = 2", 1);
    Expect("x = atan2(1, 1) * 4;", Math.PI);
    Expect("x = rand(1);", 0);
    Expect("Q1 = 4; x = q1;", 4);
    Expect("x = pow(2, 10) + log(1) + log10(100) + exp(0);", 1027);
    Expect("a = 3; b = 4; x = a == 3 ? (b != 4 ? 1 : 2) : 3;", 2);
    Expect("x = 5; x -= 2; x /= 3; x ^= 2;", 1);

    // Syntax errors must be reported, not crash.
    try
    {
        EelCompiler.Compile("x = (1 + ;", new EelContext(new EelGlobals()));
        Console.WriteLine("FAIL: a syntax error compiled.");
        failures++;
    }
    catch (EelException)
    {
    }

    var warnings = new List<string>();
    EelCompiler.Compile("x = nosuchfunction(2);", new EelContext(new EelGlobals()), warnings);
    if (warnings.Count == 0)
    {
        Console.WriteLine("FAIL: an unknown function gave no warning.");
        failures++;
    }

    Console.WriteLine(failures == 0 ? "ok   expressions" : $"FAIL expressions: {failures}");
    return failures;
}

// The warp mesh against sums done by hand: no warp reads each point from where it is, zoom 2
// reads from halfway to the middle, and dx moves the picture.
static int CheckWarp()
{
    var failures = 0;
    void Expect(string settings, Func<double, double, (double U, double V)> expected)
    {
        var runner = new MilkPresetRunner(MilkPreset.Parse("[preset00]\nwarp=0\n" + settings, "test"), new EelGlobals());
        runner.Render(new MilkFrameInput(1, 1 / 60.0, 0, 1280, 720, 16, 12), new MilkAudio());
        var mesh = runner.Mesh;
        for (var at = 0; at < mesh.Length; at += MilkPresetRunner.MeshFloatsPerVertex)
        {
            var (u, v) = expected(mesh[at + 4], mesh[at + 5]);
            if (Math.Abs(mesh[at + 2] - u) > 1e-4 || Math.Abs(mesh[at + 3] - v) > 1e-4)
            {
                Console.WriteLine($"FAIL warp `{settings.ReplaceLineEndings(" ")}`: at ({mesh[at + 4]}, {mesh[at + 5]}) read ({mesh[at + 2]}, {mesh[at + 3]}), expected ({u}, {v})");
                failures++;
                return;
            }
        }
    }

    Expect("zoom=1", (u, v) => (u, v));
    Expect("zoom=2", (u, v) => ((u - 0.5) / 2 + 0.5, (v - 0.5) / 2 + 0.5));
    Expect("dx=0.1\ndy=-0.05", (u, v) => (u - 0.1, v + 0.05));
    Expect("sx=2\nsy=0.5", (u, v) => ((u - 0.5) / 2 + 0.5, (v - 0.5) * 2 + 0.5));
    Expect("per_pixel_1=zoom = 1 + above(x, 0.5);", (u, v) => u > 0.5 + 1e-6 ? ((u - 0.5) / 2 + 0.5, (v - 0.5) / 2 + 0.5) : (u, v));
    Console.WriteLine(failures == 0 ? "ok   warp mesh" : $"FAIL warp mesh: {failures}");
    return failures;
}

static string FindRepository()
{
    var folder = AppContext.BaseDirectory;
    while (folder is not null && !Directory.Exists(Path.Combine(folder, "src", "Lucent.Windows")))
    {
        folder = Path.GetDirectoryName(folder);
    }

    return folder ?? Directory.GetCurrentDirectory();
}
