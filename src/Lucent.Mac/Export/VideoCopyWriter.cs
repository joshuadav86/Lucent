using Lucent.Mac.Interop;
using Lucent.Editor;
using Lucent.Export;

namespace Lucent.Mac.Export;

/// <summary>
/// Writes the editor's smaller copies of a video (<see cref="VideoCopy"/>) with macOS's encoders, through
/// <see cref="Mp4Writer"/>. Each call drains its own autorelease pool.
/// </summary>
public sealed class VideoCopyWriter : IVideoCopyWriter
{
    private readonly Mp4Writer _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _hevc;

    private VideoCopyWriter(Mp4Writer writer, int width, int height, bool hevc)
    {
        _writer = writer;
        _width = width;
        _height = height;
        _hevc = hevc;
    }

    /// <summary>Starts a copy (see <see cref="VideoCopy.CreateWriter"/>).</summary>
    public static IVideoCopyWriter Create(string path, int width, int height, int frameRate, int videoBitrate,
        int sampleRate, int audioBitrate, bool hevc, bool allowHardware)
    {
        using var pool = AutoreleasePool.Begin();
        try
        {
            var writer = Mp4Writer.Create(path, width, height, frameRate, videoBitrate, sampleRate, audioBitrate, allowHardware, hevc);
            SafeFile.Hide(path);
            return new VideoCopyWriter(writer, width, height, hevc);
        }
        catch (Exception ex) when (hevc && ex is VideoEncoderException or EntryPointNotFoundException)
        {
            SafeFile.Discard(path);
            throw new NotSupportedException("This Mac can't encode HEVC. Pick H.264 instead.");
        }
    }

    public string EncoderNote => _writer.HardwareEncoder
        ? "Encoded by the Mac's video encoder hardware."
        : $"Encoded by macOS's own {(_hevc ? "HEVC" : "H.264")} encoder.";

    public void WriteVideo(nint pixels, int stride, long frame)
    {
        using var pool = AutoreleasePool.Begin();
        _writer.WriteVideo((target, rowBytes) =>
        {
            for (var y = 0; y < _height; y++)
            {
                unsafe
                {
                    Buffer.MemoryCopy((void*)(pixels + y * stride), (void*)(target + y * rowBytes), rowBytes, _width * 4);
                }
            }
        }, frame);
    }

    public void WriteAudio(byte[] samples, int length, long firstSample)
    {
        using var pool = AutoreleasePool.Begin();
        _writer.WriteAudio(samples, length, firstSample);
    }

    public void Finish()
    {
        using var pool = AutoreleasePool.Begin();
        _writer.Finish();
    }

    public void Dispose()
    {
        using var pool = AutoreleasePool.Begin();
        _writer.Dispose();
    }
}
