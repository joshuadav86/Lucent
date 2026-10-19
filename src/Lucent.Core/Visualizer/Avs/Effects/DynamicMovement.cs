using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Dynamic Movement (r_dmove.cpp): the preset's code says, each frame, where the picture moves at the
/// points of a coarse grid, and every pixel moves by what the grid points around it say.
/// </summary>
[AvsEffect(43)]
public sealed class DynamicMovement : AvsEffect
{
    private readonly string[] _code = { string.Empty, string.Empty, string.Empty, string.Empty };
    private int _subpixel = 1;
    private int _rectangular;
    private int _gridWidth = 16;
    private int _gridHeight = 16;
    private int _blend;
    private int _wrap;
    private int _buffer;
    private int _noMove;
    private Compiled? _compiled;

    // Each grid point's source x and y (16.16 fixed point, in pixels) and alpha (0..255 in 16.16).
    private int[] _grid = Array.Empty<int>();

    // Each picture row's grid row, and how many rows into that grid row it is.
    private int[] _rowCell = Array.Empty<int>();
    private int[] _rowStep = Array.Empty<int>();

    public DynamicMovement(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        if (config[0] == 1)
        {
            config.Position = 1;
            for (var i = 0; i < 4; i++)
            {
                _code[i] = config.ReadString();
            }
        }
        else if (config.Remaining >= 1024)
        {
            // Older presets: point, frame, beat, and init code in 256 bytes each.
            for (var i = 0; i < 4; i++)
            {
                _code[i] = config.ReadFixedString(256);
            }
        }

        _subpixel = config.ReadInt(_subpixel);
        _rectangular = config.ReadInt(_rectangular);
        _gridWidth = config.ReadInt(_gridWidth);
        _gridHeight = config.ReadInt(_gridHeight);
        _blend = config.ReadInt(_blend);
        _wrap = config.ReadInt(_wrap);
        _buffer = config.ReadInt(0);
        _noMove = config.ReadInt(0);
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
        if (w < 3 || h < 3)
        {
            return 0;
        }

        var fbin = _buffer == 0 ? framebuffer : Shared.Buffer(_buffer - 1, w, h, false);
        if (fbin is null)
        {
            return 0;
        }

        var xres = Math.Clamp(_gridWidth + 1, 2, 256);
        var yres = Math.Clamp(_gridHeight + 1, 2, 256);
        if (_grid.Length != xres * yres * 3)
        {
            _grid = new int[xres * yres * 3];
        }

        var subpixel = _subpixel != 0;
        var wAdjust = (subpixel ? w - 2 : w - 1) << 16;
        var hAdjust = (subpixel ? h - 2 : h - 1) << 16;

        var v = code.Values;
        v[code.W] = w;
        v[code.H] = h;
        v[code.B] = context.IsBeat ? 1.0 : 0.0;
        v[code.Alpha] = 0.5;
        if (code.HasInit && !code.Initialised)
        {
            code.Init.Run();
            code.Initialised = true;
        }

        code.Frame.Run();
        if (context.IsBeat)
        {
            code.Beat.Run();
        }

        var xStep = (w << 16) / (xres - 1);
        var yStep = (h << 16) / (yres - 1);
        FillGrid(code, w, h, xres, yres, xStep, yStep, wAdjust, hAdjust);

        // Which grid row each picture row falls in. Rows past the grid's last line (from rounding) carry on from the last grid row.
        if (_rowCell.Length != h)
        {
            _rowCell = new int[h];
            _rowStep = new int[h];
        }

        for (var cell = 0; cell < yres - 1; cell++)
        {
            var top = (cell * yStep) >> 16;
            var bottom = cell == yres - 2 ? h : ((cell + 1) * yStep) >> 16;
            for (var y = Math.Max(0, top); y < Math.Min(bottom, h); y++)
            {
                _rowCell[y] = cell;
                _rowStep[y] = y - top;
            }
        }

        var grid = _grid;
        var rowCell = _rowCell;
        var rowStep = _rowStep;
        var blend = _blend != 0;
        var wrap = _wrap != 0;
        var noMove = _noMove != 0;
        context.ForEachRow(y =>
        {
            Span<int> row = stackalloc int[xres * 3];
            var cell = rowCell[y];
            var k = rowStep[y];
            var span = Math.Max(1, (((cell + 1) * yStep) >> 16) - ((cell * yStep) >> 16));
            var above = cell * xres * 3;
            var below = above + xres * 3;
            for (var i = 0; i < xres * 3; i++)
            {
                var start = grid[above + i];
                row[i] = start + k * ((grid[below + i] - start) / span);
            }

            var at = y * w;
            var left = w;
            var position = 0;
            for (var column = 0; column < xres - 1 && left > 0; column++)
            {
                position += xStep;
                var seek = (position >> 16) - ((position - xStep) >> 16);
                var count = column == xres - 2 ? left : Math.Min(seek, left);
                if (count <= 0)
                {
                    continue;
                }

                seek = Math.Max(1, seek);
                var xp = row[column * 3];
                var yp = row[column * 3 + 1];
                var ap = row[column * 3 + 2];
                var dx = (row[column * 3 + 3] - xp) / seek;
                var dy = (row[column * 3 + 4] - yp) / seek;
                var da = (row[column * 3 + 5] - ap) / seek;
                left -= count;
                if (noMove)
                {
                    for (var end = at + count; at < end; at++)
                    {
                        framebuffer[at] = AvsBlend.Adjust(fbin != framebuffer ? fbin[at] : 0, framebuffer[at], Alpha(ap));
                        ap += da;
                    }

                    continue;
                }

                if (wrap)
                {
                    xp %= wAdjust;
                    yp %= hAdjust;
                    if (xp < 0)
                    {
                        xp += wAdjust;
                    }

                    if (yp < 0)
                    {
                        yp += hAdjust;
                    }

                    dx = Math.Clamp(dx, -wAdjust + 1, wAdjust - 1);
                    dy = Math.Clamp(dy, -hAdjust + 1, hAdjust - 1);
                }

                for (var end = at + count; at < end; at++)
                {
                    if (wrap)
                    {
                        if (xp < 0)
                        {
                            xp += wAdjust;
                        }
                        else if (xp >= wAdjust)
                        {
                            xp -= wAdjust;
                        }

                        if (yp < 0)
                        {
                            yp += hAdjust;
                        }
                        else if (yp >= hAdjust)
                        {
                            yp -= hAdjust;
                        }
                    }
                    else
                    {
                        if (xp < 0)
                        {
                            xp = 0;
                        }
                        else if (xp >= wAdjust)
                        {
                            xp = wAdjust - 1;
                        }

                        if (yp < 0)
                        {
                            yp = 0;
                        }
                        else if (yp >= hAdjust)
                        {
                            yp = hAdjust - 1;
                        }
                    }

                    var from = (xp >> 16) + (yp >> 16) * w;
                    var colour = subpixel ? MoveHelpers.Bilinear(fbin, from, w, (xp >> 8) & 0xff, (yp >> 8) & 0xff) : fbin[from];
                    if (blend)
                    {
                        colour = AvsBlend.Adjust(colour, framebuffer[at], Alpha(ap));
                        ap += da;
                    }

                    fbout[at] = colour;
                    xp += dx;
                    yp += dy;
                }
            }
        });

        return noMove ? 0 : Swapped;
    }

