namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Multi Filter (Jheriko's MULTIFILTER, e_multifilter.cpp): "chrome" (each channel folded so middle
/// levels are brightest, once, twice, or three times), or every lit pixel turned white with a thin border.
/// </summary>
[AvsEffect("Jheriko : MULTIFILTER")]
public sealed class MultiFilter : AvsEffect
{
    private const int BorderMode = 3;

    private static readonly byte[] Chrome = MakeChrome();

    private bool _enabled = true;
    private int _effect;
    private bool _toggleOnBeat;
    private bool _toggledOn;

    public MultiFilter(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        if (config.HasInt)
        {
            _enabled = config.ReadInt() != 0;
        }

        if (config.HasInt)
        {
            var effect = config.ReadInt();
            _effect = effect is >= 0 and <= BorderMode ? effect : 0;
        }

        if (config.HasInt)
        {
            _toggleOnBeat = config.ReadInt() != 0;
        }
    }

    public override void Restart()
    {
        _toggledOn = false;
    }

    private static byte[] MakeChrome()
    {
        var table = new byte[256];
        for (var c = 0; c < 256; c++)
        {
            table[c] = (byte)(c < 128 ? c * 2 : 510 - c * 2);
        }

        return table;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (!_enabled)
        {
            return 0;
        }

        if (_toggleOnBeat)
        {
            if (context.IsBeat)
            {
                _toggledOn = !_toggledOn;
            }

            if (!_toggledOn)
            {
                return 0;
            }
        }

        int w = context.Width, h = context.Height;
        if (_effect == BorderMode)
        {
            // Done in place in the plug-in: a lit pixel lights itself and the pixels left of and above it.
            context.ForEachRow(y =>
            {
                for (int x = 0, at = y * w; x < w; x++, at++)
                {
                    var lit = (framebuffer[at] & 0xffffff) != 0
                        || (x + 1 < w && (framebuffer[at + 1] & 0xffffff) != 0)
                        || (y + 1 < h && (framebuffer[at + w] & 0xffffff) != 0);
                    fbout[at] = lit ? 0xffffff : 0;
                }
            });
            return Swapped;
        }

        var times = _effect + 1;
        var chrome = Chrome;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                int b = c & 0xff, g = (c >> 8) & 0xff, r = (c >> 16) & 0xff;
                for (var i = 0; i < times; i++)
                {
                    b = chrome[b];
                    g = chrome[g];
                    r = chrome[r];
                }

                framebuffer[at] = b | (g << 8) | (r << 16);
            }
        });
        return 0;
    }
}
