using SharpMetal.Metal;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace Lucent.Mac.Visuals;

/// <summary>
/// One MilkDrop preset, ready to draw: its equations (compiled and running), its MilkDrop 2 shaders
/// if it has them and they compiled, and, while it's on screen, its own images. The Mac's version of
/// the Windows app's MilkDropVisual: MilkDrop keeps its picture to itself between frames, separately
/// from what's shown (the composite), so each preset has its own pair of images to warp back and forth
/// between, plus its blurred copies.
/// </summary>
public sealed class MilkDropVisual : IDisposable
{
    // Shared by every preset, as reg00-reg99 and gmegabuf are in MilkDrop.
    private static readonly EelGlobals Globals = new();

    private readonly MTLDevice _device;
    private readonly List<MTLLibrary> _libraries;

    private MilkDropVisual(
        MTLDevice device, MilkPresetRunner runner, MTLRenderPipelineState? warp, MTLRenderPipelineState? composite, int blurLevels,
        IReadOnlyList<string> notes, List<MTLLibrary> libraries)
    {
        _device = device;
        Runner = runner;
        Warp = warp;
        Composite = composite;
        BlurLevels = blurLevels;
        Notes = notes;
        _libraries = libraries;
    }

    public MilkPresetRunner Runner { get; }

    /// <summary>The preset's warp shader's pipeline, or null for MilkDrop 1's warp.</summary>
    public MTLRenderPipelineState? Warp { get; }

    /// <summary>The preset's composite shader's pipeline, or null for MilkDrop 1's composite.</summary>
    public MTLRenderPipelineState? Composite { get; }

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
    /// Reads a preset and makes its shaders (on a compile thread). Throws if the file isn't a MilkDrop preset at all;
    /// anything less (an equation or shader that doesn't compile) is left out and listed in <see cref="Notes"/>.
    /// </summary>
    public static MilkDropVisual Compile(MilkDropRenderer renderer, MetalShaderFiles files, PresetSource source)
    {
        var preset = MilkPreset.Parse(source.Code, source.Name);
        var runner = new MilkPresetRunner(preset, Globals);
        var notes = runner.Warnings.ToList();
        var libraries = new List<MTLLibrary>();
        var prelude = files.MilkDropPrelude();
        var blurLevels = 0;
        MTLRenderPipelineState? Shader(bool wanted, string code, MilkShaderKind kind, string label)
        {
            if (!wanted)
            {
                return null;
            }

            try
            {
                var translated = MilkShaderTranslator.Translate(prelude, code, kind);
                var metal = files.MilkDropPreset(source, translated);
                var fragment = MetalPipelines.LoadFunction(renderer.Device, metal, MetalTranslation.MetalNames[translated.EntryPoint], libraries);
                var pipeline = kind == MilkShaderKind.Warp
                    ? MetalPipelines.Make(renderer.Device, renderer.WarpVertices, fragment, MilkDropImages.MainFormat, MetalBlend.None)
                    : MetalPipelines.Make(renderer.Device, renderer.ScreenVertices, fragment, VisualEngine.FeedbackFormat, MetalBlend.None);
                blurLevels = Math.Max(blurLevels, translated.BlurLevels);
                if (translated.StandIns.Count > 0)
                {
                    notes.Add($"The {label} shader's textures {string.Join(", ", translated.StandIns)} aren't available, so it gets noise instead.");
                }

                return pipeline;
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                notes.Add($"The {label} shader didn't compile, so MilkDrop 1's {label} is used instead. {FirstLines(ex.Message)}");
                return null;
            }
        }

        var warp = Shader(preset.HasWarpShader, preset.WarpShader, MilkShaderKind.Warp, "warp");
        var composite = Shader(preset.HasCompShader, preset.CompShader, MilkShaderKind.Composite, "composite");
        return new MilkDropVisual(renderer.Device, runner, warp, composite, blurLevels, notes, libraries);
    }

    private static string FirstLines(string message)
    {
        var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" ", lines.Take(3));
    }

    /// <summary>Makes (or remakes, at a new size) the preset's images, starting it from the beginning when it comes on screen.</summary>
    public MilkDropImages ImagesFor(MTLCommandBuffer commands, int width, int height, double now)
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

        // Frames still on the GPU keep their own hold on the old images, so they can go now.
        Images?.Dispose();
        Images = new MilkDropImages(_device, commands, width, height, BlurLevels);
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
        Composite?.Dispose();
        foreach (var library in _libraries)
        {
            library.Dispose();
        }
    }
}

/// <summary>A MilkDrop preset's images: the main one (two, to warp from one into the other) and its blurred copies.</summary>
public sealed class MilkDropImages : IDisposable
{
    // 8 bits per channel, as in MilkDrop: presets count on colours clipping at white and fading out in steps.
    public const MTLPixelFormat MainFormat = MTLPixelFormat.RGBA8Unorm;
    public const MTLPixelFormat BlurWorkFormat = MTLPixelFormat.RGBA16Float;

    private int _current;

    public MilkDropImages(MTLDevice device, MTLCommandBuffer commands, int width, int height, int blurLevels)
    {
        Width = width;
        Height = height;
        Main = new[] { new MilkDropImage(device, commands, width, height, MainFormat), new MilkDropImage(device, commands, width, height, MainFormat) };
        Blurs = new MilkDropImage[blurLevels];
        BlurWork = new MilkDropImage[blurLevels];
        for (var level = 0; level < blurLevels; level++)
        {
            var blurWidth = Math.Max(1, width >> (level + 1));
            var blurHeight = Math.Max(1, height >> (level + 1));
            Blurs[level] = new MilkDropImage(device, commands, blurWidth, blurHeight, MainFormat);
            BlurWork[level] = new MilkDropImage(device, commands, blurWidth, blurHeight, BlurWorkFormat);
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

/// <summary>A texture that's drawn into and read from, black to begin with.</summary>
public sealed class MilkDropImage : IDisposable
{
    public MilkDropImage(MTLDevice device, MTLCommandBuffer commands, int width, int height, MTLPixelFormat format)
    {
        Width = width;
        Height = height;
        var description = MTLTextureDescriptor.Texture2DDescriptor(format, (ulong)width, (ulong)height, false);
        description.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
        description.StorageMode = MTLStorageMode.Private;
        Texture = device.NewTexture(description);
        MilkDropRenderer.Clear(commands, Texture);
    }

    public int Width { get; }

    public int Height { get; }

    public MTLTexture Texture { get; }

    public void Dispose() => Texture.Dispose();
}
