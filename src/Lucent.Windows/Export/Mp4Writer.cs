using System.IO;
using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace Lucent.Export;

/// <summary>
/// Writes an MP4 file of H.264 (or HEVC) video and AAC audio with the encoders built into Windows (Media
/// Foundation's sink writer). The graphics card's own video encoder (NVIDIA, AMD or Intel) is used
/// when Windows offers one, and Windows' software encoder otherwise. Pictures go in as NV12 (see
/// <see cref="VideoFrameConverter"/>) and sound as 16-bit stereo; everything is timed by frame and
/// sample number, so the file plays back at exactly the rate it was made for however fast it's written.
/// Use it on one thread.
/// </summary>
public sealed class Mp4Writer : IDisposable
{
    // Media Foundation times are in 100-nanosecond units.
    private const long TicksPerSecond = 10_000_000;

    // Values of Media Foundation enumerations the bindings don't name.
    private const uint ProgressiveVideo = 2;          // MFVideoInterlace_Progressive
    private const uint H264HighProfile = 100;         // eAVEncH264VProfile_High
    private const uint HevcMainProfile = 1;           // eAVEncH265VProfile_Main_420_8
    private const uint TvRange = 2;                   // MFNominalRange_16_235
    private const uint Bt709Matrix = 1;               // MFVideoTransferMatrix_BT709
    private const uint Bt709Primaries = 2;            // MFVideoPrimaries_BT709
    private const uint Bt709Transfer = 5;             // MFVideoTransFunc_709

    private static readonly Guid Nv12 = new("3231564E-0000-0010-8000-00AA00389B71");

    private readonly int _frameRate;
    private readonly int _sampleRate;
    private IMFSinkWriter? _writer;
    private int _videoStream;
    private int _audioStream;
    private bool _started;
    private bool _finished;
    private bool _disposed;

    private Mp4Writer(int frameRate, int sampleRate)
    {
        _frameRate = frameRate;
        _sampleRate = sampleRate;
    }

    /// <summary>True when Windows was allowed to pick the graphics card's encoder (it may still have picked its own).</summary>
    public bool HardwareAllowed { get; private set; }

    /// <summary>True when the graphics card is doing the encoding, as far as Windows says.</summary>
    public bool HardwareEncoder { get; private set; }

    /// <summary>The video encoder's name, when Windows says.</summary>
    public string? EncoderName { get; private set; }

    /// <summary>
    /// Starts an MP4 file at <paramref name="path"/> (written in place, so give it a temporary name).
    /// <paramref name="sampleRate"/> must be 44100 or 48000, the rates Windows' AAC encoder takes, and
    /// <paramref name="audioBitrate"/> 96, 128, 160 or 192 thousand. With <paramref name="allowHardware"/>
    /// off, or if the graphics card's encoder won't take these settings, Windows' software encoder is used.
    /// With <paramref name="hevc"/>, the video is HEVC: Windows' own HEVC encoder comes with the HEVC Video
    /// Extensions, and most graphics cards from the last ten years have one.
    /// </summary>
    public static Mp4Writer Create(string path, int width, int height, int frameRate, int videoBitrate,
        int sampleRate, int audioBitrate, bool allowHardware, bool hevc = false)
    {
        if (allowHardware)
        {
            try
            {
                return Open(path, width, height, frameRate, videoBitrate, sampleRate, audioBitrate, hardware: true, hevc);
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                // Some graphics card encoders turn down a size or rate, or fail to start: use Windows' own.
                SafeFile.Discard(path);
            }
        }

        return Open(path, width, height, frameRate, videoBitrate, sampleRate, audioBitrate, hardware: false, hevc);
    }

    private static Mp4Writer Open(string path, int width, int height, int frameRate, int videoBitrate,
        int sampleRate, int audioBitrate, bool hardware, bool hevc)
    {
        var writer = new Mp4Writer(frameRate, sampleRate);
        // Media Foundation keeps count of starts, so this pairs with the shutdown in Dispose even
        // while the player has it running too.
        MediaFactory.MFStartup().CheckError();
        try
        {
            writer.Begin(path, width, height, videoBitrate, audioBitrate, hardware, hevc);
            return writer;
        }
        catch (Exception)
        {
            writer.Dispose();
            throw;
        }
    }

