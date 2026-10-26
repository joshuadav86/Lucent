namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Render / Picture II (the "Picture II" plug-in, e_picture2.cpp in the later open-source AVS): shows a picture from beside the
/// preset stretched over the whole frame, smooth or blocky, blended one way normally and another on beats.
/// </summary>
[AvsEffect("Picture II")]
public sealed class PictureII : AvsEffect
{
    private const int NameLength = 260;
    private const int Ignore = 10;

    // The plug-in's saved blend numbers: replace, additive, maximum, minimum, 50/50, subtract 1 and 2,
    // multiply, XOR, adjustable, and ignore (draw nothing).
    private int _blend;
    private int _beatBlend;
    private bool _bilinear = true;
    private bool _beatBilinear = true;
    private int _adjust = 128;
    private int _beatAdjust = 128;
    private int[]? _image;
    private int _imageWidth;
    private int _imageHeight;
    private int[]? _plain;
    private int[]? _smooth;
    private int _workWidth;
    private int _workHeight;

    public PictureII(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        var name = config.Slice(0, NameLength).ReadTerminatedString();
        config.Position = Math.Min(config.Length, NameLength);
        _blend = LegacyBlend(config.ReadInt(0));
        _beatBlend = LegacyBlend(config.ReadInt(0));
        _bilinear = config.ReadInt(_bilinear ? 1 : 0) != 0;
        _beatBilinear = config.ReadInt(_beatBilinear ? 1 : 0) != 0;
        _adjust = config.ReadInt(_adjust);
        _beatAdjust = config.ReadInt(_beatAdjust);
        _image = null;
        _plain = null;
        _smooth = null;
        if (name.Length == 0)
        {
            return;
        }

        if (!name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
        {
            Warn($"Picture II's picture \"{name}\" isn't a .bmp file, and Lucent only reads those here, so it's left out.");
            return;
        }

        var bitmap = AvsBitmap.Load(Shared.Folder, name, out var problem);
        if (bitmap is null)
        {
            Warn(problem!);
            return;
        }

        // Each edge row and column doubled, so the smooth stretch blends with copies of the edge rather than past it.
        _imageWidth = bitmap.Width + 2;
        _imageHeight = bitmap.Height + 2;
        _image = new int[_imageWidth * _imageHeight];
        for (var y = 0; y < _imageHeight; y++)
        {
            for (var x = 0; x < _imageWidth; x++)
            {
                var from = Math.Clamp(x - 1, 0, bitmap.Width - 1) + Math.Clamp(y - 1, 0, bitmap.Height - 1) * bitmap.Width;
                _image[x + y * _imageWidth] = bitmap.Pixels[from];
            }
        }
    }

    /// <summary>Keeps known blend numbers; anything else replaces.</summary>
    private static int LegacyBlend(int value) => value is >= 0 and <= Ignore ? value : 0;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_image is null)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_plain is null || _smooth is null || _workWidth != w || _workHeight != h)
        {
            Refresh(w, h);
        }

        var beat = context.IsBeat;
        var source = (beat ? _beatBilinear : _bilinear) ? _smooth! : _plain!;
        var blend = beat ? _beatBlend : _blend;
        var adjust = (beat ? _beatAdjust : _adjust) & 0xff;
        if (blend == Ignore)
        {
            return 0;
        }

        if (blend == 0)
        {
            Array.Copy(source, framebuffer, w * h);
            return 0;
        }

        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                int s = source[at], d = framebuffer[at];
                framebuffer[at] = blend switch
                {
                    1 => AvsBlend.Add(s, d),
                    2 => AvsBlend.Max(s, d),
                    3 => AvsBlend.Min(s, d),
                    4 => AvsBlend.Average(s, d),
                    5 => AvsBlend.Subtract(d, s),
                    6 => AvsBlend.Subtract(s, d),
                    7 => AvsBlend.Multiply(s, d),
                    8 => s ^ d,
                    _ => AvsBlend.Adjust(s, d, adjust),
                };
            }
        });
        return 0;
    }

    /// <summary>The picture stretched to the frame both ways: nearest pixel, and smoothly between pixels.</summary>
    private void Refresh(int w, int h)
    {
        var image = _image!;
        var iw = _imageWidth;
        var ih = _imageHeight;
        _workWidth = w;
        _workHeight = h;
        var plain = _plain = new int[w * h];
        var smooth = _smooth = new int[w * h];
        for (var y = 0; y < h; y++)
        {
            var py = .5 + y * (double)(ih - 2) / h;
            var pyi = (int)py;
            var fy = (int)(byte)((py - pyi) * 256);
            var plainRow = (1 + (int)((long)y * (ih - 2) / h)) * iw;
            for (var x = 0; x < w; x++)
            {
                var px = .5 + x * (double)(iw - 2) / w;
                var pxi = (int)px;
                var fx = (int)(byte)((px - pxi) * 256);
                smooth[y * w + x] = AvsBlend.Bilinear(image, pxi + pyi * iw, iw, fx, fy);
                plain[y * w + x] = image[plainRow + 1 + (int)((long)x * (iw - 2) / w)];
            }
        }
    }
}
