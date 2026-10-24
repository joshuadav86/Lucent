namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// The apps' --self-test check of the analyzer's loudness meter against EBU Tech 3341 and 3342's test signals:
/// a 1 kHz tone at -23 dBFS reads -23 LUFS, silence is left out of the integrated loudness, a 10 dB step reads a
/// 10 LU range, and a peak between samples is caught. Also checks the capture feeds the meter only while it's listened to.
/// </summary>
public static class LoudnessSelfTest
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

        static string Show(double value) => value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var rate in new[] { 44100, 48000, 96000 })
        {
            var meter = new LoudnessMeter(rate);
            Tone(meter, rate, 1000, -23, 20);
            var reading = meter.Read();
            Check(Math.Abs(reading.Momentary + 23) < 0.1 && Math.Abs(reading.ShortTerm + 23) < 0.1 && Math.Abs(reading.Integrated + 23) < 0.1,
                $"A 1 kHz tone at -23 dBFS read {Show(reading.Momentary)} / {Show(reading.ShortTerm)} / {Show(reading.Integrated)} LUFS "
                + $"(momentary / short-term / integrated) at {rate} Hz instead of -23.");

            // Tech 3341 case 3: -36 then -23 then -36 dBFS reads -23 integrated, as the quiet parts fall below the gate.
            meter.Restart();
            Tone(meter, rate, 1000, -36, 10);
            Tone(meter, rate, 1000, -23, 60);
            Tone(meter, rate, 1000, -36, 10);
            Silence(meter, rate, 10);
            reading = meter.Read();
            Check(Math.Abs(reading.Integrated + 23) < 0.1,
                $"-36, -23 and -36 dBFS then silence read {Show(reading.Integrated)} LUFS integrated at {rate} Hz instead of -23.");

            // Tech 3342 case 1: 20 s at -20 then 20 s at -30 dBFS is a 10 LU range.
            meter.Restart();
            Tone(meter, rate, 1000, -20, 20);
            Tone(meter, rate, 1000, -30, 20);
            reading = meter.Read();
            Check(Math.Abs(reading.Range - 10) < 1, $"A 10 dB step read a range of {Show(reading.Range)} LU at {rate} Hz instead of 10.");
        }

        // A quarter of the sample rate, a quarter turn round, puts every sample at 0.707 of the peak between them.
        var peaks = new LoudnessMeter(48000);
        Tone(peaks, 48000, 12000, -6.02, 1, Math.PI / 4);
        var truePeak = peaks.Read().TruePeak;
        Check(Math.Abs(truePeak + 6.02) < 0.4, $"A tone peaking between samples at -6.02 dBFS read {Show(truePeak)} dBTP instead.");

        var capture = new AudioCapture();
        capture.Reset(48000);
        var block = Enumerable.Range(0, 4800).Select(i => (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / 48000))).ToArray();
        capture.Write(block, block, block);
        Check(double.IsNegativeInfinity(capture.Loudness.Read().TruePeak), "The capture fed the loudness meter while nothing showed it.");
        capture.Loudness.Listen();
        for (var i = 0; i < 5; i++)
        {
            capture.Write(block, block, block);
        }

        Check(capture.Loudness.Read().Momentary > -10, "The capture didn't feed the loudness meter while the analyzer showed it.");
        capture.Reset(44100);
        Check(double.IsNegativeInfinity(capture.Loudness.Read().Momentary) && capture.Loudness.SampleRate == 44100,
            "The loudness meter didn't start again for a new song.");
        capture.Loudness.StopListening();

        if (problems == 0)
        {
            log("The analyzer's loudness meter reads EBU R 128's test tones right.");
        }

        return problems;
    }

    // A tone in both channels, its peak at dbfs.
    private static void Tone(LoudnessMeter meter, int rate, double hertz, double dbfs, double seconds, double phase = 0)
    {
        var amplitude = Math.Pow(10, dbfs / 20);
        var block = new float[rate / 10];
        var at = 0L;
        for (var piece = 0; piece < seconds * 10; piece++)
        {
            for (var i = 0; i < block.Length; i++, at++)
            {
                block[i] = (float)(amplitude * Math.Sin(2 * Math.PI * hertz * at / rate + phase));
            }

            meter.Write(block, block);
        }
    }

    private static void Silence(LoudnessMeter meter, int rate, double seconds)
    {
        var block = new float[rate / 10];
        for (var piece = 0; piece < seconds * 10; piece++)
        {
            meter.Write(block, block);
        }
    }
}
