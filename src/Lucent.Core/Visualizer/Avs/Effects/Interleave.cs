namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Interleave (r_interleave.cpp): covers every other band of rows and columns with a colour, like
/// a grid of blinds, with the band sizes able to jump on beats and glide back.
/// </summary>
[AvsEffect(23)]
public sealed class Interleave : AvsEffect
{
    private int _enabled = 1;
    private int _x = 1;
    private int _y = 1;
    private int _colour;
    private int _blend;
    private int _blendAverage;
    private int _onBeat;
    private int _beatX = 1;
    private int _beatY = 1;
    private int _beatDuration = 4;
    private double _currentX = 1.0;
    private double _currentY = 1.0;
    private bool[] _rowCovered = Array.Empty<bool>();

    public Interleave(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _x = config.ReadInt(_x);
        _y = config.ReadInt(_y);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _onBeat = config.ReadInt(_onBeat);
        _beatX = config.ReadInt(_beatX);
        _beatY = config.ReadInt(_beatY);
        _beatDuration = config.ReadInt(_beatDuration);
        _currentX = _x;
        _currentY = _y;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var glide = (_beatDuration + 512.0 - 64.0) / 512.0;
        _currentX = _currentX * glide + _x * (1.0 - glide);
        _currentY = _currentY * glide + _y * (1.0 - glide);
        if (context.IsBeat && _onBeat != 0)
        {
            _currentX = _beatX;
            _currentY = _beatY;
        }

        // The bands are lines a few pixels wide, so they're drawn as much bigger as Lucent's picture is.
        var scale = context.DotScale;
        var tx = (int)(_currentX * scale);
        var ty = (int)(_currentY * scale);
        if (tx < 0 || ty < 0)
        {
            return 0;
        }

        // Which rows are pure colour (false) and which are banded (true), worked out in order as AVS did.
        if (_rowCovered.Length != h)
        {
            _rowCovered = new bool[h];
        }

        var rows = _rowCovered;
        var ystat = ty == 0;
        var yp = ty > 0 ? h % ty / 2 : 0;
        for (var j = 0; j < h; j++)
        {
            if (ty != 0 && ++yp >= ty)
            {
                ystat = !ystat;
                yp = 0;
            }

            rows[j] = ystat;
        }

        var xos = tx > 0 ? w % tx / 2 : 0;
        var colour = _colour & 0xffffff;
        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
        context.ForEachRow(y =>
        {
            var start = y * w;
            if (!rows[y])
            {
                Paint(framebuffer, start, w, colour, mode);
                return;
            }

            if (tx == 0)
            {
                return;
            }

            // Bands across the row: a coloured stretch, a clear one, and so on, the first one shorter by xos.
            var left = w;
            var at = start;
            var xo = xos;
            var covered = false;
            while (left > 0)
            {
                var length = Math.Min(left, tx - xo);
                xo = 0;
                left -= length;
                if (!covered && length > 0)
                {
                    Paint(framebuffer, at, length, colour, mode);
                }

                at += length;
                covered = !covered;
            }
        });
        return 0;
    }

    private static void Paint(int[] framebuffer, int start, int count, int colour, int mode)
    {
        for (int at = start, end = start + count; at < end; at++)
        {
            framebuffer[at] = mode switch
            {
                1 => AvsBlend.Add(framebuffer[at], colour),
                2 => AvsBlend.Average(framebuffer[at], colour),
                _ => colour,
            };
        }
    }
}
