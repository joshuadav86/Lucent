using System.Diagnostics;
using Lucent.Mac.Interop;
using Lucent.Export;
using static Lucent.Mac.Interop.AVFoundation;
using static Lucent.Mac.Interop.CoreMedia;

namespace Lucent.Mac.Export;

/// <summary>
/// Writes an MP4 file of H.264 (or HEVC) video and AAC audio with the encoders built into macOS (AVFoundation's
/// AVAssetWriter, which encodes the video with VideoToolbox). The Mac's video encoder hardware (Apple
/// silicon's media engine, or an Intel Mac's) is used when there is one, and macOS's software encoder
/// otherwise. Pictures go in as BGRA (VideoToolbox turns them into the encoder's YUV) and sound as 16-bit
/// stereo; everything is timed by frame and sample number, so the file plays back at exactly the rate
/// it was made for however fast it's written. The Mac version of the Windows app's Mp4Writer. Use it on
/// one thread, inside an autorelease pool.
/// </summary>
public sealed unsafe class Mp4Writer : IDisposable
{
    private const uint Bgra = 0x42475241;           // kCVPixelFormatType_32BGRA ('BGRA')
    private const uint Aac = 0x61616320;            // kAudioFormatMPEG4AAC ('aac ')
    private const uint SignedPackedFlags = 0x4 | 0x8; // kAudioFormatFlagIsSignedInteger | kAudioFormatFlagIsPacked

    // How long the encoder may go without taking more before it counts as stuck, and how long finishing may take.
    private static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan FinishTimeout = TimeSpan.FromMinutes(5);

    private readonly int _frameRate;
    private readonly int _sampleRate;
    private IntPtr _writer;
    private IntPtr _videoInput;
    private IntPtr _audioInput;
    private IntPtr _adaptor;
    private IntPtr _audioFormat;
    private long _videoFrames;
    private bool _started;
    private bool _finished;
    private bool _disposed;

    private Mp4Writer(int frameRate, int sampleRate)
    {
        _frameRate = frameRate;
        _sampleRate = sampleRate;
    }

    /// <summary>True when only the Mac's video encoder hardware was allowed to encode.</summary>
    public bool HardwareAllowed { get; private set; }

    /// <summary>True when the hardware is doing the encoding: with only it allowed, macOS can't have picked anything else.</summary>
    public bool HardwareEncoder => HardwareAllowed && _started;

