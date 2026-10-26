namespace Lucent.Editor;

/// <summary>The curve a clip's fade or crossfade follows, chosen by right-clicking it, as in REAPER.</summary>
public enum FadeShape
{
    /// <summary>Slow at both ends and quickest in the middle: smooth, and two of them crossfade at an even level.</summary>
    SCurve,
    /// <summary>A straight line.</summary>
    Linear,
    /// <summary>Most of the change at the start (a fade out drops away quickly, a fade in comes up quickly).</summary>
    FastStart,
    /// <summary>Most of the change at the end.</summary>
    SlowStart,
    /// <summary>Keeps the power even across a crossfade between different sounds, so it doesn't dip in the middle.</summary>
    EqualPower,
}

public static class FadeShapes
{
    /// <summary>Every shape, in the order the menu lists them.</summary>
    public static IReadOnlyList<FadeShape> All { get; } =
        new[] { FadeShape.Linear, FadeShape.FastStart, FadeShape.SlowStart, FadeShape.SCurve, FadeShape.EqualPower };

    public static string Title(FadeShape shape) => shape switch
    {
        FadeShape.Linear => "Linear",
        FadeShape.FastStart => "Fast start",
        FadeShape.SlowStart => "Slow start",
        FadeShape.EqualPower => "Equal power",
        _ => "S-curve",
    };

    /// <summary>
    /// A fade's gain partway through it, from <paramref name="t"/> 0 (its start) to 1 (its end): rising from 0 to 1
    /// for a fade in, falling from 1 to 0 for a fade out. A fade out is its fade in played backwards, so the two
    /// halves of a crossfade match.
    /// </summary>
    public static double Gain(double t, bool rising, FadeShape shape)
    {
        t = Math.Clamp(t, 0, 1);
        // A fade out of a shape mirrors its fade in, so "fast start" falls quickly at first too.
        var x = rising ? t : 1 - t;
        var fastStart = rising ? FadeShape.FastStart : FadeShape.SlowStart;
        var slowStart = rising ? FadeShape.SlowStart : FadeShape.FastStart;
        return shape switch
        {
            FadeShape.Linear => x,
            _ when shape == fastStart => 1 - (1 - x) * (1 - x),
            _ when shape == slowStart => x * x,
            FadeShape.EqualPower => Math.Sin(Math.PI / 2 * x),
            _ => 0.5 - 0.5 * Math.Cos(Math.PI * x),
        };
    }
}

/// <summary>A clip's fade (or a crossfade) under the pointer in the waveform, as right-clicking it finds it.</summary>
/// <param name="Clip">The clip it belongs to: for a crossfade, the clip before it, which fades out.</param>
/// <param name="FadeIn">True for a fade in, false for a fade out or crossfade.</param>
public sealed record FadeSpot(int Clip, bool FadeIn, bool Crossfade, FadeShape Shape);
