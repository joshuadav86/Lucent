using System.Text;
using Lucent.Mac.Interop;
using NAudio.Wave;
using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Decodes a music file with macOS's own decoders (Extended Audio File Services), as 32-bit float
/// at the file's sample rate and channel count. Covers MP3, AAC/M4A, ALAC, FLAC, WAV and AIFF:
/// everything the Windows app sends to Media Foundation and NAudio's readers.
/// </summary>
public sealed unsafe class MacAudioFileReader : WaveStream
{
    private readonly object _lock = new();
    private readonly long _lengthFrames;
    private IntPtr _file;

    public MacAudioFileReader(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(path));
        IntPtr url;
        fixed (byte* pathBytes = bytes)
        {
            url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, pathBytes, bytes.Length, 0);
        }

        if (url == IntPtr.Zero)
        {
            throw new IOException($"Couldn't open {path}.");
        }

        try
        {
            Check(ExtAudioFileOpenURL(url, out _file), $"open {Path.GetFileName(path)}");
        }
        finally
        {
            CFRelease(url);
        }

        try
        {
            AudioStreamBasicDescription fileFormat;
            var size = (uint)sizeof(AudioStreamBasicDescription);
            Check(ExtAudioFileGetProperty(_file, FileDataFormatProperty, ref size, &fileFormat), "read the file's format");
            var channels = (int)Math.Max(1, fileFormat.ChannelsPerFrame);
            var sampleRate = fileFormat.SampleRate > 0 ? fileFormat.SampleRate : 44100;

            // Ask for float samples at the file's own rate; Core Audio decodes and converts as it reads.
            var clientFormat = AudioStreamBasicDescription.Float(sampleRate, channels);
            Check(ExtAudioFileSetProperty(_file, ClientDataFormatProperty, (uint)sizeof(AudioStreamBasicDescription), &clientFormat),
                "choose the decoded format");

            long frames;
            size = sizeof(long);
            _lengthFrames = ExtAudioFileGetProperty(_file, FileLengthFramesProperty, ref size, &frames) == 0 ? Math.Max(0, frames) : 0;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)Math.Round(sampleRate), channels);
        }
        catch
        {
            ExtAudioFileDispose(_file);
            _file = IntPtr.Zero;
            throw;
        }
    }

    public override WaveFormat WaveFormat { get; }

    public override long Length => _lengthFrames * WaveFormat.BlockAlign;

    public override long Position
    {
        get
        {
            lock (_lock)
            {
                return _file != IntPtr.Zero && ExtAudioFileTell(_file, out var frame) == 0 ? frame * WaveFormat.BlockAlign : 0;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_file != IntPtr.Zero)
                {
                    var frame = Math.Clamp(value / WaveFormat.BlockAlign, 0, Math.Max(0, _lengthFrames));
                    Check(ExtAudioFileSeek(_file, frame), "seek");
                }
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var blockAlign = WaveFormat.BlockAlign;
        var frames = (uint)(count / blockAlign);
        if (frames == 0)
        {
            return 0;
        }

        lock (_lock)
        {
            if (_file == IntPtr.Zero)
            {
                return 0;
            }

            fixed (byte* data = &buffer[offset])
            {
                var list = new AudioBufferList
                {
                    NumberBuffers = 1,
                    Buffer = new AudioBuffer
                    {
                        NumberChannels = (uint)WaveFormat.Channels,
                        DataByteSize = frames * (uint)blockAlign,
                        Data = data,
                    },
                };
                Check(ExtAudioFileRead(_file, ref frames, &list), "decode");
            }

            // Zero frames means the end of the file.
            return (int)frames * blockAlign;
        }
    }

    protected override void Dispose(bool disposing)
    {
        lock (_lock)
        {
            if (_file != IntPtr.Zero)
            {
                ExtAudioFileDispose(_file);
                _file = IntPtr.Zero;
            }
        }

        base.Dispose(disposing);
    }
}
