namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Timescope (r_timescope.cpp): draws the spectrum as one column of shades each frame, the column sweeping across the picture over time.</summary>
[AvsEffect(39)]
public sealed class Timescope : AvsEffect
{
    private int _enabled = 1;
    private int _colour = 0xffffff;
    private int _blend = 2;
    private int _blendAverage;
    private int _bands = 576;
    private int _x;

    public Timescope(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        config.ReadInt(); // The channel, which AVS saved but didn't use.
        _bands = config.ReadInt(_bands);
    }

    public override void Restart() => _x = 0;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        _x++;
        _x %= w;

        // 2 the line blend mode, 1 additive; otherwise 50/50 or replace.
        var mode = _blend == 2 ? Shared.LineBlendMode : _blend == 1 ? 1 : _blendAverage != 0 ? 3 : 0;
        int r = _colour & 0xff, g = (_colour >> 8) & 0xff, b = (_colour >> 16) & 0xff;
        var audio = context.Audio;

        // As in AVS, the column always shows the left channel's spectrum, whichever channel is chosen.
        for (var i = 0; i < h; i++)
        {
            var band = (int)Math.Clamp((long)i * _bands / h, 0, AvsAudio.Samples - 1);
            var c = audio.Spectrum(0, band);
            c = r * c / 256 + ((g * c / 256) << 8) + ((b * c / 256) << 16);
            var at = i * w + _x;
            framebuffer[at] = AvsBlend.Line(framebuffer[at], c, mode);
        }

        return 0;
    }
}
