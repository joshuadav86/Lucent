using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Turns finished frames into NV12 pictures for the video encoder, on the graphics card
/// (Shaders/VideoEncode.hlsl), and brings them back to the processor. A few frames are kept in
/// flight, so the graphics card is never waited on: a frame is read back only once the next couple
/// have been handed in, by which time it's long done.
/// </summary>
public sealed class VideoFrameConverter : IDisposable
{
    /// <summary>How many frames can be in flight before the oldest has to be read back.</summary>
    public const int Depth = 3;

    private readonly ID3D11Device _device;
    private readonly ID3D11DeviceContext _context;
    private readonly int _width;
    private readonly int _height;
    private readonly ID3D11VertexShader _vertexShader;
    private readonly ID3D11PixelShader _lumaShader;
    private readonly ID3D11PixelShader _chromaShader;
    private readonly ID3D11SamplerState _sampler;
    private readonly ID3D11Texture2D _luma;
    private readonly ID3D11RenderTargetView _lumaView;
    private readonly ID3D11Texture2D _chroma;
    private readonly ID3D11RenderTargetView _chromaView;
    private readonly ID3D11Texture2D[] _lumaCopies = new ID3D11Texture2D[Depth];
    private readonly ID3D11Texture2D[] _chromaCopies = new ID3D11Texture2D[Depth];
    private ID3D11Texture2D? _source;
    private ID3D11ShaderResourceView? _sourceView;
    private int _oldest;
    private int _pending;

    /// <summary>Frames of <paramref name="width"/> x <paramref name="height"/> (both even) drawn with <paramref name="device"/>.</summary>
    public VideoFrameConverter(ID3D11Device device, ID3D11DeviceContext context, int width, int height)
    {
        if (width % 2 != 0 || height % 2 != 0)
        {
            throw new ArgumentException("NV12 pictures need an even width and height.");
        }

        _device = device;
        _context = context;
        _width = width;
        _height = height;

        var cache = new ShaderCache();
        var source = PresetLibrary.ReadShader("VideoEncode.hlsl");
        byte[] Compile(string entryPoint, string profile, bool useCache = true) =>
            cache.GetOrCompile(source, entryPoint, "VideoEncode.hlsl", profile, useCache);
        try
        {
            _vertexShader = device.CreateVertexShader(Compile("VSMain", "vs_5_0"), null);
            _lumaShader = device.CreatePixelShader(Compile("PSLuma", "ps_5_0"), null);
            _chromaShader = device.CreatePixelShader(Compile("PSChroma", "ps_5_0"), null);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            // A damaged cache file: compile them again from scratch.
            _vertexShader?.Dispose();
            _lumaShader?.Dispose();
            _vertexShader = device.CreateVertexShader(Compile("VSMain", "vs_5_0", useCache: false), null);
            _lumaShader = device.CreatePixelShader(Compile("PSLuma", "ps_5_0", useCache: false), null);
            _chromaShader = device.CreatePixelShader(Compile("PSChroma", "ps_5_0", useCache: false), null);
        }

        _sampler = device.CreateSamplerState(SamplerDescription.LinearClamp);

        _luma = CreateTarget(device, Format.R8_UNorm, width, height);
        _lumaView = device.CreateRenderTargetView(_luma, null);
        _chroma = CreateTarget(device, Format.R8G8_UNorm, width / 2, height / 2);
        _chromaView = device.CreateRenderTargetView(_chroma, null);
        for (var i = 0; i < Depth; i++)
        {
            _lumaCopies[i] = CreateCopy(device, Format.R8_UNorm, width, height);
            _chromaCopies[i] = CreateCopy(device, Format.R8G8_UNorm, width / 2, height / 2);
        }
    }

    /// <summary>Bytes in one NV12 picture: a byte of brightness per pixel, then a pair of colour bytes per 2 x 2 pixels.</summary>
    public int FrameBytes => _width * _height * 3 / 2;

    /// <summary>Frames handed in and not read back yet.</summary>
    public int Pending => _pending;

