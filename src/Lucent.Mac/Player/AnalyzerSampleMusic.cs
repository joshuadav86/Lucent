using Lucent.Audio;

namespace Lucent.Mac;

/// <summary>
/// A few bars of made-up music (kick, snare, hats, bass, a wide pad and a lead), for the screenshots
/// to play into an <see cref="AudioCapture"/> so the equalizer's spectrum and the analyzer show something.
/// </summary>
internal static class AnalyzerSampleMusic
{
    private const int SampleRate = 44100;
    private const double Tempo = 120;

    // A minor, F, C, G: one chord a bar, as root frequency and the chord's notes.
    private static readonly (double Root, double[] Chord)[] Bars =
    {
        (55.00, new[] { 220.00, 261.63, 329.63 }),
        (43.65, new[] { 174.61, 220.00, 261.63 }),
        (65.41, new[] { 196.00, 261.63, 329.63 }),
        (49.00, new[] { 196.00, 246.94, 293.66 }),
    };

    private static readonly double[] Lead = { 659.25, 587.33, 523.25, 587.33, 659.25, 783.99, 880.00, 783.99 };

    /// <summary>
    /// Writes <paramref name="seconds"/> of music into <paramref name="capture"/>, a screen frame at a
    /// time, calling <paramref name="frame"/> after each with the frame's length in seconds.
    /// </summary>
    public static void Play(AudioCapture capture, double seconds, Action<double> frame)
    {
        const double frameSeconds = 1 / 60.0;
        capture.Reset(SampleRate);
        var random = new Random(7);
        var perFrame = (int)(SampleRate * frameSeconds);
        var mono = new float[perFrame];
        var left = new float[perFrame];
        var right = new float[perFrame];
        long at = 0;
        for (var time = 0.0; time < seconds; time += frameSeconds)
        {
            for (var i = 0; i < perFrame; i++, at++)
            {
                var (l, r) = Sample(at / (double)SampleRate, random);
                left[i] = l;
                right[i] = r;
                mono[i] = (l + r) / 2;
            }

            capture.Write(mono, left, right);
            frame(frameSeconds);
        }
    }

    private static (float Left, float Right) Sample(double t, Random random)
    {
        var beatLength = 60 / Tempo;
        var beat = t / beatLength;
        var inBeat = (beat - Math.Floor(beat)) * beatLength;
        var beatIndex = (int)Math.Floor(beat);
        var (root, chord) = Bars[(beatIndex / 4) % Bars.Length];

        // Kick on every beat: a falling sine.
        var kickPitch = 45 + 90 * Math.Exp(-inBeat * 30);
        var kick = Math.Sin(2 * Math.PI * kickPitch * inBeat) * Math.Exp(-inBeat * 7) * 0.55;

        // Snare on two and four: noise and a tone.
        var snare = 0.0;
        if (beatIndex % 2 == 1)
        {
            snare = ((random.NextDouble() * 2 - 1) * 0.28 + Math.Sin(2 * Math.PI * 190 * inBeat) * 0.15) * Math.Exp(-inBeat * 16);
        }

        // Hats on the eighths: bright noise, short.
        var inEighth = inBeat % (beatLength / 2);
        var noise = random.NextDouble() * 2 - 1;
        var hat = (noise - _lastNoise) * 0.09 * Math.Exp(-inEighth * 45);
        _lastNoise = noise;

        // Bass: a soft saw on the root, an octave up on the off-beat.
        var bassNote = inBeat < beatLength / 2 ? root : root * 2;
        var bass = 0.0;
        for (var harmonic = 1; harmonic <= 6; harmonic++)
        {
            bass += Math.Sin(2 * Math.PI * bassNote * harmonic * t) / harmonic;
        }

        bass *= 0.16 * (0.6 + 0.4 * Math.Exp(-(inBeat % (beatLength / 2)) * 6));

        // Pad: the chord, a little detuned between left and right, so it's wide.
        double padLeft = 0, padRight = 0;
        foreach (var note in chord)
        {
            for (var harmonic = 1; harmonic <= 4; harmonic++)
            {
                var weight = 0.03 / harmonic;
                padLeft += Math.Sin(2 * Math.PI * note * harmonic * 0.997 * t) * weight;
                padRight += Math.Sin(2 * Math.PI * note * harmonic * 1.003 * t + 1.1) * weight;
            }
        }

        // Lead: an eighth-note line, panned a little left.
        var eighth = (int)Math.Floor(t / (beatLength / 2));
        var leadNote = Lead[eighth % Lead.Length];
        var lead = 0.0;
        for (var harmonic = 1; harmonic <= 5; harmonic++)
        {
            lead += Math.Sin(2 * Math.PI * leadNote * harmonic * t) / (harmonic * harmonic);
        }

        lead *= 0.09 * Math.Exp(-inEighth * 5);

        var centre = kick + snare + bass;
        var leftOut = centre + padLeft + lead * 1.2 + hat * 0.7;
        var rightOut = centre + padRight + lead * 0.8 + hat * 1.3;
        return ((float)Math.Clamp(leftOut, -1, 1), (float)Math.Clamp(rightOut, -1, 1));
    }

    private static double _lastNoise;
}
