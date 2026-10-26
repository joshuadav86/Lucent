namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Mirror (r_mirror.cpp): copies one half of the picture onto the other, flipped, top to bottom,
/// bottom to top, left to right and/or right to left; optionally picking at random on each beat, and
/// fading between mirrorings smoothly.
/// </summary>
[AvsEffect(26)]
public sealed class Mirror : AvsEffect
{
    private const int TopToBottom = 1;
    private const int BottomToTop = 2;
    private const int LeftToRight = 4;
    private const int RightToLeft = 8;

    private int _enabled = 1;
    private int _mode = TopToBottom;
    private int _onBeat;
    private int _smooth;
    private int _slower = 4;
    private int _beatMode;
    private int _frameCount;

    // AVS's smooth-transition state, one byte per direction (bits 0-7 top to bottom, 8-15 bottom to top,
    // 16-23 left to right, 24-31 right to left): how far each fade has got (1-16, 0 when done), and the step.
    private int _lastMode;
    private int _divisors;
    private int _increments;

    public Mirror(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _mode = config.ReadInt(_mode);
        _onBeat = config.ReadInt(_onBeat);
        _smooth = config.ReadInt(_smooth);
        _slower = config.ReadInt(_slower);
    }

    public override void Restart()
    {
        _lastMode = 0;
        _divisors = 0;
        _increments = 0;
        _frameCount = 0;
    }

    // BLEND_ADAPT: (16 - divisor)/16 of a and divisor/16 of b, at 4 bits a channel.
    private static int Adapt(int a, int b, int divisor) =>
        ((a >> 4) & 0x0f0f0f) * (16 - divisor) + ((b >> 4) & 0x0f0f0f) * divisor;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var halfw = w / 2;
        var halfh = h / 2;
        var mode = _mode;
        if (_onBeat != 0)
        {
            if (context.IsBeat)
            {
                _beatMode = Shared.Random.Next(16) & _mode;
            }

            mode = _beatMode;
        }

        if (mode != _lastMode)
        {
            var changed = mode ^ _lastMode;
            for (int bit = 1, mask = 0xff, shift = 0; bit < 16; bit <<= 1, mask <<= 8, shift += 8)
            {
                if ((changed & bit) != 0)
                {
                    var wasOn = (_lastMode & bit) != 0;
                    _increments = (_increments & ~mask) | ((wasOn ? 0xff : 1) << shift);
                    if ((_divisors & mask) == 0)
                    {
                        _divisors = (_divisors & ~mask) | ((wasOn ? 16 : 1) << shift);
                    }
                }
            }

            _lastMode = mode;
        }

        var smooth = _smooth != 0;
        var fb = framebuffer;
        if ((mode & LeftToRight) != 0 || (smooth && (_divisors & 0x00ff0000) != 0))
        {
            var divisor = (_divisors >> 16) & 0xff;
            var blend = smooth && divisor != 0;
            context.ForEachRow(y =>
            {
                var left = y * w;
                var right = left + w - 1;
                for (var n = 0; n < halfw; n++, left++, right--)
                {
                    fb[right] = blend ? Adapt(fb[right], fb[left], divisor) : fb[left];
                }
            });
        }

        if ((mode & RightToLeft) != 0 || (smooth && ((uint)_divisors & 0xff000000) != 0))
        {
            var divisor = (_divisors >> 24) & 0xff;
            var blend = smooth && divisor != 0;
            context.ForEachRow(y =>
            {
                var left = y * w;
                var right = left + w - 1;
                for (var n = 0; n < halfw; n++, left++, right--)
                {
                    fb[left] = blend ? Adapt(fb[left], fb[right], divisor) : fb[right];
                }
            });
        }

        if ((mode & TopToBottom) != 0 || (smooth && (_divisors & 0xff) != 0))
        {
            var divisor = _divisors & 0xff;
            var blend = smooth && divisor != 0;
            context.ForEachRow(0, halfh, y =>
            {
                var top = y * w;
                var bottom = (h - 1 - y) * w;
                if (!blend)
                {
                    Array.Copy(fb, top, fb, bottom, w);
                    return;
                }

                for (var x = 0; x < w; x++)
                {
                    fb[bottom + x] = Adapt(fb[bottom + x], fb[top + x], divisor);
                }
            });
        }

        if ((mode & BottomToTop) != 0 || (smooth && (_divisors & 0xff00) != 0))
        {
            var divisor = (_divisors >> 8) & 0xff;
            var blend = smooth && divisor != 0;
            context.ForEachRow(0, halfh, y =>
            {
                var top = y * w;
                var bottom = (h - 1 - y) * w;
                if (!blend)
                {
                    Array.Copy(fb, bottom, fb, top, w);
                    return;
                }

                for (var x = 0; x < w; x++)
                {
                    fb[top + x] = Adapt(fb[top + x], fb[bottom + x], divisor);
                }
            });
        }

        if (smooth && ++_frameCount % Math.Max(1, _slower) == 0)
        {
            for (int mask = 0xff, shift = 0; shift < 32; mask <<= 8, shift += 8)
            {
                if ((_divisors & mask) != 0)
                {
                    var step = (((_divisors >> shift) & 0xff) + ((_increments >> shift) & 0xff)) % 16;
                    _divisors = (_divisors & ~mask) | (step << shift);
                }
            }
        }

        return 0;
    }
}
