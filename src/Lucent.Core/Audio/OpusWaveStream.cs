using System.IO;
using Concentus;
using Concentus.Oggfile;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

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

    private long AlignToBlock(long bytes) => bytes - bytes % _format.BlockAlign;
}
