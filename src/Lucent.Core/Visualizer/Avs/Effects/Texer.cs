namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Texer (the "Texer" plug-in, e_texer.cpp in the later open-source AVS): puts a copy of a picture, added on, at
/// each lit pixel of the picture so far (up to a set number), optionally tinted by that pixel's colour.
/// </summary>
[AvsEffect("Texer")]
public sealed class Texer : AvsEffect
{
    private const int NameLength = 260;

    private bool _addToInput;
    private bool _colorize;
    private int _particles = 100;
    private AvsBitmap? _image;
    private int[] _sprite = Array.Empty<int>();
    private int _spriteWidth;
    private int _spriteHeight;
    private float _spriteScale;

    public Texer(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        // 16 unused bytes, the picture's name, then the input and output modes and the particle count.
        config.Position = 16;
        var name = string.Empty;
        if (config.Remaining >= NameLength)
        {
            name = config.Slice(16, NameLength).ReadTerminatedString();
        }

        config.Position = 16 + NameLength;
        if (config.HasInt)
        {
            var modes = config.ReadInt();
            _addToInput = (modes & 3) == 2;
            _colorize = (modes & 12) == 8;
        }

        _particles = config.ReadInt(_particles);
        _image = null;
        _spriteScale = 0;
        if (name.Length > 0)
        {
            _image = AvsBitmap.Load(Shared.Folder, name, out var problem);
            if (problem is not null)
            {
                Warn(problem);
            }
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_image is null)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_spriteScale != context.DotScale)
        {
            _spriteScale = context.DotScale;
            (_sprite, _spriteWidth, _spriteHeight) = TexerSprites.Scale(_image, context.DotScale);
        }

        if (_addToInput)
        {
            Array.Copy(framebuffer, fbout, w * h);
        }
        else
        {
            Array.Clear(fbout, 0, w * h);
        }

        // Lucent draws dots bigger than AVS did, so only every step-th pixel each way is looked at: one particle
        // a dot, as in AVS, rather than one for each of its pixels.
        var step = RenderHelpers.DotSize(context);
        var placed = 0;
        var done = false;
        for (var y = 0; y < h && !done; y += step)
        {
            var row = framebuffer.AsSpan(y * w, w);
            var x = 0;
            while (x < w)
            {
                // Skips black quickly.
                var found = row[x..].IndexOfAnyExcept(0);
                if (found < 0)
                {
                    break;
                }

                x += found;
                if (step > 1 && x % step != 0)
                {
                    x += step - x % step;
                    continue;
                }

                var pixel = row[x];
                if ((pixel & 0xffffff) != 0)
                {
                    Place(fbout, x, y, w, h, pixel);
                    if (++placed >= _particles)
                    {
                        done = true;
                        break;
                    }
                }

                x += step;
            }
        }

        return Swapped;
    }

    /// <summary>Adds the picture centred on (x, y), with colour filtering each pixel multiplied by <paramref name="tint"/> (over 256).</summary>
    private void Place(int[] fbout, int x, int y, int w, int h, int tint)
    {
        int iw = _spriteWidth, ih = _spriteHeight;
        var startX = x - iw / 2;
        var startY = y - ih / 2;
        var endX = x + (iw - iw / 2);
        var endY = y + (ih - ih / 2);
        var imageStartX = -Math.Min(0, startX);
        var imageStartY = -Math.Min(0, startY);
        var imageEndX = iw + Math.Min(0, w - endX);
        var imageEndY = ih + Math.Min(0, h - endY);
        startX = Math.Max(0, startX);
        startY = Math.Max(0, startY);
        var sprite = _sprite;
        int tb = tint & 0xff, tg = (tint >> 8) & 0xff, tr = (tint >> 16) & 0xff;
        for (int iy = imageStartY, fy = startY; iy < imageEndY; iy++, fy++)
        {
            var source = iy * iw;
            var output = fy * w + startX;
            for (var ix = imageStartX; ix < imageEndX; ix++, output++)
            {
                var c = sprite[source + ix];
                if (_colorize)
                {
                    c = ((c & 0xff) * tb >> 8) | (((c >> 8) & 0xff) * tg >> 8 << 8) | (((c >> 16) & 0xff) * tr >> 8 << 16);
                }

                fbout[output] = AvsBlend.Add(fbout[output], c);
            }
        }
    }
}

/// <summary>Sprites for Texer and Texer II, made bigger to match the dot scale.</summary>
internal static class TexerSprites
{
    /// <summary>The picture at <paramref name="scale"/> times its size (smoothly), or itself at 1.</summary>
    public static (int[] Pixels, int Width, int Height) Scale(AvsBitmap image, float scale)
    {
        var width = Math.Max(1, (int)MathF.Round(image.Width * scale));
        var height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        return width == image.Width && height == image.Height
            ? (image.Pixels, width, height)
            : (ScaleSmooth(image.Pixels, image.Width, image.Height, width, height), width, height);
    }

    /// <summary>The picture stretched to <paramref name="newWidth"/> by <paramref name="newHeight"/>, blending between pixels.</summary>
    public static int[] ScaleSmooth(int[] pixels, int width, int height, int newWidth, int newHeight)
    {
        // A copy with the last row and column doubled, so the blend never reads past the edge.
        var padded = new int[(width + 1) * (height + 1)];
        for (var y = 0; y <= height; y++)
        {
            for (var x = 0; x <= width; x++)
            {
                padded[y * (width + 1) + x] = pixels[Math.Min(y, height - 1) * width + Math.Min(x, width - 1)];
            }
        }

        var result = new int[newWidth * newHeight];
        for (var y = 0; y < newHeight; y++)
        {
            var sy = Math.Max(0.0, (y + 0.5) * height / newHeight - 0.5);
            var row = (int)sy;
            var fy = (int)((sy - row) * 255);
            for (var x = 0; x < newWidth; x++)
            {
                var sx = Math.Max(0.0, (x + 0.5) * width / newWidth - 0.5);
                var column = (int)sx;
                var fx = (int)((sx - column) * 255);
                result[y * newWidth + x] = AvsBlend.Bilinear(padded, row * (width + 1) + column, width + 1, fx, fy);
            }
        }

        return result;
    }
}
