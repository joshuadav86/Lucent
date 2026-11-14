using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Lucent.Audio.Midi;

/// <summary>
/// A standard MIDI file (.mid, .midi, .kar, or an .rmi with one inside), read into the notes and controller changes a
/// synthesizer plays, each at its time in seconds. Tempo changes are worked in, so the times are the song's own.
/// </summary>
/// <remarks>
/// Format 0 and 1 files have their tracks played together; format 2 (rare) plays them one after another. System
/// exclusive messages are left out: the synthesizer resets itself at the start, and GS and XG extras need a GS or XG
/// synthesizer anyway.
/// </remarks>
public sealed class MidiSong
{
    /// <summary>One channel message: note on or off, controller, program change, pitch bend and so on.</summary>
    public readonly record struct Event(double Time, byte Status, byte Data1, byte Data2)
    {
        public int Channel => Status & 0x0F;

        public int Command => Status & 0xF0;

        /// <summary>A note starting (a note on with a velocity), which a seek leaves out.</summary>
        public bool StartsNote => Command == 0x90 && Data2 > 0;

        /// <summary>A note starting or stopping, rather than something that sets how the channel sounds.</summary>
        public bool IsNote => Command is 0x80 or 0x90 or 0xA0;
    }

    /// <summary>The most events read from one file, so a damaged or hostile one can't fill the memory.</summary>
    private const int MostEvents = 8_000_000;

    // 120 beats a minute until the file says otherwise.
    private const int DefaultMicrosecondsPerBeat = 500_000;

    private MidiSong(IReadOnlyList<Event> events, double length, string? title)
    {
        Events = events;
        Length = length;
        Title = title;
    }

    /// <summary>Every channel message, in time order.</summary>
    public IReadOnlyList<Event> Events { get; }

    /// <summary>How long the song lasts, in seconds, up to its last event.</summary>
    public double Length { get; }

    /// <summary>The name the file gives itself (its first track's name), if any.</summary>
    public string? Title { get; }

