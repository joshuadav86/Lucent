using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using WinampInspiredVisualizer.Visualizer.Avs;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

// Usage: AvsCheck [preset files or folders...] [--images <output folder>] [--size <width>x<height>] [--strict]
// With no presets given it checks the ones that ship with Lucent. Each preset runs for 5 seconds of
// made-up music. Exits with 1 if a preset doesn't load or throws, or (with --strict) leaves anything out.
// With --images it saves frames 60, 180 and 300 of each preset as PNGs, for checking by eye.
var repository = FindRepository();
var inputs = new List<string>();
string? imageFolder = null;
var width = 640;
var height = 360;
var strict = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--images" when i + 1 < args.Length:
            imageFolder = args[++i];
            break;
        case "--size" when i + 1 < args.Length:
            var parts = args[++i].Split('x');
            width = int.Parse(parts[0]);
            height = int.Parse(parts[1]);
            break;
        case "--strict":
            strict = true;
            break;
        default:
            inputs.Add(args[i]);
            break;
    }
}

if (inputs.Count == 0)
{
    inputs.Add(Path.Combine(repository, "src/WinampInspiredVisualizer/Presets/AVS"));
}

var files = inputs.SelectMany<string, string>(input => Directory.Exists(input)
    ? Directory.EnumerateFiles(input, "*.avs", SearchOption.AllDirectories).Order()
    : new[] { input }).ToList();
if (imageFolder is not null)
{
    Directory.CreateDirectory(imageFolder);
}

var failures = 0;
foreach (var file in files)
{
    failures += CheckPreset(file, width, height, imageFolder, strict);
}

Console.WriteLine(failures == 0 ? $"All good: {files.Count} preset(s)." : $"{failures} problem(s).");
return failures == 0 ? 0 : 1;

static int CheckPreset(string file, int width, int height, string? imageFolder, bool strict)
{
    var name = Path.GetFileNameWithoutExtension(file);
    try
    {
        var preset = AvsPreset.Parse(File.ReadAllBytes(file), name);
        var runner = new AvsPresetRunner(preset, new EelGlobals(), Path.GetDirectoryName(file));
        var left = new float[AvsAudio.Samples];
        var right = new float[AvsAudio.Samples];
        const double dt = 1.0 / 60;
        var clock = new Stopwatch();
        for (var frame = 1; frame <= 300; frame++)
        {
            // Made-up music: a kick twice a second over a pair of tones.
            var time = frame * dt;
            var kick = Math.Exp(-(time % 0.5) * 12);
            for (var i = 0; i < AvsAudio.Samples; i++)
            {
                var t = time + i / 44100.0;
                left[i] = (float)(0.5 * Math.Sin(t * 2 * Math.PI * 110) * (0.3 + kick) + 0.1 * Math.Sin(t * 2 * Math.PI * 1760));
                right[i] = (float)(0.5 * Math.Sin(t * 2 * Math.PI * 165) * (0.3 + kick));
            }

            runner.Audio.Update(left, right, frequency => (float)(Math.Max(0, 1 - frequency / 9000) * (0.4 + 0.6 * kick)), time % 0.5 < dt);
            clock.Start();
            runner.Render(width, height, time);
            clock.Stop();
            if (imageFolder is not null && frame % 120 == 60)
            {
                SavePng(Path.Combine(imageFolder, $"{name}.{frame}.png"), runner.Picture, width, height);
            }
        }

        var warnings = runner.Warnings;
        var lit = runner.Picture.Count(pixel => (pixel & 0xffffff) != 0) * 100.0 / runner.Picture.Length;
        Console.WriteLine($"{name}: {clock.Elapsed.TotalMilliseconds / 300:F2} ms a frame at {width}x{height}, {lit:F0}% of the picture lit"
            + (warnings.Count == 0 ? "" : $"\n  - {string.Join("\n  - ", warnings)}"));
        return strict && warnings.Count > 0 ? 1 : 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"{name}: FAILED: {ex}");
        return 1;
    }
}

// A plain 8-bit RGB PNG, no libraries needed.
static void SavePng(string path, int[] pixels, int width, int height)
{
    var raw = new byte[height * (width * 3 + 1)];
    for (var y = 0; y < height; y++)
    {
        var row = y * (width * 3 + 1);
        for (var x = 0; x < width; x++)
        {
            var c = pixels[y * width + x];
            raw[row + 1 + x * 3] = (byte)(c >> 16);
            raw[row + 2 + x * 3] = (byte)(c >> 8);
            raw[row + 3 + x * 3] = (byte)c;
        }
    }

    using var file = File.Create(path);
    file.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
    var header = new byte[13];
    BinaryPrimitives.WriteInt32BigEndian(header, width);
    BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
    header[8] = 8;
    header[9] = 2;
    Chunk(file, "IHDR", header);
    using (var compressed = new MemoryStream())
    {
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        Chunk(file, "IDAT", compressed.ToArray());
    }

    Chunk(file, "IEND", Array.Empty<byte>());
}

static void Chunk(Stream stream, string type, byte[] data)
{
    var length = new byte[4];
    BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
    stream.Write(length);
    var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
    stream.Write(typed);
    var crc = new byte[4];
    BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
    stream.Write(crc);
}

static uint Crc(byte[] data)
{
    var crc = 0xffffffffu;
    foreach (var b in data)
    {
        crc ^= b;
        for (var k = 0; k < 8; k++)
        {
            crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        }
    }

    return ~crc;
}

static string FindRepository()
{
    var folder = AppContext.BaseDirectory;
    while (folder is not null && !Directory.Exists(Path.Combine(folder, "src", "Lucent.Core")))
    {
        folder = Path.GetDirectoryName(folder);
    }

    return folder ?? Directory.GetCurrentDirectory();
}
