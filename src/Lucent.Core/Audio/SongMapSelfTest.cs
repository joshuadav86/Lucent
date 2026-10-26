using Lucent.Editor.Dsp;
using Lucent.Visualizer;

namespace Lucent.Audio;

/// <summary>
/// Checks that a song's sections, build-ups and drops are found, on dance music made up on the spot whose
/// arrangement is known: an intro, a verse, a build-up, a drop, a breakdown, another build-up and drop, and
/// an outro. Both apps run it in their self-tests.
/// </summary>
public static class SongMapSelfTest
{
    private const int SampleRate = 44100;
    private const double Tempo = 128;

    // The arrangement, in bars: what plays, and how many bars of it.
    private enum Part { Intro, Verse, Build, Drop, Breakdown, Outro }

    private static readonly (Part Part, int Bars)[] Arrangement =
    {
        (Part.Intro, 8), (Part.Verse, 16), (Part.Build, 8), (Part.Drop, 16),
        (Part.Breakdown, 16), (Part.Build, 8), (Part.Drop, 16), (Part.Outro, 8),
    };

    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        var lead = 0.21;
        var audio = MakeSong(lead);
        var (full, low, _) = BeatMap.MeasureEnergy(audio, SampleRate);
        var map = BeatMap.FromEnergy(full, low, CancellationToken.None);
        if (map is null)
        {
            log("Song map: no beat found in the test song.");
            return 1;
        }

        var failures = 0;
        var bar = 4 * 60.0 / Tempo;
        double StartOf(int part) => lead + Arrangement.Take(part).Sum(p => p.Bars) * bar;
        var expected = Enumerable.Range(0, Arrangement.Length).Where(i => Arrangement[i].Part == Part.Drop).Select(StartOf).ToArray();
        var found = map.Drops.Select(d => d.At).ToArray();
        if (found.Length != expected.Length || found.Zip(expected).Any(pair => Math.Abs(pair.First - pair.Second) > 0.05))
        {
            log($"Song map: drops found at {Times(found)}, not {Times(expected)}. Sections: " +
                string.Join(", ", map.Sections.Select(s => $"{s.Start:0.0}-{s.End:0.0} s at {s.Intensity:0.00}")));
            return failures + 1;
        }

        // Each drop's build-up starts at the build-up's first bar.
        foreach (var drop in map.Drops)
        {
            if (Math.Abs(drop.At - drop.BuildStart - 8 * bar) > 0.05)
            {
                log($"Song map: the build-up to the drop at {drop.At:0.0} s starts at {drop.BuildStart:0.0} s.");
                failures++;
            }
        }

        // The drops are the most intense parts, the breakdown and intro the least.
        var middle = (int part) => map.At(StartOf(part) + Arrangement[part].Bars * bar / 2);
        var dropIntensity = middle(3).Intensity;
        var breakdown = middle(4).Intensity;
        var intro = middle(0).Intensity;
        if (dropIntensity < 0.7 || breakdown > 0.4 || intro > 0.4)
        {
            log($"Song map: intensity {dropIntensity:0.00} in the drop, {breakdown:0.00} in the breakdown and {intro:0.00} in the intro.");
            failures++;
        }

        // Halfway through a build-up, and just after a drop.
        var building = map.At(expected[0] - 4 * bar).BuildUp;
        var dropped = map.At(expected[0] + 0.05).Drop;
        var beat = map.At(expected[0] + 60.0 / Tempo / 4);
        if (Math.Abs(building - 0.5) > 0.05 || dropped < 0.8 || Math.Abs(beat.BeatPhase - 0.25) > 0.05 || Math.Abs(beat.BarPhase - 1 / 16.0) > 0.05)
        {
            log($"Song map: build-up {building:0.00} halfway through, drop {dropped:0.00} as it hits, beat {beat.BeatPhase:0.00} and bar {beat.BarPhase:0.00} a sixteenth in.");
            failures++;
        }

        // Auto, 20 seconds into a visual a few bars before the first drop, cuts to the next one right on the drop.
        var plan = AutoLanding.Plan(map, expected[0] - 4 * bar, waited: 20, interval: 25, lead: 3);
        if (plan is not { OnDrop: true } landing || Math.Abs(landing.At - expected[0]) > 0.05)
        {
            log($"Song map: Auto planned its change for {plan?.At:0.00} s (on a drop: {plan?.OnDrop}), not the drop at {expected[0]:0.00} s.");
            failures++;
        }

        if (failures == 0)
        {
            log($"Song map: {map.Sections.Count} sections, drops at {Times(found)}, as arranged.");
        }

