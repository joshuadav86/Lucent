namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Dot Plane (r_dotpln.cpp): a turning 3D grid of dots that the spectrum lifts like waves, rows flowing back over time.</summary>
[AvsEffect(1)]
public sealed class DotPlane : AvsEffect
{
    private const int Size = 64;

    private readonly float[] _heights = new float[Size * Size];
    private readonly float[] _speeds = new float[Size * Size];
    private readonly int[] _cellColours = new int[Size * Size];
    private readonly float[] _firstRow = new float[Size];
    private readonly int[] _colourTable = new int[64];
    private readonly int[] _colours = { 0x1c6b18, 0xff0a23, 0x2a1d74, 0x9036d9, 0x6b88ff };
    private readonly float[] _matrix = new float[16];
    private readonly float[] _matrix2 = new float[16];
    private readonly float[] _temp = new float[16];
    private float _rotation;
    private int _rotationSpeed = 16;
    private int _angle = -20;

    public DotPlane(AvsShared shared)
        : base(shared)
    {
        RenderHelpers.FiveColourTable(_colours, _colourTable);
    }

    public override void Load(AvsConfigReader config)
    {
        _rotationSpeed = config.ReadInt(_rotationSpeed);
        for (var x = 0; x < 5; x++)
        {
            _colours[x] = config.ReadInt(_colours[x]);
        }

        _angle = config.ReadInt(_angle);
        if (config.HasInt)
        {
            _rotation = config.ReadInt() / 32.0f;
        }

        RenderHelpers.FiveColourTable(_colours, _colourTable);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        int width = context.Width, height = context.Height;
        var matrix = _matrix;
        RenderHelpers.ViewMatrix(matrix, _matrix2, _temp, _rotation, _angle);

        var heights = _heights;
        var speeds = _speeds;
        var cells = _cellColours;
        Array.Copy(heights, _firstRow, Size);
        for (var fo = 0; fo < Size; fo++)
        {
            // Each row moves one back; the front row is new from the spectrum.
            var t = (Size - (fo + 2)) * Size;
            var o = t + Size;
            if (fo == Size - 1)
            {
                var audio = context.Audio;
                for (var p = 0; p < Size; p++)
                {
                    var value = Math.Max(Math.Max(audio.Spectrum(0, p * 3), audio.Spectrum(0, p * 3 + 1)), audio.Spectrum(0, p * 3 + 2));
                    heights[p] = value;
                    cells[p] = _colourTable[Math.Min(value >> 2, 63)];
                    speeds[p] = (heights[p] - _firstRow[p]) / 90.0f;
                }
            }
            else
            {
                for (var p = 0; p < Size; p++)
                {
                    var h = heights[t + p] + speeds[t + p];
                    if (h < 0.0f)
                    {
                        h = 0.0f;
                    }

                    heights[o + p] = h;
                    speeds[o + p] = speeds[t + p] - 0.15f * (h / 255.0f);
                    cells[o + p] = cells[t + p];
                }
            }
        }

        var adj = width * 440.0f / 640.0f;
        var adj2 = height * 440.0f / 480.0f;
        if (adj2 < adj)
        {
            adj = adj2;
        }

        var blend = Shared.LineBlendMode;
        var dotSize = RenderHelpers.DotSize(context);
        for (var fo = 0; fo < Size; fo++)
        {
            var f = _rotation < 90.0 || _rotation > 270.0 ? Size - fo - 1 : fo;
            var dw = 350.0f / Size;
            var w = -(Size * 0.5f) * dw;
            var q = (f - Size * 0.5f) * dw;
            var at = f * Size;
            var da = 1;
            if (_rotation < 180.0)
            {
                da = -1;
                dw = -dw;
                w = -w + dw;
                at += Size - 1;
            }

            for (var p = 0; p < Size; p++)
            {
                var y0 = 64.0f - heights[at];
                var x = w * matrix[0] + y0 * matrix[1] + q * matrix[2] + matrix[3];
                var y = w * matrix[4] + y0 * matrix[5] + q * matrix[6] + matrix[7];
                var z = w * matrix[8] + y0 * matrix[9] + q * matrix[10] + matrix[11];
                z = adj / z;
                var ix = (int)(x * z) + width / 2;
                var iy = (int)(y * z) + height / 2;
                if (iy >= 0 && iy < height && ix >= 0 && ix < width)
                {
                    AvsDraw.Dot(framebuffer, ix, iy, width, height, cells[at], dotSize, blend);
                }

                w += dw;
                at += da;
            }
        }

        _rotation += _rotationSpeed / 5.0f;
        if (_rotation >= 360.0f)
        {
            _rotation -= 360.0f;
        }

        if (_rotation < 0.0f)
        {
            _rotation += 360.0f;
        }

        return 0;
    }
}
