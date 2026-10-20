using NAudio.Wave;
using NLayer.NAudioSupport;
using SharpJaad.AAC;

namespace WinampInspiredVisualizer.Radio;

/// <summary>
/// Turns a station's stream into audio, a frame at a time, as 32-bit float samples. There's one for MP3 (NLayer)
/// and one for AAC and AAC+ (SharpJaad); both are written in C#, so a station sounds the same on Windows and the Mac.
/// </summary>
internal abstract class RadioDecoder : IDisposable
{
    /// <summary>How far into the stream to look for the first frame: plenty for the junk some streams start with, without reading a stream of something else forever.</summary>
    protected const int SearchLimit = 256 * 1024;

    /// <summary>Room for any frame's samples: HE-AAC's 2,048 per channel, in stereo, as floats.</summary>
    public const int MaxFrameBytes = 2048 * 2 * sizeof(float) * 2;

    /// <summary>The audio it makes: float samples at the stream's rate and channels.</summary>
    public abstract WaveFormat Format { get; }

    /// <summary>The stream's own bitrate in kbps, from its first frame (or 0 when that doesn't say).</summary>
    public abstract int Bitrate { get; }

    /// <summary>"MP3", "AAC" or "AAC+", for showing.</summary>
    public abstract string Codec { get; }

    /// <summary>
    /// Decodes the next frame into <paramref name="pcm"/> (at least <see cref="MaxFrameBytes"/> long) and returns how many
    /// bytes it made: 0 for a frame that was skipped (damaged, or not in the stream's format), -1 when the stream has ended.
    /// </summary>
    public abstract int Next(byte[] pcm);

    /// <summary>
    /// Finds the first frames of <paramref name="codec"/> in <paramref name="audio"/>, checking they really are that,
    /// and returns the decoder ready to go. Throws <see cref="RadioProblem"/> when they aren't there.
    /// </summary>
    public static RadioDecoder Open(IcyStream audio, RadioCodec codec) => codec switch
    {
        RadioCodec.Aac => AacRadioDecoder.Open(audio),
        _ => Mp3RadioDecoder.Open(audio),
    };

    public virtual void Dispose()
    {
    }
}

/// <summary>The kinds of stream Lucent plays.</summary>
public enum RadioCodec
{
    Mp3,

    /// <summary>AAC in ADTS frames, as Shoutcast and Icecast send it: AAC-LC, and HE-AAC (AAC+) v1 and v2.</summary>
    Aac,
}

/// <summary>MP3, decoded by NLayer from NAudio's frames.</summary>
internal sealed class Mp3RadioDecoder : RadioDecoder
{
    private readonly IcyStream _audio;
    private readonly Queue<Mp3Frame> _first;
    private readonly WaveFormat _format;
    private Mp3FrameDecompressor _decompressor;

    private Mp3RadioDecoder(IcyStream audio, Queue<Mp3Frame> first)
    {
        _audio = audio;
        _first = first;
        var frame = first.Peek();
        _decompressor = Decompressor(frame);
        _format = _decompressor.OutputFormat;
        Bitrate = frame.BitRate / 1000;
    }

    public override WaveFormat Format => _format;

    public override int Bitrate { get; }

    public override string Codec => "MP3";

    public static Mp3RadioDecoder Open(IcyStream audio)
    {
        // Two frames in a row that agree, so a chance 0xFFF in the junk before them isn't taken for one.
        audio.Limit = SearchLimit;
        try
        {
            var previous = Mp3Frame.LoadFromStream(audio);
            while (previous is not null)
            {
                var next = Mp3Frame.LoadFromStream(audio);
                if (next is null)
                {
                    break;
                }

                if (next.SampleRate == previous.SampleRate && Channels(next) == Channels(previous)
                    && next.FileOffset == previous.FileOffset + previous.FrameLength)
                {
                    return new Mp3RadioDecoder(audio, new Queue<Mp3Frame>(new[] { previous, next }));
                }

                previous = next;
            }
        }
        catch (EndOfStreamException)
        {
            // Fall through to the problem below.
        }
        finally
        {
            audio.Limit = 0;
        }

        throw new RadioProblem("The station's stream isn't MP3 audio Lucent can play.");
    }

    public override int Next(byte[] pcm)
    {
        var frame = _first.Count > 0 ? _first.Dequeue() : Mp3Frame.LoadFromStream(_audio);
        if (frame is null)
        {
            return -1;
        }

        // The odd stray frame that doesn't fit (a false start in the data, a station switching formats) is skipped.
        if (frame.SampleRate != _format.SampleRate || Channels(frame) != _format.Channels)
        {
            return 0;
        }

        try
        {
            return _decompressor.DecompressFrame(frame, pcm, 0);
        }
        catch (Exception)
        {
            _decompressor.Dispose();
            _decompressor = Decompressor(frame);
            return 0;
        }
    }

    public override void Dispose() => _decompressor.Dispose();