    /// <summary>The extensions MIDI files come in.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mid", ".midi", ".kar", ".rmi",
    };

    public static bool IsMidi(string path) => Extensions.Contains(Path.GetExtension(path));

    public static MidiSong Read(string path) => Parse(File.ReadAllBytes(path));

    /// <summary>How long a MIDI file lasts, or zero when it can't be read (for the library, which only needs that).</summary>
    public static TimeSpan LengthOf(string path)
    {
        try
        {
            return TimeSpan.FromSeconds(Read(path).Length);
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }

    public static MidiSong Parse(ReadOnlySpan<byte> file)
    {
        file = Unwrap(file);
        if (file.Length < 14 || !file[..4].SequenceEqual("MThd"u8))
        {
            throw new InvalidDataException("This isn't a MIDI file.");
        }

        var headerLength = (int)BinaryPrimitives.ReadUInt32BigEndian(file[4..]);
        if (headerLength < 6 || 8 + headerLength > file.Length)
        {
            throw new InvalidDataException("This MIDI file's header is damaged.");
        }

        var format = BinaryPrimitives.ReadUInt16BigEndian(file[8..]);
        var trackCount = BinaryPrimitives.ReadUInt16BigEndian(file[10..]);
        var division = BinaryPrimitives.ReadUInt16BigEndian(file[12..]);
        if (division == 0)
        {
            throw new InvalidDataException("This MIDI file doesn't say how fast it goes.");
        }

        var raw = new List<RawEvent>();
        var tempos = new List<(long Tick, int MicrosecondsPerBeat)>();
        string? title = null;
        long end = 0;
        long trackStart = 0;
        var position = 8 + headerLength;
        for (var track = 0; track < trackCount && position + 8 <= file.Length; track++)
        {
            var chunkLength = (long)BinaryPrimitives.ReadUInt32BigEndian(file[(position + 4)..]);
            var bodyStart = position + 8;
            var bodyEnd = (int)Math.Min(file.Length, bodyStart + chunkLength);
            if (!file.Slice(position, 4).SequenceEqual("MTrk"u8))
            {
                // Something else stored between the tracks: skipped, as the standard says.
                position = bodyEnd;
                track--;
                continue;
            }

            // Format 2 holds separate songs, played one after another.
            var offset = format == 2 ? trackStart : 0;
            var trackEnd = ReadTrack(file[bodyStart..bodyEnd], track, offset, raw, tempos, ref title);
            end = Math.Max(end, trackEnd);
            trackStart = trackEnd;
            position = bodyEnd;
        }

        raw.Sort();
        tempos.Sort((a, b) => a.Tick.CompareTo(b.Tick));
        var clock = new TempoMap(division, tempos);
        var events = new Event[raw.Count];
        for (var i = 0; i < raw.Count; i++)
        {
            events[i] = new Event(clock.Seconds(raw[i].Tick), raw[i].Status, raw[i].Data1, raw[i].Data2);
        }

        return new MidiSong(events, clock.Seconds(end), title);
    }

    // An .rmi file is a RIFF file with the MIDI file in its "data" chunk.
    private static ReadOnlySpan<byte> Unwrap(ReadOnlySpan<byte> file)
    {
        if (file.Length < 12 || !file[..4].SequenceEqual("RIFF"u8) || !file.Slice(8, 4).SequenceEqual("RMID"u8))
        {
            return file;
        }

        var position = 12;
        while (position + 8 <= file.Length)
        {
            var length = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(file[(position + 4)..]), (uint)(file.Length - position - 8));
            if (file.Slice(position, 4).SequenceEqual("data"u8))
            {
                return file.Slice(position + 8, length);
            }

            position += 8 + length + (length & 1);
        }

        throw new InvalidDataException("This RMI file has no MIDI music in it.");
    }

    // Reads one track's events, returning the tick of its last one. A track that's cut short keeps what came before.
    private static long ReadTrack(ReadOnlySpan<byte> body, int track, long offset, List<RawEvent> raw,
        List<(long, int)> tempos, ref string? title)
    {
        var tick = offset;
        var at = 0;
        byte running = 0;
        var order = 0;
        while (at < body.Length)
        {
            if (!TryReadNumber(body, ref at, out var delta))
            {
                break;
            }

            tick += delta;
            if (at >= body.Length)
            {
                break;
            }

            var status = body[at];
            if (status < 0x80)
            {
                // Running status: the same kind of message as the one before.
                if (running == 0)
                {
                    break;
                }

                status = running;
            }
            else
            {
                at++;
            }

            if (status == 0xFF)
            {
                if (at >= body.Length)
                {
                    break;
                }

                var type = body[at++];
                if (!TryReadNumber(body, ref at, out var length) || at + length > body.Length)
                {
                    break;
                }

                var data = body.Slice(at, (int)length);
                at += (int)length;
                if (type == 0x51 && data.Length >= 3)
                {
                    tempos.Add((tick, (data[0] << 16) | (data[1] << 8) | data[2]));
                }
                else if (type == 0x03 && track == 0 && title is null && data.Length > 0)
                {
                    var name = Encoding.Latin1.GetString(data).Trim('\0', ' ', '\r', '\n');
                    title = name.Length > 0 ? name : null;
                }
                else if (type == 0x2F)
                {
                    break;
                }

                continue;
            }

            if (status is 0xF0 or 0xF7)
            {
                // System exclusive: skipped (see the remarks above). It cancels running status.
                running = 0;
                if (!TryReadNumber(body, ref at, out var length) || at + length > body.Length)
                {
                    break;
                }

                at += (int)length;
                continue;
            }

            if (status >= 0xF0)
            {
                // A system common or real-time message, which doesn't belong in a file: skipped.
                continue;
            }

            running = status;
            var command = status & 0xF0;
            var size = command is 0xC0 or 0xD0 ? 1 : 2;
            if (at + size > body.Length)
            {
                break;
            }

            var data1 = (byte)(body[at] & 0x7F);
            var data2 = size == 2 ? (byte)(body[at + 1] & 0x7F) : (byte)0;
            at += size;
            if (raw.Count >= MostEvents)
            {
                throw new InvalidDataException("This MIDI file is too big to play.");
            }

            raw.Add(new RawEvent(tick, track, order++, status, data1, data2));
        }

        return tick;
    }

    // A variable-length number: seven bits a byte, the top bit set on every byte but the last.
    private static bool TryReadNumber(ReadOnlySpan<byte> body, ref int at, out long value)
    {
        value = 0;
        for (var i = 0; i < 4; i++)
        {
            if (at >= body.Length)
            {
                return false;
            }

            var b = body[at++];
            value = (value << 7) | (uint)(b & 0x7F);
            if (b < 0x80)
            {
                return true;
            }
        }

        return false;
    }

    // Sorted by time, then by track, then in the order each track gives them, so notes on one tick keep their order.
    private readonly record struct RawEvent(long Tick, int Track, int Order, byte Status, byte Data1, byte Data2)
        : IComparable<RawEvent>
    {
        public int CompareTo(RawEvent other)
        {
            var byTick = Tick.CompareTo(other.Tick);
            if (byTick != 0)
            {
                return byTick;
            }

            var byTrack = Track.CompareTo(other.Track);
            return byTrack != 0 ? byTrack : Order.CompareTo(other.Order);
        }
    }

    // Turns ticks into seconds, with every tempo change worked in.
    private sealed class TempoMap
    {
        private readonly double _secondsPerTickSmpte;
        private readonly int _ticksPerBeat;
        private readonly List<(long Tick, double Seconds, int MicrosecondsPerBeat)> _changes = new();

        public TempoMap(ushort division, List<(long Tick, int MicrosecondsPerBeat)> tempos)
        {
            if ((division & 0x8000) != 0)
            {
                // Frames a second and ticks a frame, for files made to run against timecode.
                var framesPerSecond = -(sbyte)(division >> 8);
                var ticksPerFrame = division & 0xFF;
                _secondsPerTickSmpte = 1.0 / (Math.Max(1, framesPerSecond == 29 ? 29.97 : framesPerSecond) * Math.Max(1, ticksPerFrame));
                return;
            }

            _ticksPerBeat = division;
            _changes.Add((0, 0, DefaultMicrosecondsPerBeat));
            foreach (var (tick, microseconds) in tempos)
            {
                if (microseconds <= 0)
                {
                    continue;
                }

                var seconds = Seconds(tick);
                if (_changes[^1].Tick == tick)
                {
                    _changes[^1] = (tick, seconds, microseconds);
                }
                else
                {
                    _changes.Add((tick, seconds, microseconds));
                }
            }
        }

        public double Seconds(long tick)
        {
            if (_ticksPerBeat == 0)
            {
                return tick * _secondsPerTickSmpte;
            }

            // The last change at or before the tick (a binary search: songs can change tempo thousands of times).
            int low = 0, high = _changes.Count - 1;
            while (low < high)
            {
                var middle = (low + high + 1) / 2;
                if (_changes[middle].Tick <= tick)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var (from, seconds, microsecondsPerBeat) = _changes[low];
            return seconds + (tick - from) * microsecondsPerBeat / 1_000_000.0 / _ticksPerBeat;
        }
    }
}