        return failures + CheckLive(log, audio[0], lead, expected);
    }

    /// <summary>
    /// The same song followed live, as radio would be: played into the analyzer 60 frames a second with no map.
    /// The tempo and beats should be picked up, the drops heard as they hit, and the build-ups felt coming.
    /// </summary>
    private static int CheckLive(Action<string> log, float[] samples, double lead, double[] drops)
    {
        const int frameRate = 60;
        var analyzer = new AudioAnalyzer();
        var follower = new SongFollower();
        var perBeat = 60.0 / Tempo;
        var heardDrops = new List<double>();
        var beatErrors = new List<double>();
        var buildUps = new double[drops.Length];
        int downbeats = 0, downbeatsFound = 0;
        for (var frame = 0; ; frame++)
        {
            var time = (double)frame / frameRate;
            var end = (int)(time * SampleRate);
            if (end >= samples.Length)
            {
                break;
            }

            var window = samples.AsSpan(Math.Max(0, end - AudioAnalyzer.WindowSamples), Math.Min(end, AudioAnalyzer.WindowSamples));
            analyzer.Update(1f / frameRate, window, SampleRate);
            follower.Update(null, 1f / frameRate, analyzer);
            if (follower.DropNow)
            {
                heardDrops.Add(time);
            }

            for (var d = 0; d < drops.Length; d++)
            {
                if (time > drops[d] - 2 * 4 * perBeat && time < drops[d])
                {
                    buildUps[d] = Math.Max(buildUps[d], follower.Moment.BuildUp);
                }
            }

            // Later in each drop, once the drop has shown where bars start: the bar starts on the one.
            if (follower.BeatNow && drops.Any(d => time > d + 8 && time < d + 28))
            {
                var played = (long)Math.Round((time - lead) / perBeat);
                if (played % 4 == 0)
                {
                    downbeats++;
                    downbeatsFound += follower.Moment.BarPhase < 0.1 ? 1 : 0;
                }
            }

            // Through the first drop: how far each predicted beat is from where one was played.
            if (time > drops[0] + 4 && time < drops[0] + 28)
            {
                if (follower.BeatNow)
                {
                    var off = (time - lead) / perBeat;
                    beatErrors.Add(Math.Abs(off - Math.Round(off)) * perBeat);
                }
            }
        }

        var failures = 0;
        var missed = drops.Where(d => !heardDrops.Any(h => h >= d && h <= d + 0.5)).ToArray();
        if (missed.Length > 0 || heardDrops.Count != drops.Length)
        {
            log($"Song map live: drops heard at {Times(heardDrops)}, not just after {Times(drops)}.");
            failures++;
        }

        if (beatErrors.Count < 40 || beatErrors.Average() > 0.04)
        {
            log($"Song map live: {beatErrors.Count} beats predicted through a drop, {(beatErrors.Count == 0 ? 0 : beatErrors.Average() * 1000):0} ms off on average.");
            failures++;
        }

        if (downbeats < 15 || downbeatsFound < 0.8 * downbeats)
        {
            log($"Song map live: the bar started on {downbeatsFound} of {downbeats} downbeats in the drops.");
            failures++;
        }

        if (buildUps.Any(b => b < 0.3))
        {
            log($"Song map live: build-ups felt only up to {string.Join(" and ", buildUps.Select(b => b.ToString("0.00")))}.");
            failures++;
        }

        if (failures == 0)
        {
            log($"Song map live: drops heard at {Times(heardDrops)}, beats {beatErrors.Average() * 1000:0} ms off, build-ups up to {string.Join(" and ", buildUps.Select(b => b.ToString("0.00")))}.");
        }

        return failures;
    }

    private static string Times(IEnumerable<double> times) => string.Join(", ", times.Select(t => $"{t:0.00} s"));

    /// <summary>
    /// The arrangement played: hi-hats and a soft pad in the intro, a light kick in the verse, a snare roll that speeds
    /// up and gets louder in each build-up, a hard kick, sub bass and snare in each drop, and just the pad in the breakdown.
    /// </summary>
    private static float[][] MakeSong(double lead)
    {
        var perBeat = 60.0 / Tempo;
        var totalBars = Arrangement.Sum(p => p.Bars);
        var frames = (int)((lead + totalBars * 4 * perBeat + 1) * SampleRate);
        var samples = new float[frames];
        var random = new Random(11);
        var barStart = 0;
        foreach (var (part, bars) in Arrangement)
        {
            for (var b = 0; b < bars; b++)
            {
                var at = (int)((lead + (barStart + b) * 4 * perBeat) * SampleRate);
                var length = (int)(4 * perBeat * SampleRate);
                if (part is not Part.Drop)
                {
                    Tone(samples, at, length, 220, part is Part.Breakdown ? 0.06 : 0.03);
                }

                for (var beat = 0; beat < 4; beat++)
                {
                    var on = at + (int)(beat * perBeat * SampleRate);
                    var off = on + (int)(perBeat / 2 * SampleRate);
                    switch (part)
                    {
                        case Part.Intro or Part.Outro:
                            MusicSelfTest.Noise(samples, off, 0.05, 0.03, random);
                            if (part is Part.Outro)
                            {
                                MusicSelfTest.Kick(samples, on, beat == 0 ? 0.3 : 0.2);
                            }

                            break;
                        case Part.Verse:
                            MusicSelfTest.Kick(samples, on, beat == 0 ? 0.35 : 0.25);
                            MusicSelfTest.Noise(samples, off, 0.06, 0.03, random);
                            break;
                        case Part.Build:
                            // Quarter notes, then eighths, then sixteenths, rising.
                            var hits = b < 4 ? 1 : b < 6 ? 2 : 4;
                            for (var h = 0; h < hits; h++)
                            {
                                MusicSelfTest.Noise(samples, on + (int)(h * perBeat / hits * SampleRate), 0.08 + 0.03 * b, 0.05, random);
                            }

                            break;
                        case Part.Drop:
                            MusicSelfTest.Kick(samples, on, beat == 0 ? 0.9 : 0.7);
                            Tone(samples, off, (int)(perBeat / 2 * SampleRate), 55, 0.35);
                            if (beat % 2 == 1)
                            {
                                MusicSelfTest.Noise(samples, on, 0.3, 0.12, random);
                            }

                            MusicSelfTest.Noise(samples, off, 0.1, 0.03, random);
                            break;
                    }
                }
            }

            barStart += bars;
        }

        return new[] { samples, (float[])samples.Clone() };
    }

    private static void Tone(float[] samples, int at, int length, double frequency, double level)
    {
        for (var i = 0; i < length && at + i < samples.Length; i++)
        {
            var envelope = Math.Min(1, i / 400.0) * Math.Min(1, (length - i) / 400.0);
            samples[at + i] += (float)(level * envelope * Math.Sin(2 * Math.PI * frequency * i / SampleRate));
        }
    }
}
