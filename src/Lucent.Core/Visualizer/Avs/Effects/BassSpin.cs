namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Bass Spin (r_bspin.cpp): two spinners, one per channel, whose arms sweep faster and further with the bass, drawn as lines or filled triangles.</summary>
[AvsEffect(7)]
public sealed class BassSpin : AvsEffect
{
    private readonly int[] _colours = { 0xffffff, 0xffffff };
    private readonly int[,] _lastX = new int[2, 2];
    private readonly int[,] _lastY = new int[2, 2];
    private readonly double[] _angle = { 3.14159, 0.0 };
    private readonly double[] _speed = new double[2];
    private readonly double[] _direction = { -1.0, 1.0 };
    private readonly int[] _points = new int[6];
    private int _enabled = 3;
    private int _mode = 1;
    private int _lastLevel;

    public BassSpin(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colours[0] = config.ReadInt(_colours[0]);
        _colours[1] = config.ReadInt(_colours[1]);
        _mode = config.ReadInt(_mode);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        int w = context.Width, h = context.Height;
        var blend = Shared.LineBlendMode;
        var lineWidth = RenderHelpers.LineWidth(context);
        for (var y = 0; y < 2; y++)
        {
            if ((_enabled & (1 << y)) == 0)
            {
                continue;
            }

            var ss = Math.Min(h / 2, w * 3 / 8);
            double s = ss;
            var cx = y == 0 ? w / 2 - ss / 2 : w / 2 + ss / 2;
            var colour = _colours[y];
            var d = 0;
            for (var x = 0; x < 44; x++)
            {
                d += context.Audio.Spectrum(y, x);
            }

            var a = d * 512 / (_lastLevel + 30 * 256);
            _lastLevel = d;
            if (a > 255)
            {
                a = 255;
            }

            _speed[y] = 0.7 * (Math.Max(a - 104, 12) / 96.0) + 0.3 * _speed[y];
            _angle[y] += 3.14159 / 6.0 * _speed[y] * _direction[y];
            s *= a * 1.0 / 256.0f;
            var yp = (int)(Math.Sin(_angle[y]) * s);
            var xp = (int)(Math.Cos(_angle[y]) * s);
            if (_mode == 0)
            {
                if (_lastX[0, y] != 0 || _lastY[0, y] != 0)
                {
                    AvsDraw.Line(framebuffer, _lastX[0, y], _lastY[0, y], xp + cx, yp + h / 2, w, h, colour, lineWidth, blend);
                }

                _lastX[0, y] = xp + cx;
                _lastY[0, y] = yp + h / 2;
                AvsDraw.Line(framebuffer, cx, h / 2, cx + xp, h / 2 + yp, w, h, colour, lineWidth, blend);
                if (_lastX[1, y] != 0 || _lastY[1, y] != 0)
                {
                    AvsDraw.Line(framebuffer, _lastX[1, y], _lastY[1, y], cx - xp, h / 2 - yp, w, h, colour, lineWidth, blend);
                }

                _lastX[1, y] = cx - xp;
                _lastY[1, y] = h / 2 - yp;
                AvsDraw.Line(framebuffer, cx, h / 2, cx - xp, h / 2 - yp, w, h, colour, lineWidth, blend);
            }
            else if (_mode == 1)
            {
                if (_lastX[0, y] != 0 || _lastY[0, y] != 0)
                {
                    SetPoints(cx, h / 2, _lastX[0, y], _lastY[0, y], xp + cx, yp + h / 2);
                    Triangle(framebuffer, _points, w, h, colour, blend);
                }

                _lastX[0, y] = xp + cx;
                _lastY[0, y] = yp + h / 2;
                if (_lastX[1, y] != 0 || _lastY[1, y] != 0)
                {
                    SetPoints(cx, h / 2, _lastX[1, y], _lastY[1, y], cx - xp, h / 2 - yp);
                    Triangle(framebuffer, _points, w, h, colour, blend);
                }

                _lastX[1, y] = cx - xp;
                _lastY[1, y] = h / 2 - yp;
            }
        }

        return 0;
    }

    private void SetPoints(int x1, int y1, int x2, int y2, int x3, int y3)
    {
        _points[0] = x1;
        _points[1] = y1;
        _points[2] = x2;
        _points[3] = y2;
        _points[4] = x3;
        _points[5] = y3;
    }

    /// <summary>my_triangle: a filled triangle, in 16.16 fixed point, each pixel put on with the line blend mode.</summary>
    private static void Triangle(int[] fb, int[] points, int width, int height, int colour, int blend)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            if (points[1] > points[3])
            {
                (points[0], points[2]) = (points[2], points[0]);
                (points[1], points[3]) = (points[3], points[1]);
            }

            if (points[3] > points[5])
            {
                (points[2], points[4]) = (points[4], points[2]);
                (points[3], points[5]) = (points[5], points[3]);
            }
        }

        int x1, x2, dx1, dx2;
        x1 = x2 = points[0] << 16;
        dx1 = points[1] < points[3] ? ((points[2] - points[0]) << 16) / (points[3] - points[1]) : 0;
        dx2 = points[1] < points[5] ? ((points[4] - points[0]) << 16) / (points[5] - points[1]) : 0;
        var ymax = Math.Min(points[5], height);
        for (var y = points[1]; y < ymax; y++)
        {
            if (y == points[3])
            {
                if (y == points[5])
                {
                    return;
                }

                x1 = points[2] << 16;
                dx1 = ((points[4] - points[2]) << 16) / (points[5] - points[3]);
            }

            if (y >= 0)
            {
                var x = (Math.Min(x1, x2) - 32768) >> 16;
                var xl = ((Math.Max(x1, x2) + 32768) >> 16) - x;
                if (xl < 0)
                {
                    xl = -xl;
                }

                if (xl == 0)
                {
                    xl++;
                }

                // AVS's own clipping here could run past the row's end; this keeps to the row.
                var start = Math.Max(x, 0);
                var end = Math.Min(x + xl, width);
                for (int at = y * width + start, stop = y * width + end; at < stop; at++)
                {
                    fb[at] = AvsBlend.Line(fb[at], colour, blend);
                }
            }

            x1 += dx1;
            x2 += dx2;
        }
    }
}
