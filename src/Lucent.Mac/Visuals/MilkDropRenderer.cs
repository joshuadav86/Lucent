using System.Runtime.InteropServices;
using SharpMetal.Metal;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace Lucent.Mac.Visuals;

/// <summary>
/// Draws MilkDrop presets with Metal, as the Windows app's MilkDropRenderer does with Direct3D 11 and
/// from the same shaders (Shaders/MilkDrop.hlsl, translated): last frame's image is warped through the
/// preset's mesh (by its warp shader, or MilkDrop 1's), the shapes and waves are drawn on top, blurred
/// copies are made if its shaders want them, and the composite (its shader, or MilkDrop 1's echo, gamma
/// and so on) puts the result into Lucent's picture. That picture then goes through Lucent's own
/// transitions and screen pass like any other visual. Everything the presets share (the noise textures,
/// the engine's pipelines, the upload buffers) lives here.
/// </summary>
public sealed class MilkDropRenderer : IDisposable
{
    // MilkDrop drew into an image about the size of the window; much bigger only thins its lines.
    private const int MaxImageSize = 2048;

    // Uploads are placed at multiples of this in the frame's buffer, as Metal wants for buffer offsets.
    private const int UploadAlignment = 256;

    private readonly MTLDevice _device;
    private readonly List<MTLLibrary> _libraries = new();
    private readonly MTLRenderPipelineState _classicWarp;
    private readonly MTLRenderPipelineState _classicComposite;
    private readonly MTLRenderPipelineState[] _draw = new MTLRenderPipelineState[4];
    private readonly MTLRenderPipelineState _blurAcross;
    private readonly MTLRenderPipelineState _blurDown;
    private readonly MTLSamplerState[] _samplers;
    private readonly MTLTexture[] _noise;
    private readonly MTLTexture _black;
    private readonly UploadBuffer[] _uploads;
    private readonly float[] _blurValues = new float[8];
    private int _audioFrame = -1;

    /// <param name="framesInFlight">How many frames the engine can have on the GPU at once; each gets its own upload buffer.</param>
    public MilkDropRenderer(MTLDevice device, MetalShaderFiles files, int framesInFlight)
    {
        _device = device;
        MTLFunction Function(string entryPoint) =>
            MetalPipelines.LoadFunction(device, files.MilkDropEngine(entryPoint), MetalTranslation.MetalNames[entryPoint], _libraries);

        WarpVertices = Function("VSWarp");
        ScreenVertices = Function("VSScreen");
        var drawVertices = Function("VSDraw");
        _classicWarp = MetalPipelines.Make(device, WarpVertices, Function("PSWarpClassic"), MilkDropImages.MainFormat, MetalBlend.None);
        _classicComposite = MetalPipelines.Make(device, ScreenVertices, Function("PSCompClassic"), VisualEngine.FeedbackFormat, MetalBlend.None);
        var draw = Function("PSDraw");
        var drawTextured = Function("PSDrawTextured");
        _draw[0] = MetalPipelines.Make(device, drawVertices, draw, MilkDropImages.MainFormat, MetalBlend.Alpha);
        _draw[1] = MetalPipelines.Make(device, drawVertices, draw, MilkDropImages.MainFormat, MetalBlend.AddAlpha);
        _draw[2] = MetalPipelines.Make(device, drawVertices, drawTextured, MilkDropImages.MainFormat, MetalBlend.Alpha);
        _draw[3] = MetalPipelines.Make(device, drawVertices, drawTextured, MilkDropImages.MainFormat, MetalBlend.AddAlpha);
        _blurAcross = MetalPipelines.Make(device, ScreenVertices, Function("PSBlurAcross"), MilkDropImages.BlurWorkFormat, MetalBlend.None);
        _blurDown = MetalPipelines.Make(device, ScreenVertices, Function("PSBlurDown"), MilkDropImages.MainFormat, MetalBlend.None);

        // In the order of MilkDrop.hlsli: linear wrap, linear clamp, point wrap, point clamp.
        _samplers = new[]
        {
            Sampler(MTLSamplerMinMagFilter.Linear, MTLSamplerAddressMode.Repeat),
            Sampler(MTLSamplerMinMagFilter.Linear, MTLSamplerAddressMode.ClampToEdge),
            Sampler(MTLSamplerMinMagFilter.Nearest, MTLSamplerAddressMode.Repeat),
            Sampler(MTLSamplerMinMagFilter.Nearest, MTLSamplerAddressMode.ClampToEdge),
        };

        // The same noise as on Windows, every time, so presets look the same from one run to the next.
        var random = new Random(1234);
        _noise = new[]
        {
            Noise2D(256, 1, random),
            Noise2D(32, 1, random),
            Noise2D(256, 4, random),
            Noise2D(256, 8, random),
            Noise3D(32, 1, random),
            Noise3D(32, 4, random),
        };

        // Stands in for a blur a shader reads that this preset didn't make.
        _black = Texture(MTLTextureType.Type2D, 1, 1, new byte[] { 0, 0, 0, 255 });
        _uploads = Enumerable.Range(0, framesInFlight).Select(_ => new UploadBuffer(device)).ToArray();
    }

