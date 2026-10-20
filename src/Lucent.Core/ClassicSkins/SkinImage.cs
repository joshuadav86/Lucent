namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>
/// One of a skin's pictures, decoded: <see cref="Width"/> × <see cref="Height"/> pixels, top row first,
/// each 0xAARRGGBB (which is B, G, R, A in memory, the order both apps' bitmaps take).
/// </summary>
public sealed class SkinImage
{
    public SkinImage(int width, int height, uint[] pixels)
    {
        if (width <= 0 || height <= 0 || pixels.Length != width * height)
        {
            throw new ArgumentException("The pixels don't match the size.");
        }

        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }

    public int Height { get; }

    public uint[] Pixels { get; }

    public uint this[int x, int y] => Pixels[y * Width + x];
}
