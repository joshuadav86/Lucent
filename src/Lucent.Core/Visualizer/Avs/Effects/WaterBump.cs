namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Water Bump (r_waterbump.cpp): drops fall into an imaginary pool on the beat, and the picture is
/// bent by the ripples they make as they spread and fade.
/// </summary>
[AvsEffect(31)]
public sealed class WaterBump : AvsEffect
{
    private int _enabled = 1;
    private int _density = 6;
    private int _depth = 600;
    private int _randomDrop;
    private int _dropX = 1;
    private int _dropY = 1;
    private int _dropRadius = 40;

    // The pool's heights, this frame's and last frame's, swapped each frame.
    private readonly int[][] _heights = { Array.Empty<int>(), Array.Empty<int>() };
    private int _page;

    public WaterBump(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _density = config.ReadInt(_density);
        _depth = config.ReadInt(_depth);
        _randomDrop = config.ReadInt(_randomDrop);
        _dropX = config.ReadInt(_dropX);
        _dropY = config.ReadInt(_dropY);
        _dropRadius = config.ReadInt(_dropRadius);

        // A "method" setting follows, but AVS only ever had the one way of moving the ripples.
    }

    public override void Restart()
    {
        Array.Clear(_heights[0]);
        Array.Clear(_heights[1]);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_heights[0].Length != w * h)
        {
            _heights[0] = new int[w * h];
            _heights[1] = new int[w * h];
        }

        if (w < 3 || h < 3)
        {
            return 0;
        }

        if (context.IsBeat)
        {
            if (_randomDrop != 0)
            {
                SineBlob(-1, -1, _dropRadius * Math.Max(w, h) / 100, -_depth, _page, w, h);
            }
            else
            {
                var x = _dropX switch
                {
                    0 => w / 4,
                    2 => w * 3 / 4,
                    _ => w / 2,
                };
                var y = _dropY switch
                {
                    0 => h / 4,
                    2 => h * 3 / 4,
                    _ => h / 2,
                };

                // A drop's size is in pixels, so it grows with the picture as dots do.
                SineBlob(x, y, (int)(_dropRadius * context.DotScale), -_depth, _page, w, h);
            }
        }

        // Each pixel is read from a little away, as far as the pool slopes there. The edges, which AVS left alone, are copied.
        var heights = _heights[_page];
        var length = w * h;
        context.ForEachRow(y =>
        {
            var row = y * w;
            if (y == 0 || y == h - 1)
            {
                Array.Copy(framebuffer, row, fbout, row, w);
                return;
            }

            fbout[row] = framebuffer[row];
            fbout[row + w - 1] = framebuffer[row + w - 1];
            for (int at = row + 1, end = row + w - 1; at < end; at++)
            {
                var dx = heights[at] - heights[at + 1];
                var dy = heights[at] - heights[at + w];
                var from = at + w * (dy >> 3) + (dx >> 3);
                fbout[at] = from < length && from > -1 ? framebuffer[from] : framebuffer[at];
            }
        });

        CalculateWater(context, 1 - _page, w, h);
        _page = 1 - _page;
        return Swapped;
    }

    private void SineBlob(int x, int y, int radius, int height, int page, int w, int h)
    {
        var length = 1024.0 / radius * (1024.0 / radius);
        if (x < 0)
        {
            x = 1 + radius + Shared.Random.Next(Math.Max(1, w - 2 * radius - 1));
        }

        if (y < 0)
        {
            y = 1 + radius + Shared.Random.Next(Math.Max(1, h - 2 * radius - 1));
        }

        var radiusSquared = radius * radius;
        int left = -radius, right = radius, top = -radius, bottom = radius;
        if (x - radius < 1)
        {
            left -= x - radius - 1;
        }

        if (y - radius < 1)
        {
            top -= y - radius - 1;
        }

        if (x + radius > w - 1)
        {
            right -= x + radius - w + 1;
        }

        if (y + radius > h - 1)
        {
            bottom -= y + radius - h + 1;
        }

        var buffer = _heights[page];
        for (var cy = top; cy < bottom; cy++)
        {
            for (var cx = left; cx < right; cx++)
            {
                var square = cy * cy + cx * cx;
                if (square < radiusSquared)
                {
                    var at = w * (cy + y) + cx + x;
                    if ((uint)at < (uint)buffer.Length)
                    {
                        var distance = Math.Sqrt(square * length);
                        buffer[at] += (int)((Math.Cos(distance) + 0xffff) * height) >> 19;
                    }
                }
            }
        }
    }

    /// <summary>Moves the ripples on a frame: each height becomes its eight neighbours' average less its height a frame ago, a little damped.</summary>
    private void CalculateWater(AvsRenderContext context, int page, int w, int h)
    {
        var next = _heights[page];
        var old = _heights[1 - page];
        var density = _density;
        context.ForEachRow(1, h - 1, y =>
        {
            for (int at = y * w + 1, end = y * w + w - 1; at < end; at++)
            {
                var height = ((old[at + w] + old[at - w] + old[at + 1] + old[at - 1] +
                               old[at - w - 1] + old[at - w + 1] + old[at + w - 1] + old[at + w + 1]) >> 2) - next[at];
                next[at] = height - (height >> density);
            }
        });
    }
}
