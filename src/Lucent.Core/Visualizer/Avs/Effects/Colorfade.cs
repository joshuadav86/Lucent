namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Colorfade (r_colorfade.cpp): shifts each pixel's channels up or down by amounts that depend on
/// which of its channels is strongest, fading colours towards (or away from) each other. The amounts can
/// jump on beats, to set or random values, and glide back.
/// </summary>
[AvsEffect(11)]
public sealed class Colorfade : AvsEffect
{
    private readonly int[] _faders = { 8, -8, -8 };
    private readonly int[] _beatFaders = { 8, -8, -8 };
    private readonly int[] _faderPos = { 8, -8, -8 };

    // For each of the four kinds of pixel, a 256-entry table per channel (blue byte, green, red).
    private readonly byte[] _table = new byte[4 * 3 * 256];
    private int _enabled = 1;

    public Colorfade(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        for (var i = 0; i < 3; i++)
        {
            _faders[i] = config.ReadInt(_faders[i]);
        }

        Array.Copy(_faders, _beatFaders, 3);
        for (var i = 0; i < 3; i++)
        {
            _beatFaders[i] = config.ReadInt(_beatFaders[i]);
        }

        Array.Copy(_faders, _faderPos, 3);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        // As AVS: the second and third positions glide towards each other's targets.
        Step(0, _faders[0]);
        Step(1, _faders[2]);
        Step(2, _faders[1]);

        if ((_enabled & 4) == 0)
        {
            Array.Copy(_faders, _faderPos, 3);
        }
        else if (context.IsBeat && (_enabled & 2) != 0)
        {
            var random = Shared.Random;
            _faderPos[0] = random.Next(32) - 6;
            _faderPos[1] = random.Next(64) - 32;
            if (_faderPos[1] is < 0 and > -16)
            {
                _faderPos[1] = -32;
            }

            if (_faderPos[1] is >= 0 and < 16)
            {
                _faderPos[1] = 32;
            }

            _faderPos[2] = random.Next(32) - 6;
        }
        else if (context.IsBeat)
        {
            Array.Copy(_beatFaders, _faderPos, 3);
        }

        int fs1 = _faderPos[0], fs2 = _faderPos[1], fs3 = _faderPos[2];
        Fill(0, fs3, fs2, fs1);
        Fill(1, fs2, fs1, fs3);
        Fill(2, fs1, fs3, fs2);
        Fill(3, fs3, fs3, fs3);

        var table = _table;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                int r = c & 0xff, g = (c >> 8) & 0xff, b = (c >> 16) & 0xff;
                int xp = g - b, yp = b - r;
                int kind = xp > 0 && xp > -yp ? 0
                    : yp < 0 && xp < -yp ? 1
                    : xp < 0 && yp > 0 ? 2
                    : 3;
                var t = kind * 768;
                framebuffer[at] = table[t + r] | (table[t + 256 + g] << 8) | (table[t + 512 + b] << 16);
            }
        });
        return 0;
    }

    private void Step(int i, int target)
    {
        if (_faderPos[i] < target)
        {
            _faderPos[i]++;
        }

        if (_faderPos[i] > target)
        {
            _faderPos[i]--;
        }
    }

    private void Fill(int kind, int d0, int d1, int d2)
    {
        var t = kind * 768;
        for (var x = 0; x < 256; x++)
        {
            _table[t + x] = (byte)Math.Clamp(x + d0, 0, 255);
            _table[t + 256 + x] = (byte)Math.Clamp(x + d1, 0, 255);
            _table[t + 512 + x] = (byte)Math.Clamp(x + d2, 0, 255);
        }
    }
}
