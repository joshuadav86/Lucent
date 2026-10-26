namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Misc / Buffer Save (r_stack.cpp): saves the picture into one of the eight shared buffers, or puts a saved one back, in one of several blends, or alternates between the two each frame.</summary>
[AvsEffect(18)]
public sealed class BufferSave : AvsEffect
{
    private int _direction;
    private int _which;
    private int _blend;
    private int _adjustableBlend = 128;
    private int _alternate;

    public BufferSave(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _direction = config.ReadInt(_direction);
        _which = config.ReadInt(_which);
        _blend = config.ReadInt(_blend);
        _adjustableBlend = config.ReadInt(_adjustableBlend);
        _which = Math.Clamp(_which, 0, AvsShared.BufferCount - 1);
    }

    public override void Restart()
    {
        _alternate = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        int w = context.Width, h = context.Height;
        var buffer = Shared.Buffer(_which, w, h, allocate: _direction != 1);
        if (buffer is null)
        {
            return 0;
        }

        // 0 saves, 1 restores; 2 and 3 alternate between them, starting with saving and restoring.
        var direction = _direction < 2 ? _direction : (_direction & 1) ^ _alternate;
        _alternate ^= 1;
        var source = direction == 0 ? framebuffer : buffer;
        var target = direction != 0 ? framebuffer : buffer;
        switch (_blend)
        {
            case 1:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Average(s, t), 0);
                break;
            case 2:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Add(s, t), 0);
                break;
            case 3:
                // Every other pixel, alternating from line to line.
                for (var y = 0; y < h; y++)
                {
                    var row = y * w;
                    for (int x = y & 1, n = 0; n < w / 2; n++, x += 2)
                    {
                        target[row + x] = source[row + x];
                    }
                }

                break;
            case 4:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Subtract(t, s), 0);
                break;
            case 5:
                // Every other line.
                for (var y = 0; y < h / 2 * 2; y += 2)
                {
                    Array.Copy(source, y * w, target, y * w, w);
                }

                break;
            case 6:
                PerPixel(context, target, source, static (t, s, _) => t ^ s, 0);
                break;
            case 7:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Max(t, s), 0);
                break;
            case 8:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Min(t, s), 0);
                break;
            case 9:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Subtract(s, t), 0);
                break;
            case 10:
                PerPixel(context, target, source, static (t, s, _) => AvsBlend.Multiply(s, t), 0);
                break;
            case 11:
                PerPixel(context, target, source, static (t, s, v) => AvsBlend.Adjust(s, t, v), Math.Clamp(_adjustableBlend, 0, 255));
                break;
            default:
                Array.Copy(source, target, w * h);
                break;
        }

        return 0;
    }

    private static void PerPixel(AvsRenderContext context, int[] target, int[] source, Func<int, int, int, int> blend, int amount)
    {
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                target[at] = blend(target[at], source[at], amount);
            }
        });
    }
}
