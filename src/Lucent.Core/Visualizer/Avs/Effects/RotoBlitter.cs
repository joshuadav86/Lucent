namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Roto Blitter (r_rotblit.cpp): copies the picture back onto itself zoomed and turned, tiling at the edges, and can swap direction or zoom on beats.</summary>
[AvsEffect(9)]
public sealed class RotoBlitter : AvsEffect
{
    private int _zoomScale = 31;
    private int _rotateDirection = 31;
    private int _blend;
    private int _beatReverse;
    private int _beatReverseSpeed;
    private int _beatZoomScale = 31;
    private int _beatZoom;
    private int _subpixel = 1;
    private int _scalePosition = 31;
    private int _rotateReverse = 1;
    private double _rotateReversePosition = 1.0;

    public RotoBlitter(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _zoomScale = config.ReadInt(_zoomScale);
        _rotateDirection = config.ReadInt(_rotateDirection);
        _blend = config.ReadInt(_blend);
        _beatReverse = config.ReadInt(_beatReverse);
        _beatReverseSpeed = config.ReadInt(_beatReverseSpeed);
        _beatZoomScale = config.ReadInt(_beatZoomScale);
        _beatZoom = config.ReadInt(_beatZoom);

        // Presets saved before the setting existed didn't smooth.
        _subpixel = config.ReadInt(0);
        _scalePosition = _zoomScale;
    }

    public override void Restart()
    {
        _scalePosition = _zoomScale;
        _rotateReverse = 1;
        _rotateReversePosition = 1.0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        int w = context.Width, h = context.Height;
        if (context.IsBeat && _beatReverse != 0)
        {
            _rotateReverse = -_rotateReverse;
        }

        if (_beatReverse == 0)
        {
            _rotateReverse = 1;
        }

        _rotateReversePosition += 1.0 / (1 + _beatReverseSpeed * 4) * (_rotateReverse - _rotateReversePosition);
        if (_rotateReversePosition > _rotateReverse && _rotateReverse > 0)
        {
            _rotateReversePosition = _rotateReverse;
        }

        if (_rotateReversePosition < _rotateReverse && _rotateReverse < 0)
        {
            _rotateReversePosition = _rotateReverse;
        }

        if (context.IsBeat && _beatZoom != 0)
        {
            _scalePosition = _beatZoomScale;
        }

        int zoomValue;
        if (_zoomScale < _beatZoomScale)
        {
            zoomValue = Math.Max(_scalePosition, _zoomScale);
            if (_scalePosition > _zoomScale)
            {
                _scalePosition -= 3;
            }
        }
        else
        {
            zoomValue = Math.Min(_scalePosition, _zoomScale);
            if (_scalePosition < _zoomScale)
            {
                _scalePosition += 3;
            }
        }

        var zoom = 1.0 + (zoomValue - 31) / 31.0;
        var theta = (_rotateDirection - 32) * _rotateReversePosition;
        var cos = Math.Cos(theta * Math.PI / 180.0) * zoom;
        var sin = Math.Sin(theta * Math.PI / 180.0) * zoom;
        var dsDx = (int)(cos * 65536.0);
        var dtDy = (int)(cos * 65536.0);
        var dsDy = -(int)(sin * 65536.0);
        var dtDx = (int)(sin * 65536.0);
        var ds = (w - 1) << 16;
        var dt = (h - 1) << 16;
        if (dsDx <= -ds || dsDx >= ds || dtDx <= -dt || dtDx >= dt)
        {
            // Too small a picture to tile (AVS left fbout as it was); pass the picture on unchanged.
            Array.Copy(framebuffer, fbout, w * h);
            return Swapped;
        }

        // AVS added (w - 1) * (32768 + (1 << 20)) to keep the start positive; in 64 bits so wide pictures don't overflow.
        var sStart = -((long)((w - 1) / 2) * dsDx + (long)((h - 1) / 2) * dsDy) + (long)(w - 1) * (32768 + (1 << 20));
        var tStart = -((long)((w - 1) / 2) * dtDx + (long)((h - 1) / 2) * dtDy) + (long)(h - 1) * (32768 + (1 << 20));
        var subpixel = _subpixel != 0;
        var blend = _blend != 0;
        context.ForEachRow(row =>
        {
            var s = (int)((sStart + (long)row * dsDy) % ds);
            var t = (int)((tStart + (long)row * dtDy) % dt);
            if (s < 0)
            {
                s += ds;
            }

            if (t < 0)
            {
                t += dt;
            }

            for (int at = row * w, end = at + w; at < end; at++)
            {
                if (t < 0)
                {
                    t += dt;
                }
                else if (t >= dt)
                {
                    t -= dt;
                }

                if (s < 0)
                {
                    s += ds;
                }
                else if (s >= ds)
                {
                    s -= ds;
                }

                var from = (s >> 16) + (t >> 16) * w;
                var colour = subpixel ? AvsBlend.Bilinear16(framebuffer, from, w, s, t) : framebuffer[from];
                fbout[at] = blend ? AvsBlend.Average(framebuffer[at], colour) : colour;
                s += dsDx;
                t += dtDx;
            }
        });
        return Swapped;
    }
}
