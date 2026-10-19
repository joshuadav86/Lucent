namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Mosaic (r_mosaic.cpp): turns the picture into big square-ish blocks of one colour, how many
/// across set by the quality, which can drop on a beat and recover over some frames.
/// </summary>
[AvsEffect(30)]
public sealed class Mosaic : AvsEffect
{
    private int _enabled = 1;
    private int _quality = 50;
    private int _beatQuality;
    private int _blend;
    private int _blendAverage;
    private int _onBeat;
    private int _beatFrames = 15;
    private int _framesLeft;
    private int _thisQuality;
    private int[] _sourceColumn = Array.Empty<int>();
    private int[] _sourceRow = Array.Empty<int>();

    public Mosaic(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _quality = config.ReadInt(_quality);
        _beatQuality = config.ReadInt(_beatQuality);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _onBeat = config.ReadInt(_onBeat);
        _beatFrames = config.ReadInt(_beatFrames);
    }

    public override void Restart()
    {
        _framesLeft = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        if (_onBeat != 0 && context.IsBeat)
        {
            _thisQuality = _beatQuality;
            _framesLeft = _beatFrames;
        }
        else if (_framesLeft == 0)
        {
            _thisQuality = _quality;
        }

        var result = 0;
        if (_thisQuality < 100)
        {
            Draw(context, framebuffer, fbout, Math.Max(1, _thisQuality));
            result = Swapped;
        }

        if (_framesLeft != 0)
        {
            _framesLeft--;
            if (_framesLeft != 0)
            {
                var step = Math.Abs(_quality - _beatQuality) / _beatFrames;
                _thisQuality += step * (_beatQuality > _quality ? -1 : 1);
            }
        }

        return result;
    }

    private void Draw(AvsRenderContext context, int[] framebuffer, int[] fbout, int quality)
    {
        int w = context.Width, h = context.Height;
        if (_sourceColumn.Length != w)
        {
            _sourceColumn = new int[w];
        }

        if (_sourceRow.Length != h)
        {
            _sourceRow = new int[h];
        }

        // Which pixel each column and row takes its colour from, stepping in 16.16 fixed point as AVS did.
        var stepX = (int)((long)w * 65536 / quality);
        var stepY = (int)((long)h * 65536 / quality);
        var columns = _sourceColumn;
        var xpos = stepX >> 17;
        var dpos = 0;
        for (var x = 0; x < w; x++)
        {
            columns[x] = Math.Min(xpos, w - 1);
            dpos += 1 << 16;
            if (dpos >= stepX)
            {
                xpos += dpos >> 16;
                dpos -= stepX;
            }
        }

        var rows = _sourceRow;
        var ypos = stepY >> 17;
        dpos = 0;
        for (var y = 0; y < h; y++)
        {
            rows[y] = Math.Min(ypos, h - 1);
            dpos += 1 << 16;
            if (dpos >= stepY)
            {
                ypos += dpos >> 16;
                dpos -= stepY;
            }
        }

        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
        context.ForEachRow(y =>
        {
            var source = rows[y] * w;
            for (int x = 0, at = y * w; x < w; x++, at++)
            {
                var colour = framebuffer[source + columns[x]];
                fbout[at] = mode switch
                {
                    1 => AvsBlend.Add(framebuffer[at], colour),
                    2 => AvsBlend.Average(framebuffer[at], colour),
                    _ => colour,
                };
            }
        });
    }
}
