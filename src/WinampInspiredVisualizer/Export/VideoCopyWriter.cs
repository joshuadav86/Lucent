using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Writes the editor's smaller copies of a video (<see cref="VideoCopy"/>) with Windows' encoders, through
/// <see cref="Mp4Writer"/>: the pictures come as BGRA and go in as NV12.
/// </summary>
public sealed class VideoCopyWriter : IVideoCopyWriter
{
    private readonly Mp4Writer _writer;
    private readonly int _width;
    private readonly int _height;
    private readonly bool _hevc;
    private readonly byte[] _picture;

    private VideoCopyWriter(Mp4Writer writer, int width, int height, bool hevc)
    {
        _writer = writer;
        _width = width;
        _height = height;
        _hevc = hevc;
        _picture = new byte[width * height * 3 / 2];
    }

    /// <summary>Starts a copy (see <see cref="VideoCopy.CreateWriter"/>).</summary>
    public static IVideoCopyWriter Create(string path, int width, int height, int frameRate, int videoBitrate,
        int sampleRate, int audioBitrate, bool hevc, bool allowHardware)
    {
        try
        {
            var writer = Mp4Writer.Create(path, width, height, frameRate, videoBitrate, sampleRate, audioBitrate, allowHardware, hevc);
            SafeFile.Hide(path);
            return new VideoCopyWriter(writer, width, height, hevc);
        }
        catch (SharpGen.Runtime.SharpGenException) when (hevc)
        {
            SafeFile.Discard(path);
            throw new NotSupportedException(
                "This PC has no HEVC encoder: neither the graphics card's nor Windows' own (it comes with Microsoft's HEVC Video Extensions). Pick H.264 instead.");
        }
    }

    public string EncoderNote => _writer.HardwareEncoder
        ? $"Encoded by the graphics card{(_writer.EncoderName is { Length: > 0 } name ? $" ({name})" : string.Empty)}."
        : $"Encoded by Windows' own {(_hevc ? "HEVC" : "H.264")} encoder.";

    public void WriteVideo(nint pixels, int stride, long frame)
    {
        VideoCopy.BgraToNv12(pixels, stride, _width, _height, _picture);
        _writer.WriteVideo(_picture, _picture.Length, frame);
    }

    public void WriteAudio(byte[] samples, int length, long firstSample) => _writer.WriteAudio(samples, length, firstSample);

    public void Finish() => _writer.Finish();

    public void Dispose() => _writer.Dispose();
}
