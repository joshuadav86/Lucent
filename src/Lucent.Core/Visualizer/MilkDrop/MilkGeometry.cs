namespace Lucent.Visualizer.MilkDrop;

/// <summary>A colour with alpha, 0..1 per channel.</summary>
public readonly record struct MilkColor(float R, float G, float B, float A)
{
    public static MilkColor From(double r, double g, double b, double a) =>
        new(Clamp(r), Clamp(g), Clamp(b), Clamp(a));

    private static float Clamp(double value) => double.IsFinite(value) ? (float)Math.Clamp(value, 0.0, 1.0) : 0f;
}

/// <summary>A run of triangles drawn the same way: blended normally or added on, and coloured or textured.</summary>
public readonly record struct MilkDrawBatch(int FirstVertex, int VertexCount, bool Additive, bool Textured);

/// <summary>
/// Everything a MilkDrop preset draws over its warped image in one frame (waves, shapes, borders,
/// motion vectors), as triangles in batches. Positions are -1..1 with y up, as on screen; each
/// vertex is x, y, u, v, r, g, b, a. Lines are drawn as thin quads, so they can be thicker than
/// one pixel on big screens (Direct3D 11 and Metal only draw one-pixel lines).
/// </summary>
public sealed class MilkGeometry
{
    public const int FloatsPerVertex = 8;

    /// <summary>The most vertices one frame can have; anything beyond is left out.</summary>
    public const int MaxVertices = 1 << 18;

    private readonly List<MilkDrawBatch> _batches = new();
    private float[] _data = new float[4096 * FloatsPerVertex];
    private float _pixelX = 2f / 1024;
    private float _pixelY = 2f / 768;
    private bool _additive;
    private bool _textured;
    private int _batchStart;

    /// <summary>x, y, u, v, r, g, b, a for each vertex, <see cref="VertexCount"/> of them.</summary>
    public float[] Data => _data;

    public int VertexCount { get; private set; }

    public IReadOnlyList<MilkDrawBatch> Batches => _batches;

    /// <summary>The width of a normal line, in pixels of the image being drawn.</summary>
    public float LineWidth { get; private set; } = 1f;

    /// <summary>Starts a new frame drawn into an image of the given size.</summary>
    public void Clear(int width, int height)
    {
        VertexCount = 0;
        _batches.Clear();
        _batchStart = 0;
        _pixelX = 2f / Math.Max(1, width);
        _pixelY = 2f / Math.Max(1, height);

        // MilkDrop drew one-pixel lines into an image about 1024 high; keep that look on bigger ones.
        LineWidth = Math.Max(1f, height / 800f);
    }

    /// <summary>Sets how the triangles that follow are drawn.</summary>
    public void Use(bool additive, bool textured)
    {
        if (additive == _additive && textured == _textured)
        {
            return;
        }

        Flush();
        _additive = additive;
        _textured = textured;
    }

    /// <summary>Ends the last batch. Call once the frame's drawing is done.</summary>
    public void Flush()
    {
        if (VertexCount > _batchStart)
        {
            _batches.Add(new MilkDrawBatch(_batchStart, VertexCount - _batchStart, _additive, _textured));
        }

        _batchStart = VertexCount;
    }

    public void Triangle(float x0, float y0, MilkColor c0, float x1, float y1, MilkColor c1, float x2, float y2, MilkColor c2)
    {
        if (VertexCount + 3 > MaxVertices)
        {
            return;
        }

        Vertex(x0, y0, 0, 0, c0);
        Vertex(x1, y1, 0, 0, c1);
        Vertex(x2, y2, 0, 0, c2);
    }

    public void TexturedTriangle(
        float x0, float y0, float u0, float v0, MilkColor c0,
        float x1, float y1, float u1, float v1, MilkColor c1,
        float x2, float y2, float u2, float v2, MilkColor c2)
    {
        if (VertexCount + 3 > MaxVertices)
        {
            return;
        }

        Vertex(x0, y0, u0, v0, c0);
        Vertex(x1, y1, u1, v1, c1);
        Vertex(x2, y2, u2, v2, c2);
    }

    /// <summary>A filled rectangle between two corners.</summary>
    public void Rectangle(float left, float bottom, float right, float top, MilkColor color)
    {
        Triangle(left, bottom, color, right, bottom, color, left, top, color);
        Triangle(left, top, color, right, bottom, color, right, top, color);
    }

    /// <summary>A line <paramref name="width"/> pixels wide, its colour blending from one end to the other.</summary>
    public void Line(float x0, float y0, MilkColor c0, float x1, float y1, MilkColor c1, float width)
    {
        // The offset to either side, worked out in pixels so the line is as wide whichever way it runs.
        var dx = (x1 - x0) / _pixelX;
        var dy = (y1 - y0) / _pixelY;
        var length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < 1e-4f || !float.IsFinite(length))
        {
            return;
        }

        var half = width * 0.5f / length;
        var ox = -dy * half * _pixelX;
        var oy = dx * half * _pixelY;
        Triangle(x0 - ox, y0 - oy, c0, x1 - ox, y1 - oy, c1, x0 + ox, y0 + oy, c0);
        Triangle(x0 + ox, y0 + oy, c0, x1 - ox, y1 - oy, c1, x1 + ox, y1 + oy, c1);
    }

    /// <summary>A square dot <paramref name="size"/> pixels across.</summary>
    public void Dot(float x, float y, MilkColor color, float size)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
        {
            return;
        }

        var hx = size * 0.5f * _pixelX;
        var hy = size * 0.5f * _pixelY;
        Rectangle(x - hx, y - hy, x + hx, y + hy, color);
    }

    /// <summary>Joins the points with lines (or draws each as a dot), closing the loop if asked.</summary>
    public void Strip(ReadOnlySpan<float> xs, ReadOnlySpan<float> ys, ReadOnlySpan<MilkColor> colors, int count, bool loop, bool dots, bool thick)
    {
        if (dots)
        {
            var size = LineWidth * (thick ? 3f : 1.5f);
            for (var i = 0; i < count; i++)
            {
                Dot(xs[i], ys[i], colors[i], size);
            }

            return;
        }

        var width = LineWidth * (thick ? 2.5f : 1.25f);
        for (var i = 0; i + 1 < count; i++)
        {
            Line(xs[i], ys[i], colors[i], xs[i + 1], ys[i + 1], colors[i + 1], width);
        }

        if (loop && count > 2)
        {
            Line(xs[count - 1], ys[count - 1], colors[count - 1], xs[0], ys[0], colors[0], width);
        }
    }

    private void Vertex(float x, float y, float u, float v, MilkColor color)
    {
        var at = VertexCount * FloatsPerVertex;
        if (at + FloatsPerVertex > _data.Length)
        {
            Array.Resize(ref _data, Math.Min(_data.Length * 2, MaxVertices * FloatsPerVertex));
        }

        _data[at] = float.IsFinite(x) ? x : 0f;
        _data[at + 1] = float.IsFinite(y) ? y : 0f;
        _data[at + 2] = u;
        _data[at + 3] = v;
        _data[at + 4] = color.R;
        _data[at + 5] = color.G;
        _data[at + 6] = color.B;
        _data[at + 7] = color.A;
        VertexCount++;
    }
}