    /// <summary>
    /// Starts converting <paramref name="frame"/> (the window-sized B8G8R8A8 texture the renderer drew).
    /// There must be room: read the oldest frame back first once <see cref="Pending"/> reaches <see cref="Depth"/>.
    /// </summary>
    public void Submit(ID3D11Texture2D frame)
    {
        if (_pending == Depth)
        {
            throw new InvalidOperationException("Read a frame back before handing in another.");
        }

        if (!ReferenceEquals(frame, _source))
        {
            // The renderer draws every frame into the same texture, so its view is made once.
            _sourceView?.Dispose();
            _sourceView = _device.CreateShaderResourceView(frame, null);
            _source = frame;
        }

        var context = _context;
        context.ClearState();
        context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        context.VSSetShader(_vertexShader);
        context.PSSetSampler(0, _sampler);

        context.OMSetRenderTargets(_lumaView, null);
        context.RSSetViewport(0, 0, _width, _height, 0, 1);
        context.PSSetShaderResource(0, _sourceView!);
        context.PSSetShader(_lumaShader);
        context.Draw(3, 0);

        context.OMSetRenderTargets(_chromaView, null);
        context.RSSetViewport(0, 0, _width / 2, _height / 2, 0, 1);
        context.PSSetShader(_chromaShader);
        context.Draw(3, 0);

        // The renderer sets everything it needs each frame, so nothing is left bound for it to trip over.
        context.ClearState();

        var slot = (_oldest + _pending) % Depth;
        context.CopyResource(_lumaCopies[slot], _luma);
        context.CopyResource(_chromaCopies[slot], _chroma);
        context.Flush();
        _pending++;
    }

    /// <summary>Reads the oldest frame in flight back into <paramref name="picture"/> (<see cref="FrameBytes"/> long), waiting for it if need be.</summary>
    public void ReadOldest(byte[] picture)
    {
        if (_pending == 0)
        {
            throw new InvalidOperationException("No frame is in flight.");
        }

        if (picture.Length < FrameBytes)
        {
            throw new ArgumentException("The picture buffer is too small.", nameof(picture));
        }

        var slot = _oldest;
        CopyPlane(_lumaCopies[slot], _height, _width, picture.AsSpan(0, _width * _height));
        CopyPlane(_chromaCopies[slot], _height / 2, _width, picture.AsSpan(_width * _height, _width * _height / 2));
        _oldest = (_oldest + 1) % Depth;
        _pending--;
    }

    public void Dispose()
    {
        _sourceView?.Dispose();
        foreach (var copy in _lumaCopies.Concat(_chromaCopies))
        {
            copy?.Dispose();
        }

        _chromaView.Dispose();
        _chroma.Dispose();
        _lumaView.Dispose();
        _luma.Dispose();
        _sampler.Dispose();
        _chromaShader.Dispose();
        _lumaShader.Dispose();
        _vertexShader.Dispose();
    }

    /// <summary>Copies a read-back plane row by row: the graphics card pads each row, the encoder wants them packed.</summary>
    private void CopyPlane(ID3D11Texture2D copy, int rows, int rowBytes, Span<byte> destination)
    {
        var mapped = _context.Map<byte>(copy, 0, 0, MapMode.Read);
        try
        {
            var pitch = mapped.Length / rows;
            for (var row = 0; row < rows; row++)
            {
                mapped.Slice(row * pitch, rowBytes).CopyTo(destination.Slice(row * rowBytes, rowBytes));
            }
        }
        finally
        {
            _context.Unmap(copy, 0);
        }
    }

    private static ID3D11Texture2D CreateTarget(ID3D11Device device, Format format, int width, int height) =>
        device.CreateTexture2D(
            format, (uint)width, (uint)height, 1, 1, null,
            BindFlags.RenderTarget, ResourceOptionFlags.None, ResourceUsage.Default, CpuAccessFlags.None);

    private static ID3D11Texture2D CreateCopy(ID3D11Device device, Format format, int width, int height) =>
        device.CreateTexture2D(
            format, (uint)width, (uint)height, 1, 1, null,
            BindFlags.None, ResourceOptionFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
}