    /// <summary>
    /// Starts an MP4 file at <paramref name="path"/> (written in place, so give it a temporary name that
    /// isn't taken). <paramref name="sampleRate"/> must be 44100 or 48000. With <paramref name="allowHardware"/>
    /// off, or if the hardware encoder won't take these settings, macOS's software encoder is used. With
    /// <paramref name="hevc"/>, the video is HEVC, which every Mac since 2017 encodes.
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
            catch (VideoEncoderException)
            {
                // Some Macs have no hardware encoder, or not for this size or rate: use macOS's own.
                SafeFile.Discard(path);
            }
        }

        return Open(path, width, height, frameRate, videoBitrate, sampleRate, audioBitrate, hardware: false, hevc);
    }

    private static Mp4Writer Open(string path, int width, int height, int frameRate, int videoBitrate,
        int sampleRate, int audioBitrate, bool hardware, bool hevc)
    {
        var writer = new Mp4Writer(frameRate, sampleRate);
        try
        {
            using var pool = AutoreleasePool.Begin();
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
        _writer = NewMp4Writer(Path.GetFullPath(path), out var problem);
        if (_writer == IntPtr.Zero)
        {
            throw new VideoEncoderException($"macOS couldn't start the video file. {problem}");
        }

        // Video: H.264 (or HEVC) at the bitrate asked for, saying which colours the numbers mean, so players
        // show them as they looked on screen.
        var compression = Dictionary(
            (Constant("AVVideoAverageBitRateKey"), Number(videoBitrate)),
            (Constant("AVVideoProfileLevelKey"), Constant(hevc ? "kVTProfileLevel_HEVC_Main_AutoLevel" : "AVVideoProfileLevelH264HighAutoLevel")),
            (Constant("AVVideoExpectedSourceFrameRateKey"), Number(_frameRate)),
            // A key frame every two seconds, so players can jump about the video quickly.
            (Constant("AVVideoMaxKeyFrameIntervalKey"), Number(_frameRate * 2)));
        var colours = Dictionary(
            (Constant("AVVideoColorPrimariesKey"), Constant("AVVideoColorPrimaries_ITU_R_709_2")),
            (Constant("AVVideoTransferFunctionKey"), Constant("AVVideoTransferFunction_ITU_R_709_2")),
            (Constant("AVVideoYCbCrMatrixKey"), Constant("AVVideoYCbCrMatrix_ITU_R_709_2")));
        // Only the hardware encoder, or only the software one, so a failure can be put down to the right one.
        var encoder = hardware
            ? Dictionary((Constant("kVTVideoEncoderSpecification_RequireHardwareAcceleratedVideoEncoder"), Number(true)))
            : Dictionary((Constant("kVTVideoEncoderSpecification_EnableHardwareAcceleratedVideoEncoder"), Number(false)));
        IntPtr VideoSettings(IntPtr choice)
        {
            var settings = Dictionary(
                (Constant("AVVideoCodecKey"), Constant(hevc ? "AVVideoCodecTypeHEVC" : "AVVideoCodecTypeH264")),
                (Constant("AVVideoWidthKey"), Number(width)),
                (Constant("AVVideoHeightKey"), Number(height)),
                (Constant("AVVideoCompressionPropertiesKey"), compression),
                (Constant("AVVideoColorPropertiesKey"), colours));
            if (choice != IntPtr.Zero)
            {
                AddEntry(settings, Constant("AVVideoEncoderSpecificationKey"), choice);
            }

            return settings;
        }

        var videoType = Constant("AVMediaTypeVideo");
        var video = VideoSettings(encoder);
        if (!hardware && !CanApply(_writer, video, videoType))
        {
            // A Mac that won't take a choice of encoder: let macOS pick.
            video = VideoSettings(IntPtr.Zero);
        }

        _videoInput = AddInput(videoType, video, "video");

        // Pictures are handed over in pixel buffers from the adaptor's pool, BGRA as the visuals draw them,
        // in memory the encoder hardware can read directly (IOSurface).
        var pictures = Dictionary(
            (Constant("kCVPixelBufferPixelFormatTypeKey"), Number(Bgra)),
            (Constant("kCVPixelBufferWidthKey"), Number(width)),
            (Constant("kCVPixelBufferHeightKey"), Number(height)),
            (Constant("kCVPixelBufferIOSurfacePropertiesKey"), Dictionary()));
        _adaptor = NewPixelBufferAdaptor(_videoInput, pictures);

        // Sound: AAC in the file, 16-bit stereo handed over.
        var sound = Dictionary(
            (Constant("AVFormatIDKey"), Number(Aac)),
            (Constant("AVSampleRateKey"), Number((double)_sampleRate)),
            (Constant("AVNumberOfChannelsKey"), Number(2)),
            (Constant("AVEncoderBitRateKey"), Number(audioBitrate)));
        _audioInput = AddInput(Constant("AVMediaTypeAudio"), sound, "sound");

        var samples = new CoreAudio.AudioStreamBasicDescription
        {
            SampleRate = _sampleRate,
            FormatId = CoreAudio.LinearPcm,
            FormatFlags = SignedPackedFlags,
            BytesPerPacket = 4,
            FramesPerPacket = 1,
            BytesPerFrame = 4,
            ChannelsPerFrame = 2,
            BitsPerChannel = 16,
        };
        if (CMAudioFormatDescriptionCreate(IntPtr.Zero, &samples, 0, IntPtr.Zero, 0, IntPtr.Zero, IntPtr.Zero, out _audioFormat) != 0)
        {
            throw new VideoEncoderException("macOS couldn't describe the soundtrack's format.");
        }

        if (!ObjC.SendBool(_writer, "startWriting"))
        {
            throw Failure("macOS couldn't start encoding the video.");
        }

        _started = true;
        HardwareAllowed = hardware;
        StartSession(_writer, CMTime.Make(0, _frameRate));
    }

    /// <summary>Adds an input encoding <paramref name="mediaType"/> with <paramref name="settings"/>, checking first that it can.</summary>
    private IntPtr AddInput(IntPtr mediaType, IntPtr settings, string what)
    {
        // Each of these would otherwise throw an Objective-C exception, which can't be caught here.
        if (!CanApply(_writer, settings, mediaType))
        {
            throw new VideoEncoderException($"macOS can't encode the {what} with these settings.");
        }

        var input = NewInput(mediaType, settings);
        // Frames come as fast as they're drawn, not in real time: the writer waits for them as long as it takes.
        ObjC.SendVoid(input, "setExpectsMediaDataInRealTime:", false);
        if (!ObjC.SendBool(_writer, "canAddInput:", input))
        {
            ObjC.Release(input);
            throw new VideoEncoderException($"macOS can't add the {what} to an MP4 file.");
        }

        ObjC.SendVoid(_writer, "addInput:", input);
        return input;
    }

    /// <summary>
    /// Adds picture number <paramref name="frame"/> (counting from 0): <paramref name="fill"/> is handed the
    /// address and row length (in bytes) of a BGRA picture to fill in, top row first.
    /// </summary>
    public void WriteVideo(Action<IntPtr, int> fill, long frame)
    {
        EnsureWriting();
        var pool = ObjC.Send(_adaptor, "pixelBufferPool");
        if (pool == IntPtr.Zero || CVPixelBufferPoolCreatePixelBuffer(IntPtr.Zero, pool, out var picture) != 0 || picture == IntPtr.Zero)
        {
            throw Failure("macOS couldn't make room for a picture.");
        }

        try
        {
            if (CVPixelBufferLockBaseAddress(picture, 0) != 0)
            {
                throw new VideoEncoderException("macOS couldn't open a picture to fill in.");
            }

            try
            {
                fill(CVPixelBufferGetBaseAddress(picture), (int)CVPixelBufferGetBytesPerRow(picture));
            }
            finally
            {
                CVPixelBufferUnlockBaseAddress(picture, 0);
            }

            // The writer keeps its own hold on the picture for as long as it needs it.
            WaitUntilReady(_videoInput);
            if (!AppendPixelBuffer(_adaptor, picture, CMTime.Make(frame, _frameRate)))
            {
                throw Failure("The video encoder stopped taking pictures.");
            }

            _videoFrames = Math.Max(_videoFrames, frame + 1);
        }
        finally
        {
            CVPixelBufferRelease(picture);
        }
    }

    /// <summary>
    /// Adds 16-bit stereo sound (left and right interleaved, <paramref name="length"/> bytes) starting
    /// <paramref name="firstSample"/> samples (per channel) into the video.
    /// </summary>
    public void WriteAudio(byte[] samples, int length, long firstSample)
    {
        var count = length / 4;
        if (count <= 0)
        {
            return;
        }

        EnsureWriting();
        if (CMBlockBufferCreateWithMemoryBlock(IntPtr.Zero, IntPtr.Zero, (nuint)length, IntPtr.Zero, IntPtr.Zero, 0, (nuint)length,
                AssureMemoryNow, out var block) != 0)
        {
            throw new VideoEncoderException("macOS couldn't make room for the sound.");
        }

        IntPtr sample;
        try
        {
            fixed (byte* data = samples)
            {
                if (CMBlockBufferReplaceDataBytes(data, block, 0, (nuint)length) != 0)
                {
                    throw new VideoEncoderException("macOS couldn't take the sound.");
                }
            }

            if (CMAudioSampleBufferCreateReadyWithPacketDescriptions(IntPtr.Zero, block, _audioFormat, count,
                    CMTime.Make(firstSample, _sampleRate), IntPtr.Zero, out sample) != 0)
            {
                throw new VideoEncoderException("macOS couldn't take the sound.");
            }
        }
        finally
        {
            // The sample buffer keeps its own hold on the block.
            CoreAudio.CFRelease(block);
        }

        try
        {
            WaitUntilReady(_audioInput);
            if (!ObjC.SendBool(_audioInput, "appendSampleBuffer:", sample))
            {
                throw Failure("The sound encoder stopped taking sound.");
            }
        }
        finally
        {
            CoreAudio.CFRelease(sample);
        }
    }

    /// <summary>Encodes whatever's still queued and completes the file. Without this, the file is unplayable.</summary>
    public void Finish()
    {
        if (_writer == IntPtr.Zero || !_started || _finished)
        {
            return;
        }

        EnsureWriting();
        _finished = true;
        ObjC.SendVoid(_videoInput, "markAsFinished");
        ObjC.SendVoid(_audioInput, "markAsFinished");
        // The video ends as the last picture does, so the soundtrack's encoder can't make it longer.
        EndSession(_writer, CMTime.Make(_videoFrames, _frameRate));
        FinishWriting(_writer);
        var waited = Stopwatch.StartNew();
        while (Status(_writer) == Writing)
        {
            if (waited.Elapsed > FinishTimeout)
            {
                throw new VideoEncoderException("macOS took too long to finish the video file.");
            }

            Thread.Sleep(10);
        }

        if (Status(_writer) != Completed)
        {
            throw Failure("macOS couldn't finish the video file.");
        }
    }

    /// <summary>
    /// Waits for an input to be ready for more. The encoders work in the background, and hold back from
    /// taking more while they're busy, so frames never pile up in memory. The writer's status is checked
    /// last thing, just before the caller adds to it.
    /// </summary>
    private void WaitUntilReady(IntPtr input)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            EnsureWriting();
            if (ObjC.SendBool(input, "isReadyForMoreMediaData"))
            {
                return;
            }

            if (waited.Elapsed > StallTimeout)
            {
                throw new VideoEncoderException("The video encoder stopped responding.");
            }

            Thread.Sleep(1);
        }
    }

    private void EnsureWriting()
    {
        if (_writer == IntPtr.Zero || !_started || _finished)
        {
            throw new InvalidOperationException("The video file isn't open for writing.");
        }

        // Adding to a writer that has failed throws an Objective-C exception, so check first.
        if (Status(_writer) != Writing)
        {
            throw Failure("The video encoder stopped.");
        }
    }

    /// <summary>An exception saying what went wrong, with macOS's reason if it gave one.</summary>
    private VideoEncoderException Failure(string message)
    {
        var reason = _writer == IntPtr.Zero ? null : Describe(ObjC.Send(_writer, "error"));
        return new VideoEncoderException(reason is null ? message : $"{message} {reason}");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_writer != IntPtr.Zero && Status(_writer) == Writing)
        {
            // Stopped partway: macOS stops encoding (the caller removes the unfinished file).
            ObjC.SendVoid(_writer, "cancelWriting");
        }

        if (_audioFormat != IntPtr.Zero)
        {
            CoreAudio.CFRelease(_audioFormat);
            _audioFormat = IntPtr.Zero;
        }

        ObjC.Release(_adaptor);
        ObjC.Release(_videoInput);
        ObjC.Release(_audioInput);
        ObjC.Release(_writer);
        _adaptor = _videoInput = _audioInput = _writer = IntPtr.Zero;
    }
}

/// <summary>macOS's encoders turned the video down or stopped partway, as the hardware encoder can.</summary>
public sealed class VideoEncoderException : Exception
{
    public VideoEncoderException(string message)
        : base(message)
    {
    }
}