    /// <summary>The sound, in MilkDrop's terms, shared by every preset drawn this frame.</summary>
    public MilkAudio Audio { get; } = new();

    /// <summary>The warp mesh's vertex function, which every warp pipeline uses.</summary>
    public MTLFunction WarpVertices { get; }

    /// <summary>The full-screen triangle's vertex function, which every composite pipeline uses.</summary>
    public MTLFunction ScreenVertices { get; }

    public MTLDevice Device => _device;

    /// <summary>
    /// Draws one frame of a MilkDrop preset into <paramref name="destination"/> (Lucent's picture,
    /// <paramref name="width"/> x <paramref name="height"/>). <paramref name="frameSlot"/> is which of the
    /// engine's frames in flight this is: its upload buffer is free again once the engine has waited for it.
    /// </summary>
    public void Draw(
        MilkDropVisual visual, MTLCommandBuffer commands, MTLTexture destination, int width, int height,
        AudioAnalyzer analyzer, int frame, int frameSlot, double time, double deltaTime, float quality, double secondsPerVisual)
    {
        var uploads = _uploads[frameSlot];
        if (_audioFrame != frame)
        {
            // Once a frame, however many MilkDrop presets are on screen (two during a transition).
            _audioFrame = frame;
            Audio.Update(analyzer, deltaTime);
            uploads.Reset();
        }

        var fit = Math.Min(1.0, MaxImageSize / (double)Math.Max(width, height));
        var imageWidth = Math.Max(16, (int)Math.Round(width * fit));
        var imageHeight = Math.Max(16, (int)Math.Round(height * fit));
        var images = visual.ImagesFor(commands, imageWidth, imageHeight, time);
        var (meshWidth, meshHeight) = quality switch
        {
            < 0.5f => (32, 24),
            < 1.5f => (48, 36),
            < 2.5f => (64, 48),
            _ => (96, 72),
        };

        // Everything that can go wrong (the preset's equations) happens before anything is encoded.
        var runner = visual.Runner;
        var progress = secondsPerVisual > 0 ? (time - visual.StartTime) / secondsPerVisual : 0;
        runner.Render(new MilkFrameInput(time, deltaTime, progress, imageWidth, imageHeight, meshWidth, meshHeight), Audio);
        var meshPoints = (runner.MeshWidth + 1) * (runner.MeshHeight + 1);
        var (meshBuffer, meshOffset) = uploads.Put(runner.Mesh, meshPoints * MilkPresetRunner.MeshFloatsPerVertex);
        var geometry = runner.Geometry;
        var (shapeBuffer, shapeOffset) = geometry.VertexCount > 0
            ? uploads.Put(geometry.Data, geometry.VertexCount * MilkGeometry.FloatsPerVertex)
            : (meshBuffer, 0UL);
        var constants = runner.Constants;

        // 1. Warp last frame's image into this frame's, through the mesh, then 2. the shapes, waves,
        // borders and motion vectors on top.
        var target = images.Current;
        var encoder = BeginPass(commands, target.Texture);
        encoder.SetViewport(Viewport(target.Width, target.Height));
        encoder.SetCullMode(MTLCullMode.None);
        SetConstants(encoder, constants);
        BindTextures(encoder, images.Previous.Texture, images);
        encoder.SetRenderPipelineState(visual.Warp ?? _classicWarp);
        encoder.SetVertexBuffer(meshBuffer, meshOffset, 2);
        encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, (ulong)(runner.MeshWidth * runner.MeshHeight * 6));
        if (geometry.VertexCount > 0)
        {
            encoder.SetVertexBuffer(shapeBuffer, shapeOffset, 3);
            foreach (var batch in geometry.Batches)
            {
                encoder.SetRenderPipelineState(_draw[(batch.Textured ? 2 : 0) + (batch.Additive ? 1 : 0)]);
                encoder.DrawPrimitives(MTLPrimitiveType.Triangle, (ulong)batch.FirstVertex, (ulong)batch.VertexCount);
            }
        }

