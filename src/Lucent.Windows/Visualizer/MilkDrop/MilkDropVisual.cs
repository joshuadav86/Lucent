using System.IO;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// One MilkDrop preset, ready to draw: its equations (compiled and running), its MilkDrop 2 shaders
/// if it has them and they compiled, and, while it's on screen, its own images. MilkDrop keeps
/// its picture to itself between frames, separately from what's shown (the composite), so each
/// preset has its own pair of images to warp back and forth between, plus its blurred copies.
/// </summary>
internal sealed class MilkDropVisual : IDisposable
{
    // Shared by every preset, as reg00-reg99 and gmegabuf are in MilkDrop.
    private static readonly EelGlobals Globals = new();

    private MilkDropVisual(MilkPresetRunner runner, ID3D11PixelShader? warp, ID3D11PixelShader? composite, int blurLevels, IReadOnlyList<string> notes)
    {
        Runner = runner;
        Warp = warp;
        Composite = composite;
        BlurLevels = blurLevels;
        Notes = notes;
    }

    public MilkPresetRunner Runner { get; }

    /// <summary>The preset's warp shader, or null for MilkDrop 1's warp.</summary>
    public ID3D11PixelShader? Warp { get; private set; }

    /// <summary>The preset's composite shader, or null for MilkDrop 1's composite.</summary>
    public ID3D11PixelShader? Composite { get; private set; }

    /// <summary>How many blurred copies (0-3) its shaders read.</summary>
    public int BlurLevels { get; }

    /// <summary>Parts of the preset that were left out or replaced, for the error log.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>Whether <see cref="Notes"/> have gone in the error log yet (once a session, when it's first shown).</summary>
    public bool NotesLogged { get; set; }

    /// <summary>Its images while it's on screen, else null.</summary>
    public MilkDropImages? Images { get; private set; }

    /// <summary>When it last came on screen (in the visualizer's seconds), for MilkDrop's progress.</summary>
    public double StartTime { get; private set; }

    /// <summary>
    /// Reads and compiles a preset (on a compile thread). Throws if the file isn't a MilkDrop preset at all;
    /// anything less (an equation or shader that doesn't compile) is left out and listed in <see cref="Notes"/>.
    /// </summary>
    public static MilkDropVisual Compile(ID3D11Device device, ShaderCache cache, PresetSource source, string prelude)
    {
        var preset = MilkPreset.Parse(source.Code, source.Name);
        var runner = new MilkPresetRunner(preset, Globals);
        var notes = runner.Warnings.ToList();
        var fileName = Path.GetFileName(source.FilePath);
        var blurLevels = 0;
        ID3D11PixelShader? Shader(bool wanted, string code, MilkShaderKind kind, string label)
        {
            if (!wanted)
            {
                return null;
            }

            try
            {
                var translated = MilkShaderTranslator.Translate(prelude, code, kind);
                var bytecode = cache.GetOrCompile(translated.Source, translated.EntryPoint, fileName, "ps_5_0");
                ID3D11PixelShader shader;
                try
                {
                    shader = device.CreatePixelShader(bytecode, null);
                }
                catch (SharpGen.Runtime.SharpGenException)
                {
                    // A damaged cache file: compile it again from scratch (a lost device just throws again).
                    bytecode = cache.GetOrCompile(translated.Source, translated.EntryPoint, fileName, "ps_5_0", useCache: false);
                    shader = device.CreatePixelShader(bytecode, null);
                }

                blurLevels = Math.Max(blurLevels, translated.BlurLevels);
                if (translated.StandIns.Count > 0)
                {
                    notes.Add($"The {label} shader's textures {string.Join(", ", translated.StandIns)} aren't available, so it gets noise instead.");
                }

                return shader;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                notes.Add($"The {label} shader didn't compile, so MilkDrop 1's {label} is used instead. {FirstLines(ex.Message)}");
                return null;
            }
        }

        var warp = Shader(preset.HasWarpShader, preset.WarpShader, MilkShaderKind.Warp, "warp");
        var composite = Shader(preset.HasCompShader, preset.CompShader, MilkShaderKind.Composite, "composite");
        return new MilkDropVisual(runner, warp, composite, blurLevels, notes);
    }

