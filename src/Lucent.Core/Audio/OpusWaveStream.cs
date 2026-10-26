using System.IO;
using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>Decodes Ogg Opus files into 16-bit PCM for NAudio.</summary>
public sealed class OpusWaveStream : WaveStream
{
    private const int SampleRate = 48000;
    private const int Channels = 2;

    private readonly FileStream _file;
    private readonly OpusOggReadStream _ogg;
    private readonly WaveFormat _format = new(SampleRate, 16, Channels);
    private readonly long _length;
    private short[] _pending = Array.Empty<short>();
    private int _pendingOffset;
    private long _position;

    public OpusWaveStream(string path)
    {
        _file = File.OpenRead(path);
        if (!StartsAsOpus(_file))
        {
            // An Ogg file holding something else (FLAC, Speex): the Opus reader would take it and never end.
            _file.Dispose();
            throw new InvalidDataException("This Ogg file doesn't hold Opus sound.");
        }

        _ogg = new OpusOggReadStream(OpusCodecFactory.CreateDecoder(SampleRate, Channels), _file);
        _length = AlignToBlock((long)(_ogg.TotalTime.TotalSeconds * _format.AverageBytesPerSecond));
    }

    public override WaveFormat WaveFormat => _format;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set
        {
            var target = AlignToBlock(Math.Clamp(value, 0, _length));
            _ogg.SeekTo(TimeSpan.FromSeconds((double)target / _format.AverageBytesPerSecond));
            _position = target;
            _pending = Array.Empty<short>();
            _pendingOffset = 0;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var written = 0;
        while (written + 1 < count)
        {
            if (_pendingOffset >= _pending.Length)
            {
                if (!_ogg.HasNextPacket)
                {
                    break;
                }

                _pending = _ogg.DecodeNextPacket() ?? Array.Empty<short>();
                _pendingOffset = 0;
                continue;
            }

            var sample = _pending[_pendingOffset++];
            buffer[offset + written] = (byte)sample;
            buffer[offset + written + 1] = (byte)(sample >> 8);
            written += 2;
        }

        _position += written;
        return written;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _file.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>True when the file's first Ogg page carries an Opus header ("OpusHead").</summary>
    private static bool StartsAsOpus(Stream stream)
    {
        var start = new byte[512];
        var read = stream.Read(start, 0, start.Length);
        stream.Position = 0;
        return read > 36 && start.AsSpan(0, 4).SequenceEqual("OggS"u8) && start.AsSpan(0, read).IndexOf("OpusHead"u8) >= 0;
    }

    private long AlignToBlock(long bytes) => bytes - bytes % _format.BlockAlign;
}
