namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Blitter Feedback (r_blit.cpp): copies the picture back onto itself zoomed in or shrunk into the middle, and can jump to a second zoom on beats.</summary>
[AvsEffect(4)]
public sealed class BlitterFeedback : AvsEffect
{
    private int _scale = 30;
    private int _beatScale = 30;
    private int _blend;
    private int _onBeat;
    private int _subpixel = 1;
    private int _position = 30;

    public BlitterFeedback(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _scale = config.ReadInt(_scale);
        _beatScale = config.ReadInt(_beatScale);
        _blend = config.ReadInt(_blend);
        _onBeat = config.ReadInt(_onBeat);

        // Presets saved before the setting existed didn't smooth.
        _subpixel = config.ReadInt(0);
        _position = _scale;
    }

    public override void Restart()
    {
        _position = _scale;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (context.IsBeat && _onBeat != 0)
        {
            _position = _beatScale;
        }

        int zoom;
        if (_scale < _beatScale)
        {
            zoom = Math.Max(_position, _scale);
            _position -= 3;
        }
        else
        {
            zoom = Math.Min(_position, _scale);
            _position += 3;
        }

        zoom = Math.Max(zoom, 0);
        if (zoom < 32)
        {
            return ZoomIn(context, framebuffer, fbout, zoom);
        }

        if (zoom > 32)
        {
            ShrinkInto(context, framebuffer, fbout, zoom);
        }

        return 0;
    }

    /// <summary>blitter_normal: the middle of the picture stretched to fill it.</summary>
    private int ZoomIn(AvsRenderContext context, int[] framebuffer, int[] fbout, int zoom)
    {
        int w = context.Width, h = context.Height;
        var dsX = ((zoom + 32) << 16) / 64;
        var startX = (int)((((long)w << 16) - (long)dsX * w) / 2);
        var startY = (int)((((long)h << 16) - (long)dsX * h) / 2);
        var subpixel = _subpixel != 0;
        var blend = _blend != 0;

        // The last pixel BLEND4 may read from; AVS read one past the end on the bottom row.
        var lastStart = w * h - w - 2;
        context.ForEachRow(y =>
        {
            var sY = startY + y * dsX;
            var source = (sY >> 16) * w;
            var yPart = (((sY >> 8) & 0xff) * 255) >> 8;
            var sX = startX;
            for (int at = y * w, end = at + w; at < end; at++, sX += dsX)
            {
                int colour;
                if (subpixel)
                {
                    var from = Math.Max(0, Math.Min(source + (sX >> 16), lastStart));
                    colour = AvsBlend.Bilinear(framebuffer, from, w, (sX >> 8) & 0xff, yPart);
                }
                else
                {
                    colour = framebuffer[source + (sX >> 16)];
                }

                fbout[at] = blend ? AvsBlend.Average(framebuffer[at], colour) : colour;
            }
        });
        return Swapped;
    }

    /// <summary>blitter_out: the whole picture shrunk into the middle, over what's there.</summary>
    private void ShrinkInto(AvsRenderContext context, int[] framebuffer, int[] fbout, int zoom)
    {
        int w = context.Width, h = context.Height;
        const int adjust = 7;
        var dsX = (zoom + (1 << adjust) - 32) << (16 - adjust);
        var xLength = (int)(((long)w << 16) / dsX) & ~3;
        var yLength = (int)(((long)h << 16) / dsX);
        if (xLength >= w || yLength >= h)
        {
            return;
        }

        var startX = (w - xLength) / 2;
        var startY = (h - yLength) / 2;
        var blend = _blend != 0;
        context.ForEachRow(0, yLength, y =>
        {
            var source = ((32768 + y * dsX) >> 16) * w;
            var target = (y + startY) * w + startX;
            var sX = 32768;
            for (var x = 0; x < xLength; x++, sX += dsX)
            {
                var colour = framebuffer[source + (sX >> 16)];
                fbout[target + x] = blend ? AvsBlend.Average(framebuffer[target + x], colour) : colour;
            }
        });

        for (var y = 0; y < yLength; y++)
        {
            var at = (y + startY) * w + startX;
            Array.Copy(fbout, at, framebuffer, at, xLength);
        }
    }
}
