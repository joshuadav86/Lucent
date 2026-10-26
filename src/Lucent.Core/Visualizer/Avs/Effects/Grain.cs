namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Grain (r_grain.cpp): darkens a random share of the lit pixels by random amounts, like film
/// grain, either fresh each frame or as a fixed ("static") pattern.
/// </summary>
[AvsEffect(24)]
public sealed class Grain : AvsEffect
{
    private int _enabled = 1;
    private int _blend;
    private int _blendAverage;
    private int _amount = 100;
    private int _static;
    private byte[] _pattern = Array.Empty<byte>();
    private int _patternWidth;
    private int _patternHeight;

    public Grain(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _amount = config.ReadInt(_amount);
        _static = config.ReadInt(_static);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (w != _patternWidth || h != _patternHeight)
        {
            // Two bytes per pixel for the static pattern: how much to keep, and whether to grain it at all.
            _pattern = new byte[w * h * 2];
            var random = Shared.Random;
            for (var i = 0; i < _pattern.Length; i += 2)
            {
                _pattern[i] = (byte)random.Next(255);
                _pattern[i + 1] = (byte)random.Next(100);
            }

            _patternWidth = w;
            _patternHeight = h;
        }

        var threshold = _amount * 255 / 100;
        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
        var pattern = _pattern;
        var isStatic = _static != 0;

        // Each row gets its own run of random numbers, so the rows can be done in parallel and the
        // result is the same every time.
        var seed = (uint)Shared.Random.Next();
        context.ForEachRow(y =>
        {
            var state = Mix(seed ^ (uint)(y * 0x9E3779B1));
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var p = framebuffer[at];
                if (p == 0)
                {
                    continue;
                }

                var c = 0;
                var keep = -1;
                if (isStatic)
                {
                    if (pattern[at * 2 + 1] < threshold)
                    {
                        keep = pattern[at * 2];
                    }
                }
                else if (NextByte(ref state) < threshold)
                {
                    keep = NextByte(ref state);
                }

                if (keep >= 0)
                {
                    // As AVS, in 32-bit arithmetic (a very bright red can wrap round).
                    var r = ((p & 0xff0000) * keep) >> 8;
                    c |= (r > 0xff0000 ? 0xff0000 : r) & 0xff0000;
                    r = ((p & 0xff00) * keep) >> 8;
                    c |= (r > 0xff00 ? 0xff00 : r) & 0xff00;
                    r = ((p & 0xff) * keep) >> 8;
                    c |= r > 0xff ? 0xff : r;
                }

                framebuffer[at] = mode switch
                {
                    1 => AvsBlend.Add(p, c),
                    2 => AvsBlend.Average(p, c),
                    _ => c,
                };
            }
        });
        return 0;
    }

    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352d;
        x ^= x >> 15;
        x *= 0x846ca68b;
        x ^= x >> 16;
        return x == 0 ? 0x6d2b79f5u : x;
    }

    private static int NextByte(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (int)(state >> 24);
    }
}