        encoder.EndEncoding();

        // 3. Blurred copies for the shaders, each from the one before.
        for (var level = 0; level < images.Blurs.Length; level++)
        {
            var source = level == 0 ? target : images.Blurs[level - 1];
            var blur = images.Blurs[level];
            var work = images.BlurWork[level];
            var (min, max) = BlurRange(constants, level);
            var (sourceMin, sourceMax) = level == 0 ? (0f, 1f) : BlurRange(constants, level - 1);
            _blurValues[0] = 1f / blur.Width;
            _blurValues[1] = 1f / blur.Height;
            _blurValues[2] = sourceMax - sourceMin;
            _blurValues[3] = sourceMin;
            _blurValues[4] = 1f / (max - min);
            _blurValues[5] = -min / (max - min);
            BlurPass(commands, work, source.Texture, _blurAcross);

            // The second pass reads the first's result as it is.
            _blurValues[2] = 1f;
            _blurValues[3] = 0f;
            BlurPass(commands, blur, work.Texture, _blurDown);
        }

        // 4. The composite, into Lucent's picture.
        encoder = BeginPass(commands, destination);
        encoder.SetViewport(Viewport(width, height));
        SetConstants(encoder, constants);
        BindTextures(encoder, target.Texture, images);
        encoder.SetRenderPipelineState(visual.Composite ?? _classicComposite);
        encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
        encoder.EndEncoding();
        images.Swap();
    }

    /// <summary>A blur's range (blur1_min and blur1_max...), from the constants: they start at register 19.</summary>
    private static (float Min, float Max) BlurRange(float[] constants, int level) =>
        (constants[19 * 4 + level * 2], constants[19 * 4 + level * 2 + 1]);

    private unsafe void BlurPass(MTLCommandBuffer commands, MilkDropImage destination, MTLTexture source, MTLRenderPipelineState pipeline)
    {
        var encoder = BeginPass(commands, destination.Texture);
        encoder.SetViewport(Viewport(destination.Width, destination.Height));
        encoder.SetRenderPipelineState(pipeline);
        fixed (float* values = _blurValues)
        {
            encoder.SetFragmentBytes((IntPtr)values, (ulong)(_blurValues.Length * sizeof(float)), 1);
        }

        encoder.SetFragmentTexture(source, 0);
        encoder.SetFragmentSamplerState(_samplers[1], 1);
        encoder.DrawPrimitives(MTLPrimitiveType.Triangle, 0, 3);
        encoder.EndEncoding();
    }

    /// <summary>The main image at texture 0, the blurs (or black, for ones not made), the noise and the samplers.</summary>
    private void BindTextures(MTLRenderCommandEncoder encoder, MTLTexture main, MilkDropImages images)
    {
        encoder.SetFragmentTexture(main, 0);
        for (var level = 0; level < 3; level++)
        {
            encoder.SetFragmentTexture(level < images.Blurs.Length ? images.Blurs[level].Texture : _black, (ulong)(1 + level));
        }

        for (var i = 0; i < _noise.Length; i++)
        {
            encoder.SetFragmentTexture(_noise[i], (ulong)(4 + i));
        }

        for (var i = 0; i < _samplers.Length; i++)
        {
            encoder.SetFragmentSamplerState(_samplers[i], (ulong)i);
        }
    }

    private static unsafe void SetConstants(MTLRenderCommandEncoder encoder, float[] constants)
    {
        fixed (float* values = constants)
        {
            encoder.SetVertexBytes((IntPtr)values, (ulong)(constants.Length * sizeof(float)), 0);
            encoder.SetFragmentBytes((IntPtr)values, (ulong)(constants.Length * sizeof(float)), 0);
        }
    }

    /// <summary>A pass that draws every pixel of <paramref name="target"/>, so nothing needs loading first.</summary>
    private static MTLRenderCommandEncoder BeginPass(MTLCommandBuffer commands, MTLTexture target)
    {
        var pass = new MTLRenderPassDescriptor();
        var colour = pass.ColorAttachments.Object(0);
        colour.Texture = target;
        colour.LoadAction = MTLLoadAction.DontCare;
        colour.StoreAction = MTLStoreAction.Store;
        var encoder = commands.RenderCommandEncoder(pass);
        pass.Dispose();
        return encoder;
    }

    /// <summary>Clears a new image to black, as MilkDrop starts a preset.</summary>
    internal static void Clear(MTLCommandBuffer commands, MTLTexture target)
    {
        var pass = new MTLRenderPassDescriptor();
        var colour = pass.ColorAttachments.Object(0);
        colour.Texture = target;
        colour.LoadAction = MTLLoadAction.Clear;
        colour.StoreAction = MTLStoreAction.Store;
        colour.ClearColor = new MTLClearColor { red = 0, green = 0, blue = 0, alpha = 1 };
        commands.RenderCommandEncoder(pass).EndEncoding();
        pass.Dispose();
    }

    private static MTLViewport Viewport(int width, int height) =>
        new() { originX = 0, originY = 0, width = width, height = height, znear = 0, zfar = 1 };

    private MTLSamplerState Sampler(MTLSamplerMinMagFilter filter, MTLSamplerAddressMode address)
    {
        var description = new MTLSamplerDescriptor
        {
            MinFilter = filter,
            MagFilter = filter,
            SAddressMode = address,
            TAddressMode = address,
            RAddressMode = address,
        };
        var sampler = _device.NewSamplerState(description);
        description.Dispose();
        return sampler;
    }

    /// <summary>Random colours, smoothed across <paramref name="zoom"/> pixels (1 = pure noise), tiling.</summary>
    private MTLTexture Noise2D(int size, int zoom, Random random)
    {
        var cells = size / zoom;
        var grid = new byte[cells * cells * 4];
        random.NextBytes(grid);
        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                for (var channel = 0; channel < 4; channel++)
                {
                    pixels[(y * size + x) * 4 + channel] = Smoothed(grid, cells, x, y, 0, zoom, channel, threeD: false);
                }
            }
        }

        return Texture(MTLTextureType.Type2D, size, 1, pixels);
    }

    private MTLTexture Noise3D(int size, int zoom, Random random)
    {
        var cells = size / zoom;
        var grid = new byte[cells * cells * cells * 4];
        random.NextBytes(grid);
        var pixels = new byte[size * size * size * 4];
        for (var z = 0; z < size; z++)
        {
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        pixels[((z * size + y) * size + x) * 4 + channel] = Smoothed(grid, cells, x, y, z, zoom, channel, threeD: true);
                    }
                }
            }
        }

        return Texture(MTLTextureType.Type3D, size, size, pixels);
    }

    /// <summary>A square (or cube, <paramref name="depth"/> deep) 8-bit RGBA texture holding <paramref name="pixels"/>.</summary>
    private MTLTexture Texture(MTLTextureType type, int size, int depth, byte[] pixels)
    {
        var description = new MTLTextureDescriptor
        {
            TextureType = type,
            PixelFormat = MTLPixelFormat.RGBA8Unorm,
            Width = (ulong)size,
            Height = (ulong)size,
            Depth = (ulong)depth,
            Usage = MTLTextureUsage.ShaderRead,
            // Managed works on every Mac, Intel ones included; the CPU writes it once, here.
            StorageMode = MTLStorageMode.Managed,
        };
        var texture = _device.NewTexture(description);
        description.Dispose();
        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            texture.ReplaceRegion(
                new MTLRegion { origin = new MTLOrigin(), size = new MTLSize { width = (ulong)size, height = (ulong)size, depth = (ulong)depth } },
                0, 0, handle.AddrOfPinnedObject(), (ulong)(size * 4), type == MTLTextureType.Type3D ? (ulong)(size * size * 4) : 0);
        }
        finally
        {
            handle.Free();
        }

        return texture;
    }

    /// <summary>One channel of the noise at a pixel: the random grid blended smoothly between its points.</summary>
    private static byte Smoothed(byte[] grid, int cells, int x, int y, int z, int zoom, int channel, bool threeD)
    {
        if (zoom == 1)
        {
            return grid[((threeD ? z * cells * cells : 0) + y * cells + x) * 4 + channel];
        }

        static float Ease(float t) => t * t * (3 - 2 * t);
        float tx = Ease(x % zoom / (float)zoom), ty = Ease(y % zoom / (float)zoom), tz = Ease(z % zoom / (float)zoom);
        int x0 = x / zoom, y0 = y / zoom, z0 = z / zoom;
        int x1 = (x0 + 1) % cells, y1 = (y0 + 1) % cells, z1 = (z0 + 1) % cells;
        float At(int gx, int gy, int gz) => grid[((threeD ? gz * cells * cells : 0) + gy * cells + gx) * 4 + channel];
        float Plane(int gz) =>
            (At(x0, y0, gz) * (1 - tx) + At(x1, y0, gz) * tx) * (1 - ty) + (At(x0, y1, gz) * (1 - tx) + At(x1, y1, gz) * tx) * ty;
        var value = threeD ? Plane(z0) * (1 - tz) + Plane(z1) * tz : Plane(0);
        return (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
    }

    public void Dispose()
    {
        foreach (var upload in _uploads)
        {
            upload.Dispose();
        }

        foreach (var texture in _noise)
        {
            texture.Dispose();
        }

        _black.Dispose();
        foreach (var sampler in _samplers)
        {
            sampler.Dispose();
        }

        foreach (var pipeline in _draw)
        {
            pipeline.Dispose();
        }

        _classicWarp.Dispose();
        _classicComposite.Dispose();
        _blurAcross.Dispose();
        _blurDown.Dispose();
        foreach (var library in _libraries)
        {
            library.Dispose();
        }
    }

    /// <summary>
    /// One frame's uploads (the warp meshes and the shapes and waves), one after another in a buffer the
    /// CPU writes and the GPU reads. A buffer that fills up is swapped for one twice the size; the frames
    /// already encoded keep their hold on the old one until the GPU has finished with it.
    /// </summary>
    private sealed class UploadBuffer : IDisposable
    {
        private readonly MTLDevice _device;
        private MTLBuffer _buffer;
        private ulong _used;

        public UploadBuffer(MTLDevice device)
        {
            _device = device;
            _buffer = device.NewBuffer(1024 * 1024, MTLResourceOptions.ResourceStorageModeShared);
        }

        public void Reset() => _used = 0;

        public (MTLBuffer Buffer, ulong Offset) Put(float[] data, int count)
        {
            var bytes = (ulong)count * sizeof(float);
            if (_used + bytes > _buffer.Length)
            {
                var size = Math.Max(_buffer.Length * 2, bytes + UploadAlignment);
                _buffer.Dispose();
                _buffer = _device.NewBuffer(size, MTLResourceOptions.ResourceStorageModeShared);
                _used = 0;
            }

            var offset = _used;
            Marshal.Copy(data, 0, _buffer.Contents + (nint)offset, count);
            _used = (offset + bytes + UploadAlignment - 1) / UploadAlignment * UploadAlignment;
            return (_buffer, offset);
        }

        public void Dispose() => _buffer.Dispose();
    }
}
