namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Fadeout (r_fadeout.cpp): moves every channel of every pixel a step closer to a colour (black by default).</summary>
[AvsEffect(3)]
public sealed class Fadeout : AvsEffect
{
    private readonly byte[] _table = new byte[3 * 256];
    private int _length = 16;
    private int _colour;

    public Fadeout(AvsShared shared)
        : base(shared)
    {
        MakeTable();
    }

    public override void Load(AvsConfigReader config)
    {
        _length = config.ReadInt(_length);
        _colour = config.ReadInt(_colour);
        MakeTable();
    }

    private void MakeTable()
    {
        for (var channel = 0; channel < 3; channel++)
        {
            var seek = (_colour >> (channel * 8)) & 0xff;
            for (var x = 0; x < 256; x++)
            {
                var value = x <= seek - _length ? x + _length : x >= seek + _length ? x - _length : seek;
                _table[channel * 256 + x] = (byte)value;
            }
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_length == 0)
        {
            return 0;
        }

        var table = _table;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                framebuffer[at] = table[c & 0xff] | (table[256 + ((c >> 8) & 0xff)] << 8) | (table[512 + ((c >> 16) & 0xff)] << 16);
            }
        });
        return 0;
    }
}