    private static string FirstLines(string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.Take(3));
    }

    /// <summary>Makes (or remakes, at a new size) the preset's images, starting it from the beginning when it comes on screen.</summary>
    public MilkDropImages ImagesFor(ID3D11Device device, ID3D11DeviceContext context, int width, int height, double now)
    {
        if (Images is { } images && images.Width == width && images.Height == height)
        {
            return images;
        }

        if (Images is null)
        {
            // Coming on screen: from the top, as MilkDrop starts a preset when it's loaded.
            Runner.Restart();
            StartTime = now;
        }

        Images?.Dispose();
        Images = new MilkDropImages(device, context, width, height, BlurLevels);
        return Images;
    }

    /// <summary>Frees its images once it's off screen.</summary>
    public void ReleaseImages()
    {
        Images?.Dispose();
        Images = null;
    }

    public void Dispose()
    {
        ReleaseImages();
        Warp?.Dispose();
        Warp = null;
        Composite?.Dispose();
        Composite = null;
    }
}

/// <summary>A MilkDrop preset's images: the main one (two, to warp from one into the other) and its blurred copies.</summary>
internal sealed class MilkDropImages : IDisposable
{
    // 8 bits per channel, as in MilkDrop: presets count on colours clipping at white and fading out in steps.
    private const Format MainFormat = Format.R8G8B8A8_UNorm;
    private const Format BlurWorkFormat = Format.R16G16B16A16_Float;

    private int _current;

    public MilkDropImages(ID3D11Device device, ID3D11DeviceContext context, int width, int height, int blurLevels)
    {
        Width = width;
        Height = height;
        Main = new[] { new MilkDropImage(device, context, width, height, MainFormat), new MilkDropImage(device, context, width, height, MainFormat) };
        Blurs = new MilkDropImage[blurLevels];
        BlurWork = new MilkDropImage[blurLevels];
        for (var level = 0; level < blurLevels; level++)
        {
            var blurWidth = Math.Max(1, width >> (level + 1));
            var blurHeight = Math.Max(1, height >> (level + 1));
            Blurs[level] = new MilkDropImage(device, context, blurWidth, blurHeight, MainFormat);
            BlurWork[level] = new MilkDropImage(device, context, blurWidth, blurHeight, BlurWorkFormat);
        }
    }

    public int Width { get; }

    public int Height { get; }

    public MilkDropImage[] Main { get; }

    public MilkDropImage[] Blurs { get; }

    /// <summary>Each blur's first (across) pass, kept at higher precision for the second.</summary>
    public MilkDropImage[] BlurWork { get; }

    /// <summary>The image being drawn this frame.</summary>
    public MilkDropImage Current => Main[_current];

    /// <summary>Last frame's image, which this frame warps.</summary>
    public MilkDropImage Previous => Main[1 - _current];

    /// <summary>Makes this frame's image last frame's, for the next frame.</summary>
    public void Swap() => _current = 1 - _current;

    public void Dispose()
    {
        foreach (var image in Main.Concat(Blurs).Concat(BlurWork))
        {
            image.Dispose();
        }
    }
}

/// <summary>A texture that's drawn into and read from.</summary>
internal sealed class MilkDropImage : IDisposable
{
    public MilkDropImage(ID3D11Device device, ID3D11DeviceContext context, int width, int height, Format format)
    {
        Width = width;
        Height = height;
        Texture = device.CreateTexture2D(
            format, (uint)width, (uint)height, 1, 1, null,
            BindFlags.RenderTarget | BindFlags.ShaderResource, ResourceOptionFlags.None,
            ResourceUsage.Default, CpuAccessFlags.None);
        Target = device.CreateRenderTargetView(Texture, null);
        Resource = device.CreateShaderResourceView(Texture, null);
        context.ClearRenderTargetView(Target, new Color4(0f, 0f, 0f, 1f));
    }

    public int Width { get; }

    public int Height { get; }

    public ID3D11Texture2D Texture { get; }

    public ID3D11RenderTargetView Target { get; }

    public ID3D11ShaderResourceView Resource { get; }

    public void Dispose()
    {
        Resource.Dispose();
        Target.Dispose();
        Texture.Dispose();
    }
}
