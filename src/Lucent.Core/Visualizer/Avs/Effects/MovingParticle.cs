namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Render / Moving Particle (r_parts.cpp): a round blob on a spring, pulled toward a new random spot on each beat, and optionally growing on beats.</summary>
[AvsEffect(8)]
public sealed class MovingParticle : AvsEffect
{
    private readonly double[] _target = new double[2];
    private readonly double[] _velocity = new double[2];
    private readonly double[] _position = new double[2];
    private int _enabled = 1;
    private int _colour = 0xffffff;
    private int _maxDistance = 16;
    private int _size = 8;
    private int _beatSize = 8;
    private int _blend = 1;
    private int _currentSize = 8;

    public MovingParticle(AvsShared shared)
        : base(shared)
    {
        Restart();
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _maxDistance = config.ReadInt(_maxDistance);
        _size = config.ReadInt(_size);
        _beatSize = config.ReadInt(_beatSize);
        _blend = config.ReadInt(_blend);
        _currentSize = _size;
    }

    public override void Restart()
    {
        _target[0] = _target[1] = 0.0;
        _velocity[0] = -0.01551;
        _velocity[1] = 0.0;
        _position[0] = -0.6;
        _position[1] = 0.3;
        _currentSize = _size;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if ((_enabled & 1) == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var ss = Math.Min(h / 2, w * 3 / 8);
        if (context.IsBeat)
        {
            _target[0] = (Shared.Random.Next(33) - 16) / 48.0f;
            _target[1] = (Shared.Random.Next(33) - 16) / 48.0f;
        }

        _velocity[0] -= 0.004 * (_position[0] - _target[0]);
        _velocity[1] -= 0.004 * (_position[1] - _target[1]);
        _position[0] += _velocity[0];
        _position[1] += _velocity[1];
        _velocity[0] *= 0.991;
        _velocity[1] *= 0.991;
        var xp = (int)(_position[0] * ss * (_maxDistance / 32.0)) + w / 2;
        var yp = (int)(_position[1] * ss * (_maxDistance / 32.0)) + h / 2;
        if (context.IsBeat && (_enabled & 2) != 0)
        {
            _currentSize = _beatSize;
        }

        var sz = _currentSize;
        _currentSize = (_currentSize + _size) / 2;

        // 0 replace, 2 50/50, 3 the line blend mode, anything else additive.
        var mode = _blend switch
        {
            0 => 0,
            2 => 3,
            3 => Shared.LineBlendMode,
            _ => 1,
        };

        if (sz <= 1)
        {
            AvsDraw.Dot(framebuffer, xp, yp, w, h, _colour, RenderHelpers.DotSize(context), mode);
            return 0;
        }

        if (sz > 128)
        {
            sz = 128;
        }

        sz = Math.Max(1, (int)(sz * context.DotScale));
        var md = sz * sz * 0.25;
        yp -= sz / 2;
        for (var y = 0; y < sz; y++)
        {
            if (yp + y < 0 || yp + y >= h)
            {
                continue;
            }

            var yd = y - sz * 0.5;
            var l = Math.Sqrt(md - yd * yd);
            var xs = (int)(l + 0.99);
            if (xs < 1)
            {
                xs = 1;
            }

            var xe = Math.Min(xp + xs, w);
            var xst = Math.Max(xp - xs, 0);
            var row = (yp + y) * w;
            for (var x = xst; x < xe; x++)
            {
                framebuffer[row + x] = AvsBlend.Line(framebuffer[row + x], _colour, mode);
            }
        }

        return 0;
    }
}
