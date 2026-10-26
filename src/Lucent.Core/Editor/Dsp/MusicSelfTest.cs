namespace Lucent.Editor.Dsp;

/// <summary>
/// Checks tempo, beat, key and loop finding on music made up on the spot, whose tempo, downbeats and key are
/// known: a kick, snare and hi-hats under a chord progression. Both apps run it in their self-tests.
/// </summary>
public static class MusicSelfTest
{
    private const int SampleRate = 44100;

    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        // A tempo that isn't a whole number, music that starts a little in, and a key with sharps.
        failures += Check(log, tempo: 126.5, lead: 0.37, tonic: 4, minor: true);
        failures += Check(log, tempo: 92, lead: 0.05, tonic: 2, minor: false);
        failures += CheckLengths(log);
        return failures;
    }

    private static int Check(Action<string> log, double tempo, double lead, int tonic, bool minor)
    {
        var failures = 0;
        var audio = MakeMusic(tempo, lead, bars: 32, tonic, minor);
        var name = $"{tempo} BPM in {new MusicalKey(tonic, minor, 1).Name}";
        var grid = BeatGrid.Analyze(audio);
        if (grid is null)
        {
            log($"Music: no beat found in {name}.");
            return 1;
        }

        if (Math.Abs(grid.Tempo - tempo) > 0.1)
        {
            log($"Music: {name} measured at {grid.Tempo:0.00} BPM.");
            failures++;
        }

        // Every beat within 3 ms of where it was played, and bar 1 on the first kick.
        var perBeat = 60.0 * SampleRate / tempo;
        var worst = grid.Beats.Max(beat => Math.Abs(beat - lead * SampleRate - Math.Round((beat - lead * SampleRate) / perBeat) * perBeat));
        if (worst > 0.003 * SampleRate)
        {
            log($"Music: in {name}, a beat is {worst / SampleRate * 1000:0.0} ms from where it was played.");
            failures++;
        }

        var bar = grid.BarsAt((int)(lead * SampleRate) + 10, 1, audio.Frames);
        if (bar is not { } first || Math.Abs(first.Start - lead * SampleRate) > 0.003 * SampleRate)
        {
            log($"Music: in {name}, the first bar was put at {(bar?.Start ?? -1) / (double)SampleRate:0.000} s, not {lead:0.000} s.");
            failures++;
        }

        var key = KeyDetector.Analyze(audio);
        if (key is not { } found || found.Tonic != tonic || found.IsMinor != minor)
        {
            log($"Music: {name} was heard as {key?.Name ?? "no key"}.");
            failures++;
        }

        // The music repeats every four bars, so a four-bar loop should be found, on a downbeat, and loop well.
        var loops = LoopFinder.Find(grid, SpectralFrames.Measure(audio), audio.Frames, bars: 4, count: 3);
        if (loops.Count == 0 || grid.Measure(loops[0].Range).Text != "4 bars" || loops[0].Score < 0.8)
        {
            log($"Music: the loops found in {name} were " +
                (loops.Count == 0 ? "none." : string.Join(", ", loops.Select(l => $"{grid.Measure(l.Range).Text} at {l.Score:0.00}"))));
            failures++;
        }

        if (failures == 0)
        {
            log($"Music: {name} measured as {grid.TempoText}, {key}, beats within {worst / SampleRate * 1000:0.0} ms.");
        }

        return failures;
    }

    // Lengths read as bars and beats, as the editor shows them while a loop is stretched.
    private static int CheckLengths(Action<string> log)
    {
        var grid = BeatGrid.Steady(120, 0, SampleRate * 60, SampleRate);
        var beat = SampleRate / 2;
        var cases = new (FrameRange Range, string Text)[]
        {
            (new FrameRange(0, beat * 8), "2 bars"),
            (new FrameRange(beat * 4, beat * 9), "2 bars 1 beat"),
            (new FrameRange(beat, beat * 3), "3 beats"),
            (new FrameRange(0, beat * 4), "1 bar"),
            (new FrameRange(0, beat * 5 + beat / 2), "about 1 bar 1.5 beats"),
        };
        var failures = 0;
        foreach (var (range, text) in cases)
        {
            var measured = grid.Measure(range).Text;
            if (measured != text)
            {
                log($"Music: {range.Length / (double)beat} beats read as \"{measured}\", not \"{text}\".");
                failures++;
            }
        }

        var loop = grid.BarsAt(beat * 9, 2, SampleRate * 60);
        if (loop != new FrameRange(beat * 8, beat * 8) || grid.Move(loop!.Value, 1, SampleRate * 60) != new FrameRange(beat * 16, beat * 8)
            || grid.Resize(loop.Value, 2, SampleRate * 60) != new FrameRange(beat * 8, beat * 16))
        {
            log("Music: making, moving or stretching a loop by bars went wrong.");
            failures++;
        }

        return failures;
    }

    /// <summary>
    /// Drums and chords in a key: a kick on every beat (hardest on the one), a snare on two and four, hi-hats
    /// between, and the key's I, VI, IV, V chords (i, VI, iv, v in minor), one a bar, repeating every four bars.
    /// </summary>
    public static AudioBuffer MakeMusic(double tempo, double lead, int bars, int tonic, bool minor)
    {
        var perBeat = 60.0 / tempo;
        var frames = (int)((lead + bars * 4 * perBeat + 1) * SampleRate);
        var left = new float[frames];
        var random = new Random(7);

        // Chord roots in semitones above the key note, and whether each chord is minor.
        var progression = minor
            ? new[] { (0, true), (8, false), (5, true), (7, true) }
            : new[] { (0, false), (9, true), (5, false), (7, false) };
        for (var b = 0; b < bars; b++)
        {
            var (root, chordMinor) = progression[b % 4];
            var notes = new[] { root, root + (chordMinor ? 3 : 4), root + 7, root + 12 };
            var start = (int)((lead + b * 4 * perBeat) * SampleRate);
            var length = (int)(4 * perBeat * SampleRate);
            foreach (var note in notes)
            {
                // From the C below middle C up, so the chords sit where melodies and pads do.
                var frequency = 130.81 * Math.Pow(2, (tonic + note) / 12.0);
                for (var i = 0; i < length && start + i < frames; i++)
                {
                    var t = (double)i / SampleRate;
                    var envelope = Math.Min(1, i / 400.0) * Math.Min(1, (length - i) / 400.0);
                    left[start + i] += (float)(0.06 * envelope * (Math.Sin(2 * Math.PI * frequency * t) + 0.3 * Math.Sin(4 * Math.PI * frequency * t)));
                }
            }

            for (var beat = 0; beat < 4; beat++)
            {
                var at = (int)((lead + (b * 4 + beat) * perBeat) * SampleRate);
                Kick(left, at, beat == 0 ? 0.9 : 0.6);
                if (beat % 2 == 1)
                {
                    Noise(left, at, 0.35, 0.12, random);
                }

                Noise(left, at + (int)(perBeat / 2 * SampleRate), 0.12, 0.03, random);
            }
        }

        return new AudioBuffer(new[] { left, (float[])left.Clone() }, SampleRate);
    }

    internal static void Kick(float[] samples, int at, double level)
    {
        var phase = 0.0;
        for (var i = 0; i < SampleRate / 4 && at + i < samples.Length; i++)
        {
            var t = (double)i / SampleRate;
            phase += 2 * Math.PI * (50 + 100 * Math.Exp(-t * 30)) / SampleRate;
            samples[at + i] += (float)(level * Math.Exp(-t * 12) * Math.Sin(phase));
        }
    }

    internal static void Noise(float[] samples, int at, double level, double seconds, Random random)
    {
        for (var i = 0; i < seconds * SampleRate && at + i < samples.Length; i++)
        {
            samples[at + i] += (float)(level * Math.Exp(-i / (seconds * SampleRate / 5)) * (random.NextDouble() * 2 - 1));
        }
    }
}
