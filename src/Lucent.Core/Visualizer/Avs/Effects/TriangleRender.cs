using System.Runtime.CompilerServices;
using Lucent.Visualizer.MilkDrop;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Render / Triangle (the "Render: Triangle" plug-in, e_triangle.cpp in the later open-source AVS): code places n
/// filled triangles, each in one colour, optionally hiding ones behind others by a depth value.
/// </summary>
[AvsEffect("Render: Triangle")]
public sealed class TriangleRender : AvsEffect
{
    private const double MaxZ = 40000;
    private const double ZResolution = 100000;

    // Every Triangle in a preset shares one depth buffer, as the plug-in's instances did.
    private static readonly ConditionalWeakTable<AvsShared, DepthBuffer> DepthBuffers = new();

    private readonly string[] _code = { string.Empty, string.Empty, string.Empty, string.Empty };
    private readonly Vertex[] _vertices = new Vertex[3];
    private Compiled? _compiled;

    public TriangleRender(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        // Init, frame, beat, and per-triangle code, each up to a 0 byte.
        for (var i = 0; i < 4; i++)
        {
            _code[i] = config.ReadTerminatedString();
        }

        _compiled = null;
    }

    public override void Restart()
    {
        if (_compiled is not null)
        {
            _compiled.Initialised = false;
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var code = _compiled ??= new Compiled(this);
        int w = context.Width, h = context.Height;
        var v = code.Values;
        if (!code.Initialised)
        {
            v[code.N] = 0;
            code.Init.Run();
            code.Initialised = true;
        }

        // The variables each frame starts from; i is left as the last frame left it, as the plug-in did.
        v = code.Values;
        v[code.W] = w;
        v[code.H] = h;
        v[code.X1] = v[code.Y1] = v[code.X2] = v[code.Y2] = v[code.X3] = v[code.Y3] = 0.0;
        v[code.Red1] = v[code.Green1] = v[code.Blue1] = 1.0;
        v[code.Red2] = v[code.Green2] = v[code.Blue2] = 1.0;
        v[code.Red3] = v[code.Green3] = v[code.Blue3] = 1.0;
        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        v = code.Values;
        var depth = DepthBuffers.GetValue(Shared, _ => new DepthBuffer());
        depth.ResetIfNeeded(w, h, v[code.ZbClear] != 0.0);
        var count = (int)Math.Clamp(Math.Round(v[code.N]), int.MinValue, int.MaxValue);
        if (count <= 0)
        {
            return 0;
        }

        var step = count > 1 ? 1.0 / (count - 1.0) : 1.0;
        var i = 0.0;
        var wHalf = (w - 1) / 2.0;
        var hHalf = (h - 1) / 2.0;
        v[code.I] = i;
        var blend = Shared.LineBlendMode;
        for (var k = 0; k < count; k++)
        {
            v = code.Values;
            v[code.Skip] = 0.0;
            code.Point.Run();
            v = code.Values;
            if (v[code.Skip] != 0.0)
            {
                // A skipped triangle doesn't move i on.
                continue;
            }

            var z = ToDepth(v[code.Z1]);
            _vertices[0] = new Vertex(ToScreen(v[code.X1], wHalf), ToScreen(v[code.Y1], hHalf));
            _vertices[1] = new Vertex(ToScreen(v[code.X2], wHalf), ToScreen(v[code.Y2], hHalf));
            _vertices[2] = new Vertex(ToScreen(v[code.X3], wHalf), ToScreen(v[code.Y3], hHalf));
            var colour = ToByte(v[code.Blue1]) | (ToByte(v[code.Green1]) << 8) | (ToByte(v[code.Red1]) << 16);
            Draw(framebuffer, w, h, z, v[code.ZBuf] != 0.0 ? depth.Buffer : null, colour, blend);
            i += step;
            code.Values[code.I] = i;
        }

        return 0;
    }

    private static int ToByte(double value) => (int)Math.Max(0.0, Math.Min(255.0, value * 255.0f));

    /// <summary>From -1..1 to pixels (0..size-1), truncated, kept to a billion pixels either way so the maths can't overflow.</summary>
    private static long ToScreen(double value, double half)
    {
        var p = (value + 1.0) * half;
        return double.IsNaN(p) ? 0 : (long)Math.Clamp(p, -1e9, 1e9);
    }

    private static uint ToDepth(double z) => z > MaxZ ? (uint)(MaxZ * ZResolution) : z > 0 ? (uint)(z * ZResolution) : 0;

    /// <summary>
    /// Fills the triangle a row at a time between its long edge and its two short ones (sorted from the top),
    /// leaving out each row's last pixel, as the plug-in did.
    /// </summary>
    private void Draw(int[] framebuffer, int w, int h, uint z, uint[]? depth, int colour, int blend)
    {
        // Sorted from the top, keeping the code's order where they're level.
        for (var a = 1; a < 3; a++)
        {
            for (var b = a; b > 0 && _vertices[b - 1].Y > _vertices[b].Y; b--)
            {
                (_vertices[b - 1], _vertices[b]) = (_vertices[b], _vertices[b - 1]);
            }
        }

        var v1 = _vertices[0];
        var v2 = _vertices[1];
        var v3 = _vertices[2];
        var y = Math.Max(0, Math.Min(h, v1.Y));
        if (y == v1.Y)
        {
            y++;
        }

        var midY = Math.Max(-1, Math.Min(h - 1, v2.Y));
        var endY = Math.Max(-1, Math.Min(h, v3.Y));
        for (; y <= midY; y++)
        {
            var start = (v2.X - v1.X) * (y - v1.Y) / (v2.Y - v1.Y) + v1.X;
            var end = (v3.X - v1.X) * (y - v1.Y) / (v3.Y - v1.Y) + v1.X;
            Span(framebuffer, (int)y * w, start, end, z, w, depth, colour, blend);
        }

        for (; y < endY; y++)
        {
            var start = (v3.X - v2.X) * (y - v2.Y) / (v3.Y - v2.Y) + v2.X;
            var end = (v3.X - v1.X) * (y - v1.Y) / (v3.Y - v1.Y) + v1.X;
            Span(framebuffer, (int)y * w, start, end, z, w, depth, colour, blend);
        }
    }

    private static void Span(int[] framebuffer, int row, long start, long end, uint z, int w, uint[]? depth, int colour, int blend)
    {
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if (end < 0 || start >= w)
        {
            return;
        }

        var from = row + (int)Math.Max(start, 0);
        var to = row + (int)Math.Min(end, w - 1);
        if (depth is not null)
        {
            for (var at = from; at < to; at++)
            {
                if (z >= depth[at])
                {
                    depth[at] = z;
                    framebuffer[at] = colour;
                }
            }
        }
        else if ((blend & 0xff) == 0)
        {
            framebuffer.AsSpan(from, Math.Max(0, to - from)).Fill(colour);
        }
        else
        {
            for (var at = from; at < to; at++)
            {
                framebuffer[at] = AvsBlend.Line(framebuffer[at], colour, blend);
            }
        }
    }

    private readonly record struct Vertex(long X, long Y);

    /// <summary>The depth buffer the preset's Triangles share, cleared when the size changes or code asks (zbclear).</summary>
    private sealed class DepthBuffer
    {
        public uint[] Buffer { get; private set; } = Array.Empty<uint>();

        public void ResetIfNeeded(int w, int h, bool clear)
        {
            if (Buffer.Length != w * h)
            {
                Buffer = new uint[w * h];
            }
            else if (clear)
            {
                Array.Clear(Buffer);
            }
        }
    }

    /// <summary>The four pieces of code, compiled the first time the effect draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(TriangleRender triangle)
        {
            _context = triangle.NewContext();
            W = _context.Index("w");
            H = _context.Index("h");
            N = _context.Index("n");
            I = _context.Index("i");
            Skip = _context.Index("skip");
            X1 = _context.Index("x1");
            Y1 = _context.Index("y1");
            Red1 = _context.Index("red1");
            Green1 = _context.Index("green1");
            Blue1 = _context.Index("blue1");
            X2 = _context.Index("x2");
            Y2 = _context.Index("y2");
            Red2 = _context.Index("red2");
            Green2 = _context.Index("green2");
            Blue2 = _context.Index("blue2");
            X3 = _context.Index("x3");
            Y3 = _context.Index("y3");
            Red3 = _context.Index("red3");
            Green3 = _context.Index("green3");
            Blue3 = _context.Index("blue3");
            Z1 = _context.Index("z1");
            ZBuf = _context.Index("zbuf");
            ZbClear = _context.Index("zbclear");
            Init = triangle.Compile(_context, triangle._code[0], "The Triangle's init code");
            Frame = triangle.Compile(_context, triangle._code[1], "The Triangle's per frame code");
            Beat = triangle.Compile(_context, triangle._code[2], "The Triangle's on beat code");
            Point = triangle.Compile(_context, triangle._code[3], "The Triangle's per triangle code");
        }

        public int W { get; }

        public int H { get; }

        public int N { get; }

        public int I { get; }

        public int Skip { get; }

        public int X1 { get; }

        public int Y1 { get; }

        public int Red1 { get; }

        public int Green1 { get; }

        public int Blue1 { get; }

        public int X2 { get; }

        public int Y2 { get; }

        public int Red2 { get; }

        public int Green2 { get; }

        public int Blue2 { get; }

        public int X3 { get; }

        public int Y3 { get; }

        public int Red3 { get; }

        public int Green3 { get; }

        public int Blue3 { get; }

        public int Z1 { get; }

        public int ZBuf { get; }

        public int ZbClear { get; }

        public EelCode Init { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Point { get; }

        public bool Initialised { get; set; }

        public double[] Values => _context.Values;
    }
}