    private static int Alpha(int ap) => Math.Clamp(ap >> 16, 0, 255);

    /// <summary>Runs the point code at every grid point (in order, as AVS did, since the code can keep values from one point to the next).</summary>
    private void FillGrid(Compiled code, int w, int h, int xres, int yres, int xStep, int yStep, int wAdjust, int hAdjust)
    {
        var v = code.Values;
        double xsc = 2.0 / w, ysc = 2.0 / h;
        var dw2 = w * 32768.0;
        var dh2 = h * 32768.0;
        var maxScreenD = Math.Sqrt((double)(w * w + h * h)) * 0.5;
        var divMaxD = 1.0 / maxScreenD;
        maxScreenD *= 65536.0;
        var rectangular = _rectangular != 0;
        var wrap = _wrap != 0;
        var grid = _grid;
        var at = 0;
        var yPosition = 0;
        for (var y = 0; y < yres; y++)
        {
            var xPosition = 0;
            for (var x = 0; x < xres; x++)
            {
                var xd = (xPosition - dw2) * (1.0 / 65536.0);
                var yd = (yPosition - dh2) * (1.0 / 65536.0);
                xPosition += xStep;
                v[code.X] = xd * xsc;
                v[code.Y] = yd * ysc;
                v[code.D] = Math.Sqrt(xd * xd + yd * yd) * divMaxD;
                v[code.R] = Math.Atan2(yd, xd) + Math.PI * 0.5;
                code.Point.Run();
                int tmp1, tmp2;
                if (!rectangular)
                {
                    var d = v[code.D] * maxScreenD;
                    var r = v[code.R] - Math.PI * 0.5;
                    tmp1 = (int)(dw2 + Math.Cos(r) * d);
                    tmp2 = (int)(dh2 + Math.Sin(r) * d);
                }
                else
                {
                    tmp1 = (int)((v[code.X] + 1.0) * dw2);
                    tmp2 = (int)((v[code.Y] + 1.0) * dh2);
                }

                if (!wrap)
                {
                    tmp1 = Math.Clamp(tmp1, 0, wAdjust);
                    tmp2 = Math.Clamp(tmp2, 0, hAdjust);
                }

                var alpha = Math.Clamp(v[code.Alpha], 0.0, 1.0);
                grid[at++] = tmp1;
                grid[at++] = tmp2;
                grid[at++] = (int)(alpha * 255.0 * 65536.0);
            }

            yPosition += yStep;
        }
    }

    /// <summary>The effect's four pieces of code, compiled the first time it draws, and its variables.</summary>
    private sealed class Compiled
    {
        private readonly EelContext _context;

        public Compiled(DynamicMovement effect)
        {
            _context = AvsScript.NewContext(effect.Shared);
            D = _context.Index("d");
            B = _context.Index("b");
            R = _context.Index("r");
            X = _context.Index("x");
            Y = _context.Index("y");
            W = _context.Index("w");
            H = _context.Index("h");
            Alpha = _context.Index("alpha");
            Point = MoveHelpers.Compile(effect, _context, effect._code[0], "The Dynamic Movement's per point code", out _);
            Frame = MoveHelpers.Compile(effect, _context, effect._code[1], "The Dynamic Movement's per frame code", out _);
            Beat = MoveHelpers.Compile(effect, _context, effect._code[2], "The Dynamic Movement's on beat code", out _);
            Init = MoveHelpers.Compile(effect, _context, effect._code[3], "The Dynamic Movement's init code", out var hasInit);
            HasInit = hasInit;
        }

        public int D { get; }

        public int B { get; }

        public int R { get; }

        public int X { get; }

        public int Y { get; }

        public int W { get; }

        public int H { get; }

        public int Alpha { get; }

        public EelCode Point { get; }

        public EelCode Frame { get; }

        public EelCode Beat { get; }

        public EelCode Init { get; }

        public bool HasInit { get; }

        public bool Initialised { get; set; }

        /// <summary>The variables, read fresh each time since compiling can make room for more.</summary>
        public double[] Values => _context.Values;
    }
}
