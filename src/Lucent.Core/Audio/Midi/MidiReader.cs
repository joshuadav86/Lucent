using System.Runtime.InteropServices;
using MeltySynth;
using NAudio.Wave;

namespace Lucent.Audio.Midi;

/// <summary>
/// Plays a MIDI file by synthesizing it with a SoundFont (<see cref="SoundFonts"/>), as stereo floating-point audio at
/// 44.1 kHz. It's an ordinary <see cref="WaveStream"/>, so MIDI files go through the equalizer, visuals, analyzer,
/// gapless playback, the editor and converting like any other music.
/// </summary>
/// <remarks>
/// Seeking starts the synthesizer again and plays every controller and program change up to that point without
/// sounding the notes, so the instruments are the right ones from the first note after it. Notes already held at that
/// point aren't heard (they'd have started before it).
/// </remarks>
public sealed class MidiReader : WaveStream
{
    public const int SampleRate = 44100;

    /// <summary>How long the sound rings on after the last event: reverb, and notes letting go.</summary>
    private const double TailSeconds = 1.5;

    // At most this many frames are made at a time between events (the synthesizer works in blocks of 64 anyway).
    private const int MostFramesAtOnce = 1024;

    private readonly object _gate = new();
    private readonly MidiSong _song;
    private readonly Synthesizer _synth;
    private readonly long _frames;
    private readonly float[] _left = new float[MostFramesAtOnce];
    private readonly float[] _right = new float[MostFramesAtOnce];
    private long _frame;
    private int _next;
    private bool _disposed;

    private MidiReader(MidiSong song, SoundFont soundFont)
    {
        _song = song;
        _synth = new Synthesizer(soundFont, new SynthesizerSettings(SampleRate) { MaximumPolyphony = 128 });
        _frames = (long)Math.Ceiling((song.Length + TailSeconds) * SampleRate);
    }

    /// <summary>Opens a MIDI file with the SoundFont in use. Throws <see cref="SoundFontNeededException"/> when there's none yet.</summary>
    public static MidiReader Open(string path)
    {
        var song = MidiSong.Read(path);
        var soundFont = SoundFonts.Use();
        try
        {
            return new MidiReader(song, soundFont);
        }
        catch
        {
            SoundFonts.Release();
            throw;
        }
    }

    public override WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

    public override long Length => _frames * 8;

    public override long Position
    {
        get
        {
            lock (_gate)
            {
                return _frame * 8;
            }
        }
        set
        {
            lock (_gate)
            {
                SeekTo(Math.Clamp(value / 8, 0, _frames));
            }
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count - count % 8));
        lock (_gate)
        {
            if (_disposed)
            {
                return 0;
            }

            var written = 0;
            var wanted = (int)Math.Min(floats.Length / 2, _frames - _frame);
            while (written < wanted)
            {
                PlayEventsDue();
                var untilEvent = _next < _song.Events.Count ? FrameOf(_song.Events[_next]) - _frame : long.MaxValue;
                var frames = (int)Math.Clamp(Math.Min(untilEvent, wanted - written), 1, MostFramesAtOnce);
                var left = _left.AsSpan(0, frames);
                var right = _right.AsSpan(0, frames);
                _synth.Render(left, right);
                for (var i = 0; i < frames; i++)
                {
                    floats[(written + i) * 2] = left[i];
                    floats[(written + i) * 2 + 1] = right[i];
                }

                written += frames;
                _frame += frames;
            }

            return written * 8;
        }
    }

    private static long FrameOf(MidiSong.Event midi) => (long)(midi.Time * SampleRate);

    // Plays every event that's due by now.
    private void PlayEventsDue()
    {
        var events = _song.Events;
        while (_next < events.Count && FrameOf(events[_next]) <= _frame)
        {
            Play(events[_next++]);
        }
    }

    private void Play(MidiSong.Event midi) => _synth.ProcessMidiMessage(midi.Channel, midi.Command, midi.Data1, midi.Data2);

    private void SeekTo(long frame)
    {
        _synth.Reset();
        _next = 0;
        var events = _song.Events;
        while (_next < events.Count && FrameOf(events[_next]) < frame)
        {
            var midi = events[_next++];
            if (!midi.IsNote)
            {
                Play(midi);
            }
        }

        _frame = frame;
    }

    protected override void Dispose(bool disposing)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _disposed = true;
                SoundFonts.Release();
            }
        }

        base.Dispose(disposing);
    }
}
