namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Interferences (r_interf.cpp): adds together several faded copies of the picture, shifted
/// around a circle that turns, giving ghosted echoes (or, with three or six copies, split colour
/// channels). On a beat the copies can spread out and fade back.
/// </summary>
[AvsEffect(41)]
public sealed class Interferences : AvsEffect
{
    private const int MaxPoints = 8;
    private const double Pi = 3.14159;

    private readonly int[] _xPoints = new int[MaxPoints];
    private readonly int[] _yPoints = new int[MaxPoints];
    private int _enabled = 1;
    private int _points = 2;
    private int _rotation;
    private int _distance = 10;
    private int _alpha = 128;
    private int _rotationStep;
    private int _blend;
    private int _blendAverage;
    private int _beatDistance = 32;
    private int _beatAlpha = 192;
    private int _beatRotationStep = 25;
    private int _rgb = 1;
    private int _onBeat = 1;
    private float _speed = 0.2f;
    private float _status = (float)Pi;

    public Interferences(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _points = config.ReadInt(_points);
        _rotation = config.ReadInt(_rotation);
        _distance = config.ReadInt(_distance);
        _alpha = config.ReadInt(_alpha);
        _rotationStep = config.ReadInt(_rotationStep);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _beatDistance = config.ReadInt(_beatDistance);
        _beatAlpha = config.ReadInt(_beatAlpha);
        _beatRotationStep = config.ReadInt(_beatRotationStep);
        _rgb = config.ReadInt(_rgb);
        _onBeat = config.ReadInt(_onBeat);
        _speed = config.ReadFloat(_speed);
        _status = (float)Pi;
    }

    public override void Restart()
    {
        _status = (float)Pi;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var pnts = _points;
        if (_enabled == 0 || pnts == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var angle = (float)(2 * Pi) / pnts;
        if (_onBeat != 0 && context.IsBeat && _status >= Pi)
        {
            _status = 0;
        }

        var s = (float)Math.Sin(_status);
        var rotationStep = _rotationStep + (int)((float)(_beatRotationStep - _rotationStep) * s);
        var alpha = _alpha + (int)((float)(_beatAlpha - _alpha) * s);
        var distance = _distance + (int)((float)(_beatDistance - _distance) * s);
        var a = (float)_rotation / 255 * (float)Pi * 2;

        var count = Math.Clamp(pnts, 0, MaxPoints);
        for (var i = 0; i < count; i++)
        {
            _xPoints[i] = (int)(Math.Cos(a) * distance);
            _yPoints[i] = (int)(Math.Sin(a) * distance);
            a += angle;
        }

        var scaleBase = Math.Clamp(alpha, 0, 255) << 8;
        var table = AvsBlend.Table;
        int[] xPoints = _xPoints, yPoints = _yPoints;
        var split = _rgb != 0 && (pnts == 3 || pnts == 6);
        context.ForEachRow(y =>
        {
            Span<int> rowStart = stackalloc int[MaxPoints];
            for (var i = 0; i < count; i++)
            {
                var sy = y - yPoints[i];
                rowStart[i] = sy >= 0 && sy < h ? sy * w : -1;
            }

            for (int x = 0, at = y * w; x < w; x++, at++)
            {
                int r = 0, g = 0, b = 0;
                for (var i = 0; i < count; i++)
                {
                    var sx = x - xPoints[i];
                    if (sx < 0 || sx >= w || rowStart[i] < 0)
                    {
                        continue;
                    }

                    var pix = framebuffer[rowStart[i] + sx];
                    if (split)
                    {
                        // One channel from each copy: blue byte from the first, green from the second, red from the third.
                        switch (i % 3)
                        {
                            case 0:
                                r += table[scaleBase | (pix & 0xff)];
                                break;
                            case 1:
                                g += table[scaleBase | ((pix >> 8) & 0xff)];
                                break;
                            default:
                                b += table[scaleBase | ((pix >> 16) & 0xff)];
                                break;
                        }
                    }
                    else
                    {
                        r += table[scaleBase | (pix & 0xff)];
                        g += table[scaleBase | ((pix >> 8) & 0xff)];
                        b += table[scaleBase | ((pix >> 16) & 0xff)];
                    }
                }

                fbout[at] = Math.Min(r, 255) | (Math.Min(g, 255) << 8) | (Math.Min(b, 255) << 16);
            }
        });

        _rotation += rotationStep;
        _rotation = _rotation > 255 ? _rotation - 255 : _rotation;
        _rotation = _rotation < -255 ? _rotation + 255 : _rotation;
        _status += _speed;
        _status = Math.Min(_status, (float)Pi);
        if (_status < -Pi)
        {
            _status = (float)Pi;
        }

        if (_blend == 0 && _blendAverage == 0)
        {
            return Swapped;
        }

        var average = _blendAverage != 0;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                framebuffer[at] = average ? AvsBlend.Average(framebuffer[at], fbout[at]) : AvsBlend.Add(framebuffer[at], fbout[at]);
            }
        });
        return 0;
    }
}
