namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Render / Dot Fountain (r_dotfnt.cpp): a turning 3D fountain of dots shot up by the waveform, which arc out and fall back.</summary>
[AvsEffect(19)]
public sealed class DotFountain : AvsEffect
{
    private const int Divisions = 30;
    private const int Rows = 256;

    private readonly Point[] _points = new Point[Rows * Divisions];
    private readonly Point[] _previousFirstRow = new Point[Divisions];
    private readonly int[] _colourTable = new int[64];
    private readonly int[] _colours = { 0x1c6b18, 0xff0a23, 0x2a1d74, 0x9036d9, 0x6b88ff };
    private readonly float[] _matrix = new float[16];
    private readonly float[] _matrix2 = new float[16];
    private readonly float[] _temp = new float[16];
    private float _rotation;
    private int _rotationSpeed = 16;
    private int _angle = -20;

    public DotFountain(AvsShared shared)
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

    public override void Restart() => Array.Clear(_points);

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        int width = context.Width, height = context.Height;
        var matrix = _matrix;
        RenderHelpers.ViewMatrix(matrix, _matrix2, _temp, _rotation, _angle);

        var points = _points;
        Array.Copy(points, _previousFirstRow, Divisions);

        // Every row moves one on (the last drops off), each dot falling and spreading.
        for (var fo = Rows - 2; fo >= 0; fo--)
        {
            var booga = 1.3f / (fo + 100);
            var from = fo * Divisions;
            var to = from + Divisions;
            for (var p = 0; p < Divisions; p++)
            {
                var point = points[from + p];
                point.R += point.Dr;
                point.Dh += 0.05f;
                point.Dr += booga;
                point.H += point.Dh;
                points[to + p] = point;
            }
        }

        // New dots, shot up as hard as the waveform says.
        var audio = context.Audio;
        for (var p = 0; p < Divisions; p++)
        {
            var t = audio.Unsigned(1, 0, p) ^ 128;
            t *= 5;
            t /= 4;
            t -= 64;
            if (context.IsBeat)
            {
                t += 128;
            }

            if (t > 255)
            {
                t = 255;
            }

            ref var point = ref points[p];
            point.R = 1.0f;
            var dr = t / 200.0f;
            if (dr < 0)
            {
                dr = -dr;
            }

            point.H = 250;
            dr += 1.0f;
            point.Dh = -dr * (100.0f + (point.Dh - _previousFirstRow[p].Dh)) / 100.0f * 2.8f;
            t /= 4;

            // AVS read before its colour table for quiet samples; they take its first colour.
            point.Colour = _colourTable[Math.Clamp(t, 0, 63)];
            var a = p * 3.14159f * 2.0f / Divisions;
            point.Ax = (float)Math.Sin(a);
            point.Ay = (float)Math.Cos(a);
            point.Dr = 0.0f;
        }

        var adj = width * 440.0f / 640.0f;
        var adj2 = height * 440.0f / 480.0f;
        if (adj2 < adj)
        {
            adj = adj2;
        }

        var blend = Shared.LineBlendMode;
        var dotSize = RenderHelpers.DotSize(context);
        for (var i = 0; i < points.Length; i++)
        {
            ref var point = ref points[i];
            var px = point.Ax * point.R;
            var pz = point.Ay * point.R;
            var x = px * matrix[0] + point.H * matrix[1] + pz * matrix[2] + matrix[3];
            var y = px * matrix[4] + point.H * matrix[5] + pz * matrix[6] + matrix[7];
            var z = px * matrix[8] + point.H * matrix[9] + pz * matrix[10] + matrix[11];
            z = adj / z;
            if (z > 0.0000001)
            {
                var ix = (int)(x * z) + width / 2;
                var iy = (int)(y * z) + height / 2;
                if (iy >= 0 && iy < height && ix >= 0 && ix < width)
                {
                    AvsDraw.Dot(framebuffer, ix, iy, width, height, point.Colour, dotSize, blend);
                }
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

    private struct Point
    {
        public float R;
        public float Dr;
        public float H;
        public float Dh;
        public float Ax;
        public float Ay;
        public int Colour;
    }
}
