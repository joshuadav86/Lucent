using System.Text;

namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>
/// A picture drawn from a classic skin's sprites, in skin pixels (0xAARRGGBB, opaque): the equalizer and the
/// playlist's frame draw into one. It can change size, as the playlist window does.
/// </summary>
public sealed class SkinCanvas
{
    public SkinCanvas(int width, int height) => Resize(width, height);

    public int Width { get; private set; }

    public int Height { get; private set; }

    public uint[] Pixels { get; private set; } = Array.Empty<uint>();

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        Pixels = new uint[width * height];
    }

    /// <summary>Copies a sprite here, its top left corner at (left, top). Parts past the sheet's edge or the canvas's are left out.</summary>
    public void Draw(ClassicSkin skin, Sprite sprite, int left, int top) =>
        Draw(skin, sprite, left, top, sprite.Width, sprite.Height);

    /// <summary>Copies the top left <paramref name="width"/> × <paramref name="height"/> of a sprite.</summary>
    public void Draw(ClassicSkin skin, Sprite sprite, int left, int top, int width, int height)
    {
        if (skin.Sheet(sprite.Sheet) is not { } sheet)
        {
            return;
        }

        // Some skins' sheets are smaller than Winamp's.
        width = Math.Min(Math.Min(width, sprite.Width), sheet.Width - sprite.X);
        height = Math.Min(Math.Min(height, sprite.Height), sheet.Height - sprite.Y);
        for (var y = 0; y < height; y++)
        {
            var targetY = top + y;
            if (targetY < 0 || targetY >= Height)
            {
                continue;
            }

            for (var x = 0; x < width; x++)
            {
                var targetX = left + x;
                if (targetX >= 0 && targetX < Width)
                {
                    Pixels[targetY * Width + targetX] = sheet.Pixels[(sprite.Y + y) * sheet.Width + sprite.X + x];
                }
            }
        }
    }

    /// <summary>Repeats a sprite across and down a rectangle, cutting the last copies short to fit.</summary>
    public void Tile(ClassicSkin skin, Sprite sprite, int left, int top, int width, int height)
    {
        for (var y = 0; y < height; y += sprite.Height)
        {
            for (var x = 0; x < width; x += sprite.Width)
            {
                Draw(skin, sprite, left + x, top + y, Math.Min(sprite.Width, width - x), Math.Min(sprite.Height, height - y));
            }
        }
    }

    public void Plot(int x, int y, uint colour)
    {
        if (x >= 0 && x < Width && y >= 0 && y < Height)
        {
            Pixels[y * Width + x] = 0xFF000000 | colour;
        }
    }

    /// <summary>The colour of one pixel of a sprite, or null when the skin hasn't got it.</summary>
    public static uint? PixelOf(ClassicSkin skin, Sprite sprite, int x, int y) =>
        skin.Sheet(sprite.Sheet) is { } sheet && sprite.X + x < sheet.Width && sprite.Y + y < sheet.Height
            ? sheet[sprite.X + x, sprite.Y + y]
            : null;

    /// <summary>Text in the skin's little font (text.bmp), cut off at <paramref name="width"/>.</summary>
    public void Text(ClassicSkin skin, string text, int left, int top, int width)
    {
        text = Simplify(text);
        for (var i = 0; i < text.Length && i * ClassicSprites.GlyphWidth < width; i++)
        {
            var (row, column) = ClassicSprites.Glyph(text[i]) ?? ClassicSprites.Glyph(' ')!.Value;
            var glyph = new Sprite("text", column * ClassicSprites.GlyphWidth, row * ClassicSprites.GlyphHeight,
                ClassicSprites.GlyphWidth, ClassicSprites.GlyphHeight);
            Draw(skin, glyph, left + i * ClassicSprites.GlyphWidth, top,
                Math.Min(ClassicSprites.GlyphWidth, width - i * ClassicSprites.GlyphWidth), ClassicSprites.GlyphHeight);
        }
    }

    /// <summary>Takes accents off letters the skin's font doesn't have (é becomes e), keeping the few it does.</summary>
    public static string Simplify(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (ClassicSprites.Glyph(c) is not null)
            {
                builder.Append(c);
                continue;
            }

            var plain = c.ToString().Normalize(NormalizationForm.FormD)
                .FirstOrDefault(part => ClassicSprites.Glyph(part) is not null);
            builder.Append(plain == default ? ' ' : plain);
        }

        return builder.ToString();
    }
}
