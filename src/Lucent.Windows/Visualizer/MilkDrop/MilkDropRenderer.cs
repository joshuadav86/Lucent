using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// Draws MilkDrop presets with Direct3D 11, the way MilkDrop does each frame: last frame's image is
/// warped through the preset's mesh (by its warp shader, or MilkDrop 1's), the shapes and waves are
/// drawn on top, blurred copies are made if its shaders want them, and the composite (its shader,
/// or MilkDrop 1's echo, gamma and so on) puts the result into Lucent's picture. That picture then
/// goes through Lucent's own transitions and screen pass like any other visual. Everything the
/// presets share (the noise textures, the engine's shaders, the upload buffers) lives here.
/// </summary>
internal sealed class MilkDropRenderer : IDisposable
{
    // MilkDrop drew into an image about the size of the window; much bigger only thins its lines.
    private const int MaxImageSize = 2048;
    private const int MeshStride = MilkPresetRunner.MeshFloatsPerVertex * sizeof(float);
    private const int DrawStride = MilkGeometry.FloatsPerVertex * sizeof(float);

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly ID3D11VertexShader _warpVertices;
    private readonly ID3D11VertexShader _screenVertices;
    private readonly ID3D11VertexShader _drawVertices;
    private readonly ID3D11PixelShader _classicWarp;
    private readonly ID3D11PixelShader _classicComposite;
    private readonly ID3D11PixelShader _draw;
    private readonly ID3D11PixelShader _drawTextured;
    private readonly ID3D11PixelShader _blurAcross;
    private readonly ID3D11PixelShader _blurDown;
    private readonly ID3D11SamplerState[] _samplers;
    private readonly ID3D11BlendState _alphaBlend;
    private readonly ID3D11BlendState _additiveBlend;
    private readonly ID3D11RasterizerState _noCulling;
    private readonly ID3D11Buffer _constants;
    private readonly ID3D11Buffer _blurConstants;
    private readonly float[] _blurValues = new float[8];
    private readonly ID3D11Buffer _drawConstants;
    private readonly uint[] _drawFirst = new uint[4];
    private readonly List<IDisposable> _noise = new();
    private readonly ID3D11ShaderResourceView[] _noiseViews;
    private DynamicBuffer? _mesh;
    private DynamicBuffer? _shapes;
    private int _audioFrame = -1;