    private void Begin(string path, int width, int height, int videoBitrate, int audioBitrate, bool hardware, bool hevc)
    {
        using (var attributes = MediaFactory.MFCreateAttributes(2))
        {
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, hardware ? 1u : 0u).CheckError();
            // MP4 whatever the file is called (it's written under a temporary name first).
            attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4).CheckError();
            _writer = MediaFactory.MFCreateSinkWriterFromURL(Path.GetFullPath(path), null!, attributes);
        }

        // Video: what goes in the file, then what we hand over.
        using (var encoded = MediaFactory.MFCreateMediaType())
        {
            SetVideoBasics(encoded, hevc ? VideoFormatGuids.Hevc : VideoFormatGuids.H264, width, height);
            encoded.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)videoBitrate).CheckError();
            encoded.Set(MediaTypeAttributeKeys.Mpeg2Profile, hevc ? HevcMainProfile : H264HighProfile).CheckError();
            _videoStream = _writer.AddStream(encoded);
        }

        using (var pictures = MediaFactory.MFCreateMediaType())
        {
            SetVideoBasics(pictures, Nv12, width, height);
            // Rows top to bottom, packed (a positive stride; uncompressed video defaults to bottom-up otherwise).
            pictures.Set(MediaTypeAttributeKeys.DefaultStride, (uint)width).CheckError();
            _writer.SetInputMediaType(_videoStream, pictures, null!);
        }

        // Sound: AAC in the file, 16-bit stereo handed over.
        using (var encoded = MediaFactory.MFCreateMediaType())
        {
            SetAudioBasics(encoded, AudioFormatGuids.Aac);
            encoded.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(audioBitrate / 8)).CheckError();
            _audioStream = _writer.AddStream(encoded);
        }

        using (var samples = MediaFactory.MFCreateMediaType())
        {
            SetAudioBasics(samples, AudioFormatGuids.Pcm);
            samples.Set(MediaTypeAttributeKeys.AudioBlockAlignment, 4u).CheckError();
            samples.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(_sampleRate * 4)).CheckError();
            _writer.SetInputMediaType(_audioStream, samples, null!);
        }

        _writer.BeginWriting();
        _started = true;
        HardwareAllowed = hardware;
        FindVideoEncoder();
    }

    private void SetVideoBasics(IMFMediaType type, Guid subtype, int width, int height)
    {
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        type.Set(MediaTypeAttributeKeys.InterlaceMode, ProgressiveVideo).CheckError();
        type.Set(MediaTypeAttributeKeys.FrameSize, Pack(width, height)).CheckError();
        type.Set(MediaTypeAttributeKeys.FrameRate, Pack(_frameRate, 1)).CheckError();
        type.Set(MediaTypeAttributeKeys.PixelAspectRatio, Pack(1, 1)).CheckError();
        // Say which colours the numbers mean, so players show them as they looked on screen.
        type.Set(MediaTypeAttributeKeys.VideoNominalRange, TvRange).CheckError();
        type.Set(MediaTypeAttributeKeys.YuvMatrix, Bt709Matrix).CheckError();
        type.Set(MediaTypeAttributeKeys.VideoPrimaries, Bt709Primaries).CheckError();
        type.Set(MediaTypeAttributeKeys.TransferFunction, Bt709Transfer).CheckError();
    }

    private void SetAudioBasics(IMFMediaType type, Guid subtype)
    {
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)_sampleRate).CheckError();
        type.Set(MediaTypeAttributeKeys.AudioNumChannels, 2u).CheckError();
    }

    /// <summary>Two numbers in one attribute, as Media Foundation stores sizes and ratios: the first in the high half.</summary>
    private static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

    /// <summary>Asks the sink writer which video encoder it picked, to tell the user. Only informative, so nothing here can fail the recording.</summary>
    private void FindVideoEncoder()
    {
        using var writerEx = _writer!.QueryInterfaceOrNull<IMFSinkWriterEx>();
        if (writerEx is null)
        {
            return;
        }

        for (var index = 0; index < 8; index++)
        {
            Guid category;
            IMFTransform transform;
            try
            {
                writerEx.GetTransformForStream(_videoStream, index, out category, out transform);
            }
            catch (SharpGen.Runtime.SharpGenException)
            {
                // No more transforms on this stream.
                return;
            }

            using (transform)
            {
                if (category != TransformCategoryGuids.VideoEncoder)
                {
                    continue;
                }

                try
                {
                    using var attributes = transform.Attributes;
                    HardwareEncoder = TryGetString(attributes, TransformAttributeKeys.MftEnumHardwareUrlAttribute) is not null;
                    EncoderName = TryGetString(attributes, TransformAttributeKeys.MftFriendlyNameAttribute);
                }
                catch (SharpGen.Runtime.SharpGenException)
                {
                    // An encoder with no attributes to read: Windows' own ones all have them.
                }

                return;
            }
        }
    }

    private static string? TryGetString(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetString(key);
        }
        catch (SharpGen.Runtime.SharpGenException)
        {
            return null;
        }
    }

    /// <summary>Adds picture number <paramref name="frame"/> (counting from 0), an NV12 picture of <paramref name="length"/> bytes.</summary>
    public void WriteVideo(byte[] picture, int length, long frame)
    {
        var start = frame * TicksPerSecond / _frameRate;
        var end = (frame + 1) * TicksPerSecond / _frameRate;
        Write(_videoStream, picture, length, start, end - start);
    }

    /// <summary>
    /// Adds 16-bit stereo sound (left and right interleaved, <paramref name="length"/> bytes) starting
    /// <paramref name="firstSample"/> samples (per channel) into the video.
    /// </summary>
    public void WriteAudio(byte[] samples, int length, long firstSample)
    {
        var count = length / 4;
        var start = firstSample * TicksPerSecond / _sampleRate;
        var end = (firstSample + count) * TicksPerSecond / _sampleRate;
        Write(_audioStream, samples, length, start, end - start);
    }

    private void Write(int stream, byte[] data, int length, long time, long duration)
    {
        if (_writer is null || !_started || _finished)
        {
            throw new InvalidOperationException("The video file isn't open for writing.");
        }

        if (length <= 0)
        {
            return;
        }

        using var buffer = MediaFactory.MFCreateMemoryBuffer(length);
        buffer.Lock(out var pointer, out _, out _);
        try
        {
            Marshal.Copy(data, 0, pointer, length);
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = length;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        // The sink writer keeps its own reference to the sample for as long as it needs it, and waits
        // here when the encoder falls behind, so frames never pile up in memory.
        _writer.WriteSample(stream, sample);
    }

    /// <summary>Encodes whatever's still queued and completes the file. Without this, the file is unplayable.</summary>
    public void Finish()
    {
        if (_writer is null || !_started || _finished)
        {
            return;
        }

        _finished = true;
        _writer.Finalize();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_writer is { } writer)
        {
            _writer = null;
            writer.Dispose();
        }

        MediaFactory.MFShutdown();
    }
}
