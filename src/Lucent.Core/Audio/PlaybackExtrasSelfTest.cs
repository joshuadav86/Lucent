using Lucent.Audio.Midi;
using Lucent.Playlist;
using Lucent.Settings;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lucent.Audio;

/// <summary>
/// The apps' --self-test check of the smaller playback features: reading MIDI files (tempo changes, running status,
/// .rmi files), volume leveling's gains, its measuring and its glide from one gain to another, where long files pick
/// up again, and global shortcuts as the settings keep them. Playing a MIDI file with the real SoundFont, and the
/// tracker and game music, is checked with the other formats (<see cref="FormatSelfTest"/>).
/// </summary>
public static class PlaybackExtrasSelfTest
{
    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        var problems = 0;
        void Check(bool good, string problem)
        {
            if (!good)
            {
                problems++;
                log("::error::" + problem);
            }
        }

        static string Show(double value) => value.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture);

        // ---- MIDI ----
        // Format 1: the tempo in the first track doubles after one beat; the notes in the second, with running status.
        var file = Midi(format: 1, ticksPerBeat: 480,
            Track(0, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, /* 120 bpm */ 0x83, 0x60, 0xFF, 0x51, 0x03, 0x03, 0xD0, 0x90, /* 240 bpm at tick 480 */ 0, 0xFF, 0x2F, 0),
            Track(0, 0x90, 60, 100, /* running status */ 0x87, 0x40, 60, 0, 0, 0xFF, 0x2F, 0));
        try
        {
            var song = MidiSong.Parse(file);
            var times = song.Events.Select(midi => midi.Time).ToList();
            Check(song.Events.Count == 2 && Math.Abs(times[0]) < 1e-9 && Math.Abs(times[1] - 0.75) < 1e-6 && Math.Abs(song.Length - 0.75) < 1e-6,
                $"A MIDI file's note off two beats in, with the tempo doubling after the first, read at {string.Join(", ", times.Select(Show))} s (length {Show(song.Length)}) instead of 0.75.");
            Check(song.Events.Count == 2 && song.Events[1].Command == 0x90 && song.Events[1].Data2 == 0 && !song.Events[1].StartsNote,
                "A MIDI file's running-status note off didn't read as a note on with no velocity.");

            var riff = new List<byte>();
            riff.AddRange("RIFF"u8.ToArray());
            riff.AddRange(BitConverter.GetBytes(4 + 8 + file.Length));
            riff.AddRange("RMID"u8.ToArray());
            riff.AddRange("data"u8.ToArray());
            riff.AddRange(BitConverter.GetBytes(file.Length));
            riff.AddRange(file);
            Check(Math.Abs(MidiSong.Parse(riff.ToArray()).Length - 0.75) < 1e-6, "An .rmi file's MIDI music wasn't read from inside it.");
        }
        catch (Exception ex)
        {
            Check(false, $"A MIDI file couldn't be read: {ex.Message}");
        }

        // ---- Volume leveling ----
        Check(Math.Abs(VolumeLeveling.ToFactor(-6, 0.5) - 0.501) < 0.001, $"-6 dB came out as {Show(VolumeLeveling.ToFactor(-6, 0.5))} instead of 0.501.");
        Check(Math.Abs(VolumeLeveling.ToFactor(6, 0.9) - 1 / 0.9) < 0.001, $"+6 dB with a peak of 0.9 came out as {Show(VolumeLeveling.ToFactor(6, 0.9))}, not held to 1.111 so it doesn't clip.");
        Check(VolumeLeveling.ToFactor(6, 1.2) == 1f, "A song that already peaks past full scale was turned up.");
        Check(Math.Abs(VolumeLeveling.ToFactor(30, double.NaN) - 3.981) < 0.001, "A huge gain wasn't held to +12 dB.");

        var rate = 48000;
        var tone = new SignalGenerator(rate, 2) { Frequency = 1000, Gain = Math.Pow(10, -23 / 20.0), Type = SignalGeneratorType.Sin }
            .Take(TimeSpan.FromSeconds(10));
        var measured = VolumeLeveling.Measure(tone, 0, 0);
        Check(measured is { } m && Math.Abs(m.Lufs + 23) < 0.2 && Math.Abs(m.Seconds - 10) < 0.01,
            $"A 1 kHz tone at -23 LUFS measured {Show(measured?.Lufs ?? double.NaN)} LUFS over {Show(measured?.Seconds ?? 0)} s for leveling.");

        // A gain set before a track plays (or in its first moments) is taken at once; a change while it plays glides there.
        var levelled = new LevelledSamples(new SignalGenerator(rate, 1) { Type = SignalGeneratorType.Square, Frequency = 10, Gain = 1 });
        levelled.Target = 0.5f;
        var buffer = new float[rate];
        levelled.Read(buffer, 0, 1);
        Check(Math.Abs(Math.Abs(buffer[0]) - 0.5f) < 1e-6, $"A track's leveling gain set before it played wasn't taken at once (first sample {Show(buffer[0])}).");
        levelled.Read(buffer, 0, rate / 2);
        levelled.Target = 1f;
        levelled.Read(buffer, 0, 100);
        var early = levelled.Applied;
        levelled.Read(buffer, 0, buffer.Length);
        Check(early < 0.6f && levelled.Applied == 1f,
            $"A change of leveling gain while playing jumped or never got there ({Show(early)} after 100 samples, {Show(levelled.Applied)} after a second).");

        // ---- Long files ----
        var points = new List<ResumePoint>();
        var hour = TimeSpan.FromHours(1);
        ResumePoints.Note(points, "mix.flac", TimeSpan.FromMinutes(25), hour);
        Check(ResumePoints.Where(points, "mix.flac", hour) == TimeSpan.FromMinutes(25), "A long file's place wasn't kept.");
        ResumePoints.Note(points, "song.mp3", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4));
        Check(points.Count == 1, "A song shorter than 20 minutes had its place kept.");
        ResumePoints.Note(points, "mix.flac", hour - TimeSpan.FromSeconds(5), hour);
        Check(ResumePoints.Where(points, "mix.flac", hour) is null, "A long file played to its end will start partway next time.");
        for (var i = 0; i < ResumePoints.MostKept + 5; i++)
        {
            ResumePoints.Note(points, $"set {i}.mp3", TimeSpan.FromMinutes(10), hour);
        }

        Check(points.Count == ResumePoints.MostKept && points[0].Path == "set 5.mp3", "Long files' places weren't kept to the newest 100.");

        // ---- Global shortcuts ----
        var parsed = Hotkey.Parse("Control+Alt+P");
        Check(parsed is { IsUsable: true } key && key.ToString() == "Control+Alt+P" && key.Label(mac: false) == "Ctrl+Alt+P" && key.Label(mac: true) == "⌃⌥P",
            $"The shortcut Control+Alt+P read as {parsed?.ToString() ?? "nothing"}.");
        Check(Hotkey.Parse("Shift+P") is { IsUsable: false }, "Shift+P (which only types a P) was taken as a shortcut.");
        Check(Hotkey.Parse("F13") is { IsUsable: true }, "F13 on its own wasn't taken as a shortcut.");
        Check(Hotkey.KeyForWindowsCode(0x50) == "P" && Hotkey.KeyForPhysical("Digit1") == "1" && Hotkey.Parse("Command+Space")?.Info?.MacCode == 0x31,
            "A shortcut key's Windows or Mac code didn't match.");
        var saved = Hotkey.Read(new Dictionary<string, string> { ["PlayPause"] = "Control+Alt+Space", ["Nonsense"] = "Control+X", ["Next"] = "Shift+N" });
        Check(saved.Count == 1 && saved.ContainsKey(HotkeyAction.PlayPause), "The saved shortcuts weren't read back as they were set.");

        log(problems == 0
            ? "MIDI files, volume leveling, long files' places, and global shortcuts check out."
            : $"{problems} problems with MIDI files, volume leveling, long files' places, or global shortcuts.");
        return problems;
    }

    private static byte[] Midi(int format, int ticksPerBeat, params byte[][] tracks)
    {
        var bytes = new List<byte>();
        bytes.AddRange("MThd"u8.ToArray());
        bytes.AddRange(new byte[] { 0, 0, 0, 6, 0, (byte)format, 0, (byte)tracks.Length, (byte)(ticksPerBeat >> 8), (byte)ticksPerBeat });
        foreach (var track in tracks)
        {
            bytes.AddRange("MTrk"u8.ToArray());
            bytes.AddRange(new[] { (byte)(track.Length >> 24), (byte)(track.Length >> 16), (byte)(track.Length >> 8), (byte)track.Length });
            bytes.AddRange(track);
        }

        return bytes.ToArray();
    }

    private static byte[] Track(params byte[] events) => events;
}