    private static Mp3FrameDecompressor Decompressor(Mp3Frame frame) =>
        new(new Mp3WaveFormat(frame.SampleRate, Channels(frame), frame.FrameLength, frame.BitRate));

    private static int Channels(Mp3Frame frame) => frame.ChannelMode == ChannelMode.Mono ? 1 : 2;
}

/// <summary>
/// AAC in ADTS frames, decoded by SharpJaad. The header says AAC-LC; HE-AAC hides its extra layer (SBR, and PS for
/// v2) inside the frames, so the decoder finds it in the first one, and the sound comes out at twice the header's
/// sample rate (and in stereo, for v2). The first frame is decoded straight away to learn what the sound will be.
/// </summary>
internal sealed class AacRadioDecoder : RadioDecoder
{
    private static readonly int[] SampleRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };

    private readonly IcyStream _audio;
    private readonly Decoder _decoder;
    private readonly SampleBuffer _samples = new();
    private readonly WaveFormat _format;
    private readonly byte[] _header = new byte[9];
    private readonly int _channels;
    private Header _stream;
    private byte[]? _firstPcm;

    private AacRadioDecoder(IcyStream audio, Header first, byte[] firstFrame)
    {
        _audio = audio;
        _stream = first;
        var config = new DecoderConfig();
        config.SetProfile((Profile)first.Profile);
        config.SetSampleFrequency((SampleFrequency)first.RateIndex);
        config.SetChannelConfiguration((ChannelConfiguration)first.Channels);
        _decoder = new Decoder(config);
        _samples.SetBigEndian(false);
        _decoder.DecodeFrame(firstFrame, _samples);
        if (_samples.SampleRate <= 0 || _samples.Channels is <= 0 or > 8)
        {
            throw new RadioProblem("The station's AAC stream is in a form Lucent can't play.");
        }

        _channels = _samples.Channels;
        // Surround is folded down to stereo, which is what the player, the equalizer and the visuals take.
        _format = WaveFormat.CreateIeeeFloatWaveFormat(_samples.SampleRate, Math.Min(_channels, 2));
        Codec = _samples.SampleRate > SampleRates[first.RateIndex] ? "AAC+" : "AAC";
        // ADTS frames carry no bitrate; it's worked out from the frame's size and how long it plays.
        Bitrate = (int)Math.Round(first.Length * 8.0 * SampleRates[first.RateIndex] / 1024 / 1000);
        _firstPcm = new byte[MaxFrameBytes];
        _firstPcm = _firstPcm[..ToFloat(_firstPcm)];
    }

    public override WaveFormat Format => _format;

    public override int Bitrate { get; }

    public override string Codec { get; }

    public static AacRadioDecoder Open(IcyStream audio)
    {
        audio.Limit = SearchLimit;
        try
        {
            // Two frames in a row with the same layout, so a chance 0xFFF isn't taken for a frame.
            var header = new byte[9];
            for (var tries = 0; tries < 64; tries++)
            {
                if (ReadFrame(audio, header, resync: true) is not { } first)
                {
                    break;
                }

                (Header Header, byte[] Frame)? second;
                try
                {
                    second = ReadFrame(audio, header, resync: false);
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (second is not { } next)
                {
                    break;
                }

                if (next.Header.SameStream(first.Header))
                {
                    // The first of the pair was read only to check; the second is where playing starts.
                    return new AacRadioDecoder(audio, next.Header, next.Frame);
                }
            }
        }
        catch (EndOfStreamException)
        {
            // Fall through to the problem below.
        }
        catch (AACException ex)
        {
            throw new RadioProblem("The station's AAC stream couldn't be decoded.", ex);
        }
        finally
        {
            audio.Limit = 0;
        }

        throw new RadioProblem("The station's stream isn't AAC audio Lucent can play.");
    }

    public override int Next(byte[] pcm)
    {
        if (_firstPcm is { } first)
        {
            _firstPcm = null;
            first.CopyTo(pcm, 0);
            return first.Length;
        }

        (Header Header, byte[] Frame)? read;
        try
        {
            read = ReadFrame(_audio, _header, resync: true);
        }
        catch (InvalidDataException)
        {
            return 0;
        }

        if (read is not { } frame)
        {
            return -1;
        }

        if (!frame.Header.SameStream(_stream))
        {
            return 0;
        }

        try
        {
            _decoder.DecodeFrame(frame.Frame, _samples);
        }
        catch (Exception)
        {
            // A damaged frame: skipped, and the next one decodes as usual.
            return 0;
        }

        return _samples.SampleRate == _format.SampleRate && _samples.Channels == _channels ? ToFloat(pcm) : 0;
    }

    // The decoder's 16-bit samples as floats, surround folded down to stereo.
    private int ToFloat(byte[] pcm)
    {
        var data = _samples.Data;
        if (_channels > 2)
        {
            return Downmix(data, pcm);
        }

        var count = Math.Min(data.Length / 2, pcm.Length / sizeof(float));
        for (var i = 0; i < count; i++)
        {
            var sample = (short)(data[i * 2] | (data[i * 2 + 1] << 8));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * sizeof(float)), sample / 32768f);
        }

        return count * sizeof(float);
    }

    /// <summary>
    /// Surround as stereo. AAC puts the centre first, then the front pair, then the surrounds (and, with 7.1, the back
    /// pair), and the low-frequency channel last (for 5.1 and 7.1). The centre and surrounds go into both sides at
    /// -3 dB, the usual fold-down, and the low-frequency channel is left out, as it is on stereo speakers.
    /// </summary>
    private int Downmix(byte[] data, byte[] pcm)
    {
        const float side = 0.7071f;
        var lfe = _channels is 6 or 8;
        var frames = Math.Min(data.Length / 2 / _channels, pcm.Length / sizeof(float) / 2);
        var heard = _channels - (lfe ? 1 : 0);
        // Each side takes its own channel, the centre, and its surrounds (or the one back channel, with 4).
        var extras = heard == 4 ? 1 : (heard - 3) / 2;
        // Divided by the loudest the sum can be, so the folded sound doesn't clip.
        var scale = 1f / (1f + side * (1 + extras));
        for (var frame = 0; frame < frames; frame++)
        {
            var at = frame * _channels;
            float Sample(int channel) => (short)(data[(at + channel) * 2] | (data[(at + channel) * 2 + 1] << 8)) / 32768f;
            var centre = Sample(0) * side;
            var left = Sample(1) + centre;
            var right = Sample(2) + centre;
            if (heard == 4)
            {
                // A single back channel, in both.
                left += Sample(3) * side;
                right += Sample(3) * side;
            }
            else
            {
                for (var channel = 3; channel + 1 < heard; channel += 2)
                {
                    left += Sample(channel) * side;
                    right += Sample(channel + 1) * side;
                }
            }

            BitConverter.TryWriteBytes(pcm.AsSpan(frame * 2 * sizeof(float)), left * scale);
            BitConverter.TryWriteBytes(pcm.AsSpan((frame * 2 + 1) * sizeof(float)), right * scale);
        }

        return frames * 2 * sizeof(float);
    }

    /// <summary>
    /// The next ADTS frame: its header, and the raw frame after it. With <paramref name="resync"/>, skips anything that
    /// isn't a header to find one; without, throws <see cref="InvalidDataException"/> if there isn't one. Null when the stream ends.
    /// </summary>
    private static (Header Header, byte[] Frame)? ReadFrame(Stream audio, byte[] header, bool resync)
    {
        if (!ReadFully(audio, header, 0, 7))
        {
            return null;
        }

        while (Header.Parse(header) is null)
        {
            if (!resync)
            {
                throw new InvalidDataException("Not an ADTS header.");
            }

            // Slide along a byte and look again.
            Buffer.BlockCopy(header, 1, header, 0, 6);
            if (!ReadFully(audio, header, 6, 1))
            {
                return null;
            }
        }

        var found = Header.Parse(header)!.Value;
        var size = found.HeaderSize;
        if (size > 7 && !ReadFully(audio, header, 7, size - 7))
        {
            return null;
        }

        var frame = new byte[found.Length - size];
        return ReadFully(audio, frame, 0, frame.Length) ? (found, frame) : null;
    }

    private static bool ReadFully(Stream audio, byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = audio.Read(buffer, offset, count);
            if (read == 0)
            {
                return false;
            }

            offset += read;
            count -= read;
        }

        return true;
    }

    /// <summary>An ADTS header: the 7 bytes (9 with a checksum) before each AAC frame.</summary>
    private readonly record struct Header(int Profile, int RateIndex, int Channels, int Length, int HeaderSize)
    {
        public static Header? Parse(byte[] bytes)
        {
            // 12 bits of sync, then MPEG version, a layer of 0, and whether there's no checksum.
            if (bytes[0] != 0xFF || (bytes[1] & 0xF6) != 0xF0)
            {
                return null;
            }

            var profile = (bytes[2] >> 6) + 1;
            var rateIndex = (bytes[2] >> 2) & 0x0F;
            var channels = ((bytes[2] & 0x01) << 2) | (bytes[3] >> 6);
            var length = ((bytes[3] & 0x03) << 11) | (bytes[4] << 3) | (bytes[5] >> 5);
            var headerSize = (bytes[1] & 0x01) == 1 ? 7 : 9;
            // Channel layouts 1 to 7 (mono to 7.1); 0, a layout described in the stream itself, isn't supported.
            if (rateIndex >= SampleRates.Length || channels is < 1 or > 7 || length <= headerSize)
            {
                return null;
            }

            return new Header(profile, rateIndex, channels, length, headerSize);
        }

        public bool SameStream(Header other) => Profile == other.Profile && RateIndex == other.RateIndex && Channels == other.Channels;
    }
}
