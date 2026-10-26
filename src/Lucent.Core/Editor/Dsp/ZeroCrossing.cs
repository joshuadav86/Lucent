namespace Lucent.Editor.Dsp;

/// <summary>Finds where the waveform crosses zero, so cuts land where the audio is silent for an instant and don't click.</summary>
public static class ZeroCrossing
{
    /// <summary>How far either side of a frame to look, in seconds: a cycle of a 50 Hz tone.</summary>
    public const double SearchSeconds = 0.01;

    /// <summary>
    /// The frame nearest <paramref name="frame"/> where the channels together cross zero, within
    /// <see cref="SearchSeconds"/>; failing that (a long silence, or a DC offset), the quietest frame nearby.
    /// The very start and end of the audio are left as they are.
    /// </summary>
    public static int Nearest(AudioBuffer audio, int frame)
    {
        if (frame <= 0 || frame >= audio.Frames || audio.Frames < 2)
        {
            return Math.Clamp(frame, 0, audio.Frames);
        }

        var reach = Math.Max(1, (int)(SearchSeconds * audio.SampleRate));
        var from = Math.Max(1, frame - reach);
        var to = Math.Min(audio.Frames - 1, frame + reach);
        var best = -1;
        var bestDistance = int.MaxValue;
        var quietest = frame;
        var quietestLevel = double.MaxValue;
        var previous = Sum(audio, from - 1);
        for (var i = from; i <= to; i++)
        {
            var current = Sum(audio, i);
            var level = Math.Abs(current);
            if (level < quietestLevel || (level == quietestLevel && Math.Abs(i - frame) < Math.Abs(quietest - frame)))
            {
                quietest = i;
                quietestLevel = level;
            }

            if (previous <= 0 && current >= 0 || previous >= 0 && current <= 0)
            {
                // Of the two frames either side of the crossing, the one nearer zero.
                var at = Math.Abs(previous) < level ? i - 1 : i;
                var distance = Math.Abs(at - frame);
                if (distance < bestDistance)
                {
                    best = at;
                    bestDistance = distance;
                }
            }

            previous = current;
        }

        return best >= 0 ? best : quietest;
    }

    private static double Sum(AudioBuffer audio, int frame)
    {
        double sum = 0;
        foreach (var channel in audio.Channels)
        {
            sum += channel[frame];
        }

        return sum;
    }
}