    public MilkDropRenderer(ID3D11Device device, ID3D11DeviceContext context, ShaderCache cache, string prelude, string engine)
    {
        _device = device;
        _context = context;
        // MD_DRAW_FIRST: each batch of waves and shapes is told where it starts (see VSDraw).
        var source = "#define MD_DRAW_FIRST\n" + prelude + "\n" + engine;
        ID3D11VertexShader Vertex(string entry) => device.CreateVertexShader(cache.GetOrCompile(source, entry, "MilkDrop.hlsl", "vs_5_0"), null);
        ID3D11PixelShader Pixel(string entry) => device.CreatePixelShader(cache.GetOrCompile(source, entry, "MilkDrop.hlsl", "ps_5_0"), null);
        _warpVertices = Vertex("VSWarp");
        _screenVertices = Vertex("VSScreen");
        _drawVertices = Vertex("VSDraw");
        _classicWarp = Pixel("PSWarpClassic");
        _classicComposite = Pixel("PSCompClassic");
        _draw = Pixel("PSDraw");
        _drawTextured = Pixel("PSDrawTextured");
        _blurAcross = Pixel("PSBlurAcross");
        _blurDown = Pixel("PSBlurDown");

        // In the order of MilkDrop.hlsli: linear wrap, linear clamp, point wrap, point clamp.
        _samplers = new[]
        {
            Sampler(Filter.MinMagMipLinear, TextureAddressMode.Wrap),
            Sampler(Filter.MinMagMipLinear, TextureAddressMode.Clamp),
            Sampler(Filter.MinMagMipPoint, TextureAddressMode.Wrap),
            Sampler(Filter.MinMagMipPoint, TextureAddressMode.Clamp),
        };
        _alphaBlend = device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.InverseSourceAlpha));
        _additiveBlend = device.CreateBlendState(new BlendDescription(Blend.SourceAlpha, Blend.One));
        _noCulling = device.CreateRasterizerState(RasterizerDescription.CullNone);
        _constants = device.CreateBuffer(
            MilkPresetRunner.ConstantFloats * sizeof(float), BindFlags.ConstantBuffer, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        _blurConstants = device.CreateBuffer(
            (uint)(_blurValues.Length * sizeof(float)), BindFlags.ConstantBuffer, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.None, 0);
        _drawConstants = device.CreateBuffer(
            (uint)(_drawFirst.Length * sizeof(uint)), BindFlags.ConstantBuffer, ResourceUsage.Default,
            CpuAccessFlags.None, ResourceOptionFlags.None, 0);

        // The same noise every time, so presets look the same from one run to the next.
        var random = new Random(1234);
        _noiseViews = new[]
        {
            Noise2D(256, 1, random),
            Noise2D(32, 1, random),
            Noise2D(256, 4, random),
            Noise2D(256, 8, random),
            Noise3D(32, 1, random),
            Noise3D(32, 4, random),
        };
    }

    /// <summary>The sound, in MilkDrop's terms, shared by every preset drawn this frame.</summary>
    public MilkAudio Audio { get; } = new();

    /// <summary>
    /// Draws one frame of a MilkDrop preset into <paramref name="destination"/> (Lucent's picture,
    /// <paramref name="width"/> x <paramref name="height"/>). Afterwards the caller sets its own
    /// pipeline state again: this changes shaders, textures, samplers, blending and the viewport.
    /// </summary>
    public void Draw(
        MilkDropVisual visual, ID3D11RenderTargetView destination, int width, int height,
        AudioAnalyzer analyzer, int frame, double time, double deltaTime, float quality, double secondsPerVisual)
    {
        if (_audioFrame != frame)
        {
            // Once a frame, however many MilkDrop presets are on screen (two during a transition).
            _audioFrame = frame;
            Audio.Update(analyzer, deltaTime);
        }

        var context = _context;
        var fit = Math.Min(1.0, MaxImageSize / (double)Math.Max(width, height));
        var imageWidth = Math.Max(16, (int)Math.Round(width * fit));
        var imageHeight = Math.Max(16, (int)Math.Round(height * fit));
        var shown = visual.Images;
        var images = visual.ImagesFor(_device, context, imageWidth, imageHeight, time);
        var (meshWidth, meshHeight) = quality switch
        {
            < 0.5f => (32, 24),
            < 1.5f => (48, 36),
            < 2.5f => (64, 48),
            _ => (96, 72),
        };

        // The preset moves on at most 60 times a second; on the frames between, its last image is shown again.
        var runner = visual.Runner;
        var step = runner.StepDue(time, force: images != shown);
        if (step)
        {
            var progress = secondsPerVisual > 0 ? (time - visual.StartTime) / secondsPerVisual : 0;
            runner.Render(new MilkFrameInput(time, runner.StepDelta, progress, imageWidth, imageHeight, meshWidth, meshHeight), Audio);
        }

        context.UpdateSubresource(runner.Constants, _constants, 0, 0, 0, null);
        UnbindTextures();
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.RSSetState(_noCulling);
        context.OMSetBlendState(null);
        context.VSSetConstantBuffer(0, _constants);
        context.PSSetConstantBuffer(0, _constants);
        context.PSSetConstantBuffer(1, _blurConstants);
        context.PSSetSamplers(0, _samplers);
        context.PSSetShaderResources(4, _noiseViews);

        if (step)
        {
            var meshPoints = (runner.MeshWidth + 1) * (runner.MeshHeight + 1);
            _mesh = Upload(_mesh, runner.Mesh, meshPoints, MeshStride);
            var geometry = runner.Geometry;
            if (geometry.VertexCount > 0)
            {
                _shapes = Upload(_shapes, geometry.Data, geometry.VertexCount, DrawStride);
            }

            // 1. Warp last frame's image into this frame's, through the mesh.
            var target = images.Current;
            context.OMSetRenderTargets(target.Target, null);
            context.RSSetViewport(0, 0, target.Width, target.Height, 0, 1);
            context.PSSetShaderResource(0, images.Previous.Resource);
            BindBlurs(images);
            context.VSSetShader(_warpVertices);
            context.VSSetShaderResource(12, _mesh.View);
            context.PSSetShader(visual.Warp ?? _classicWarp);
            context.Draw((uint)(runner.MeshWidth * runner.MeshHeight * 6), 0);

            // 2. The shapes, waves, borders and motion vectors on top.
            if (geometry.VertexCount > 0)
            {
                context.VSSetShader(_drawVertices);
                context.VSSetShaderResource(13, _shapes!.View);
                context.VSSetConstantBuffer(2, _drawConstants);

                // Textured shapes read through the first sampler; clamp it when MilkDrop would have.
                var shapesWrap = runner.ShapesWrap(visual.Warp is not null, visual.BlurLevels);
                context.PSSetSampler(0, shapesWrap ? _samplers[0] : _samplers[1]);
                foreach (var batch in geometry.Batches)
                {
                    context.OMSetBlendState(batch.Additive ? _additiveBlend : _alphaBlend);
                    context.PSSetShader(batch.Textured ? _drawTextured : _draw);

                    // SV_VertexID starts at 0 whatever the draw's first vertex, so the batch's start goes in a constant.
                    _drawFirst[0] = (uint)batch.FirstVertex;
                    context.UpdateSubresource(_drawFirst, _drawConstants, 0, 0, 0, null);
                    context.Draw((uint)batch.VertexCount, 0);
                }

                context.OMSetBlendState(null);
                context.PSSetSampler(0, _samplers[0]);
            }

            // 3. Blurred copies for the shaders, each from the one before.
            context.VSSetShader(_screenVertices);
            UnbindTextures();
            context.PSSetShaderResources(4, _noiseViews);
            var constants = runner.Constants;
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
                context.UpdateSubresource(_blurValues, _blurConstants, 0, 0, 0, null);
                context.RSSetViewport(0, 0, blur.Width, blur.Height, 0, 1);
                Pass(work.Target, source.Resource, _blurAcross);

                // The second pass reads the first's result as it is.
                _blurValues[2] = 1f;
                _blurValues[3] = 0f;
                context.UpdateSubresource(_blurValues, _blurConstants, 0, 0, 0, null);
                Pass(blur.Target, work.Resource, _blurDown);
            }

            images.Swap();
        }

        // 4. The composite of the newest image (the one just made, if it moved on), into Lucent's picture.
        context.VSSetShader(_screenVertices);
        UnbindTextures();
        context.PSSetShaderResources(4, _noiseViews);
        context.OMSetRenderTargets(destination, null);
        context.RSSetViewport(0, 0, width, height, 0, 1);
        context.PSSetShaderResource(0, images.Previous.Resource);
        BindBlurs(images);
        context.PSSetShader(visual.Composite ?? _classicComposite);
        context.Draw(3, 0);

        UnbindTextures();
        context.VSSetShaderResource(12, null!);
        context.VSSetShaderResource(13, null!);
        context.RSSetState(null);
    }

    /// <summary>A blur's range (blur1_min and blur1_max...), from the constants: they start at register 19.</summary>
    private static (float Min, float Max) BlurRange(float[] constants, int level) =>
        (constants[19 * 4 + level * 2], constants[19 * 4 + level * 2 + 1]);

    private void Pass(ID3D11RenderTargetView destination, ID3D11ShaderResourceView source, ID3D11PixelShader shader)
    {
        _context.PSSetShaderResource(0, null!);
        _context.OMSetRenderTargets(destination, null);
        _context.PSSetShaderResource(0, source);
        _context.PSSetShader(shader);
        _context.Draw(3, 0);
        _context.PSSetShaderResource(0, null!);
    }

    private void BindBlurs(MilkDropImages images)
    {
        for (var level = 0; level < images.Blurs.Length; level++)
        {
            _context.PSSetShaderResource((uint)(1 + level), images.Blurs[level].Resource);
        }
    }

    private void UnbindTextures()
    {
        for (uint slot = 0; slot < 10; slot++)
        {
            _context.PSSetShaderResource(slot, null!);
        }
    }

    private DynamicBuffer Upload(DynamicBuffer? buffer, float[] data, int count, int stride)
    {
        if (buffer is null || buffer.Capacity < count)
        {
            buffer?.Dispose();
            buffer = new DynamicBuffer(_device, Math.Max(count, (buffer?.Capacity ?? 1024) * 2), stride);
        }

        var mapped = _context.Map(buffer.Buffer, MapMode.WriteDiscard, Vortice.Direct3D11.MapFlags.None);
        Marshal.Copy(data, 0, mapped.DataPointer, count * stride / sizeof(float));
        _context.Unmap(buffer.Buffer);
        return buffer;
    }

    private ID3D11SamplerState Sampler(Filter filter, TextureAddressMode address) =>
        _device.CreateSamplerState(new SamplerDescription(filter, address, 0f, 1, ComparisonFunction.Never, 0f, float.MaxValue));

    /// <summary>Random colours, smoothed across <paramref name="zoom"/> pixels (1 = pure noise), tiling.</summary>
    private ID3D11ShaderResourceView Noise2D(int size, int zoom, Random random)
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

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var texture = _device.CreateTexture2D(
                Format.R8G8B8A8_UNorm, (uint)size, (uint)size, 1, 1,
                new[] { new SubresourceData(handle.AddrOfPinnedObject(), (uint)(size * 4), 0) },
                BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Immutable, CpuAccessFlags.None);
            _noise.Add(texture);
            var view = _device.CreateShaderResourceView(texture, null);
            _noise.Add(view);
            return view;
        }
        finally
        {
            handle.Free();
        }
    }

    private ID3D11ShaderResourceView Noise3D(int size, int zoom, Random random)
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

        var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try
        {
            var texture = _device.CreateTexture3D(
                Format.R8G8B8A8_UNorm, (uint)size, (uint)size, (uint)size, 1,
                new[] { new SubresourceData(handle.AddrOfPinnedObject(), (uint)(size * 4), (uint)(size * size * 4)) },
                BindFlags.ShaderResource, ResourceOptionFlags.None, ResourceUsage.Immutable, CpuAccessFlags.None);
            _noise.Add(texture);
            var view = _device.CreateShaderResourceView(texture, null);
            _noise.Add(view);
            return view;
        }
        finally
        {
            handle.Free();
        }
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
        _mesh?.Dispose();
        _shapes?.Dispose();
        foreach (var item in _noise)
        {
            item.Dispose();
        }

        foreach (var sampler in _samplers)
        {
            sampler.Dispose();
        }

        _alphaBlend.Dispose();
        _additiveBlend.Dispose();
        _noCulling.Dispose();
        _constants.Dispose();
        _blurConstants.Dispose();
        _drawConstants.Dispose();
        _warpVertices.Dispose();
        _screenVertices.Dispose();
        _drawVertices.Dispose();
        _classicWarp.Dispose();
        _classicComposite.Dispose();
        _draw.Dispose();
        _drawTextured.Dispose();
        _blurAcross.Dispose();
        _blurDown.Dispose();
    }

    /// <summary>A buffer the CPU rewrites every frame and a vertex shader reads as a list of structs.</summary>
    private sealed class DynamicBuffer : IDisposable
    {
        public DynamicBuffer(ID3D11Device device, int capacity, int stride)
        {
            Capacity = capacity;
            Buffer = device.CreateBuffer(new BufferDescription(
                (uint)(capacity * stride), BindFlags.ShaderResource, ResourceUsage.Dynamic,
                CpuAccessFlags.Write, ResourceOptionFlags.BufferStructured, (uint)stride));
            View = device.CreateShaderResourceView(Buffer, new ShaderResourceViewDescription(Buffer, Format.Unknown, 0, (uint)capacity));
        }

        public int Capacity { get; }

        public ID3D11Buffer Buffer { get; }

        public ID3D11ShaderResourceView View { get; }

        public void Dispose()
        {
            View.Dispose();
            Buffer.Dispose();
        }
    }
}
