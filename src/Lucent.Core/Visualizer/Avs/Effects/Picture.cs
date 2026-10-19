namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Picture (r_picture.cpp): shows a .bmp picture from beside the preset, stretched over the picture and blended in, optionally only for a while after each beat.</summary>
[AvsEffect(34)]
public sealed class Picture : AvsEffect
{
    private int _enabled = 1;
    private int _blend;
    private int _blendAverage = 1;
    private int _adapt;
    private int _persist = 6;
    private int _ratio;
    private int _axisRatio;
    private int _persistCount;
    private AvsBitmap? _bitmap;
    private int[]? _stretched;
    private int _lastWidth;
    private int _lastHeight;

    public Picture(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _adapt = config.ReadInt(_adapt);
        _persist = config.ReadInt(_persist);
        var name = config.ReadTerminatedString();
        _ratio = config.ReadInt(_ratio);
        _axisRatio = config.ReadInt(_axisRatio);
        _bitmap = null;
        _stretched = null;
        if (name.Length > 0)
        {
            _bitmap = AvsBitmap.Load(Shared.Folder, name, out var problem);
            if (problem is not null)
            {
                Warn(problem);
            }
        }
    }

    public override void Restart() => _persistCount = 0;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var bitmap = _bitmap;
        if (_enabled == 0 || bitmap is null)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_stretched is null || _lastWidth != w || _lastHeight != h)
        {
            _lastWidth = w;
            _lastHeight = h;
            _stretched = Stretch(bitmap, w, h);
        }

        if (context.IsBeat)
        {
            _persistCount = _persist;
        }
        else if (_persistCount > 0)
        {
            _persistCount--;
        }

        var stretched = _stretched;
        var mode = _blend != 0 || (_adapt != 0 && (context.IsBeat || _persistCount != 0)) ? 1
            : _blendAverage != 0 || _adapt != 0 ? 3
            : 0;
        if (mode == 0)
        {
            Array.Copy(stretched, framebuffer, w * h);
            return 0;
        }

        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                framebuffer[at] = mode == 1 ? AvsBlend.Add(stretched[at], framebuffer[at]) : AvsBlend.Average(stretched[at], framebuffer[at]);
            }
        });
        return 0;
    }

    /// <summary>
    /// The picture stretched over the whole frame on black, as StretchBlt's COLORONCOLOR did (nearest pixel),
    /// keeping its shape across the width or height when the ratio option is on.
    /// </summary>
    private int[] Stretch(AvsBitmap bitmap, int w, int h)
    {
        var result = new int[w * h];
        int finalWidth = w, finalHeight = h, startX = 0, startY = 0;
        if (_ratio != 0)
        {
            if (_axisRatio == 0)
            {
                finalHeight = bitmap.Height * w / bitmap.Width;
                startY = h / 2 - finalHeight / 2;
            }
            else
            {
                finalWidth = bitmap.Width * h / bitmap.Height;
                startX = w / 2 - finalWidth / 2;
            }
        }

        if (finalWidth <= 0 || finalHeight <= 0)
        {
            return result;
        }

        var pixels = bitmap.Pixels;
        for (var y = Math.Max(0, startY); y < Math.Min(h, startY + finalHeight); y++)
        {
            var sourceRow = (int)((long)(y - startY) * bitmap.Height / finalHeight) * bitmap.Width;
            for (var x = Math.Max(0, startX); x < Math.Min(w, startX + finalWidth); x++)
            {
                result[y * w + x] = pixels[sourceRow + (int)((long)(x - startX) * bitmap.Width / finalWidth)];
            }
        }

        return result;
    }
}
