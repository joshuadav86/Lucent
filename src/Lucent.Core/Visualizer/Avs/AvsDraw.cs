namespace WinampInspiredVisualizer.Visualizer.Avs;

/// <summary>AVS's line drawing (linedraw.cpp) and dots, putting each pixel on with the line blend mode.</summary>
public static class AvsDraw
{
    /// <summary>
    /// line(): from (x1, y1) to (x2, y2), <paramref name="lineWidth"/> pixels thick (1-255), each pixel put
    /// on with <paramref name="blendMode"/> (see <see cref="AvsBlend.Line"/>). Parts off the picture are left out.
    /// As in AVS, the last pixel isn't drawn, so lines that join don't double up their ends.
    /// </summary>
    public static void Line(int[] fb, int x1, int y1, int x2, int y2, int width, int height, int colour, int lineWidth, int blendMode)
    {
        var dy = Math.Abs(y2 - y1);
        var dx = Math.Abs(x2 - x1);
        var lw = Math.Clamp(lineWidth, 1, 255);
        var lw2 = lw / 2;

        if (dx == 0)
        {
            // Straight down.
            x1 -= lw2;
            if (x1 + lw >= 0 && x1 < width)
            {
                var d = Math.Max(Math.Min(y1, y2), 0);
                var ye = Math.Min(Math.Max(y1, y2), height - 1);
                if (x1 < 0)
                {
                    lw += x1;
                    x1 = 0;
                }

                if (x1 + lw >= width)
                {
                    lw = width - x1;
                }

                for (; d < ye; d++)
                {
                    var at = d * width + x1;
                    for (var x = 0; x < lw; x++, at++)
                    {
                        fb[at] = AvsBlend.Line(fb[at], colour, blendMode);
                    }
                }
            }

            return;
        }

        if (y1 == y2)
        {
            // Straight across.
            y1 -= lw2;
            if (y1 + lw >= 0 && y1 < height)
            {
                var d = Math.Max(Math.Min(x1, x2), 0);
                var xe = Math.Min(Math.Max(x1, x2), width - 1);
                if (y1 < 0)
                {
                    lw += y1;
                    y1 = 0;
                }

                if (y1 + lw >= height)
                {
                    lw = height - y1;
                }

                for (var y = 0; y < lw; y++)
                {
                    var at = (y1 + y) * width + d;
                    for (var x = d; x < xe; x++, at++)
                    {
                        fb[at] = AvsBlend.Line(fb[at], colour, blendMode);
                    }
                }
            }

            return;
        }

        if (dy <= dx)
        {
            // Mostly across: step along x, a column of lw pixels at each step.
            if (x2 < x1)
            {
                (x1, x2) = (x2, x1);
                (y1, y2) = (y2, y1);
            }

            var yIncrement = y2 > y1 ? 1 : -1;
            y1 -= lw2;
            var d = dy + dy - dx;
            var eIncrement = dy + dy;
            var neIncrement = d - dx;
            if (x2 >= 0 && x1 < width)
            {
                if (x1 < 0)
                {
                    var v = yIncrement * -x1;
                    v = v * dy / dx;
                    y1 += v;
                    x1 = 0;
                }

                if (x2 > width)
                {
                    x2 = width;
                }

                for (; x1 < x2; x1++)
                {
                    var yp = Math.Max(y1, 0);
                    var ype = Math.Min(y1 + lw, height);
                    for (; yp < ype; yp++)
                    {
                        var at = yp * width + x1;
                        fb[at] = AvsBlend.Line(fb[at], colour, blendMode);
                    }

                    if (d < 0)
                    {
                        d += eIncrement;
                    }
                    else
                    {
                        d += neIncrement;
                        y1 += yIncrement;
                    }
                }
            }
        }
        else
        {
            // Mostly down: step along y, a row of lw pixels at each step.
            if (y2 < y1)
            {
                (x1, x2) = (x2, x1);
                (y1, y2) = (y2, y1);
            }

            var xIncrement = x2 > x1 ? 1 : -1;
            var d = dx + dx - dy;
            var eIncrement = dx + dx;
            var neIncrement = d - dy;
            x1 -= lw2;
            if (y2 >= 0 && y1 < height)
            {
                if (y1 < 0)
                {
                    var v = xIncrement * -y1;
                    v = v * dx / dy;
                    x1 += v;
                    y1 = 0;
                }

                if (y2 > height)
                {
                    y2 = height;
                }

                for (; y1 < y2; y1++)
                {
                    var xp = Math.Max(x1, 0);
                    var xpe = Math.Min(x1 + lw, width);
                    var row = y1 * width;
                    for (; xp < xpe; xp++)
                    {
                        fb[row + xp] = AvsBlend.Line(fb[row + xp], colour, blendMode);
                    }

                    if (d < 0)
                    {
                        d += eIncrement;
                    }
                    else
                    {
                        d += neIncrement;
                        x1 += xIncrement;
                    }
                }
            }
        }
    }

    /// <summary>
    /// A dot at (x, y): one pixel at AVS's sizes, or a little square <paramref name="size"/> pixels across
    /// (centred on it) when Lucent draws bigger than AVS did, put on with <paramref name="blendMode"/>.
    /// </summary>
    public static void Dot(int[] fb, int x, int y, int width, int height, int colour, int size, int blendMode)
    {
        if (size <= 1)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                var at = y * width + x;
                fb[at] = AvsBlend.Line(fb[at], colour, blendMode);
            }

            return;
        }

        var half = size / 2;
        var left = Math.Max(x - half, 0);
        var right = Math.Min(x - half + size, width);
        var top = Math.Max(y - half, 0);
        var bottom = Math.Min(y - half + size, height);
        for (var row = top; row < bottom; row++)
        {
            var at = row * width + left;
            for (var column = left; column < right; column++, at++)
            {
                fb[at] = AvsBlend.Line(fb[at], colour, blendMode);
            }
        }
    }
}
