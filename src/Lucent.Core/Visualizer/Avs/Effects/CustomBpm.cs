namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Misc / Custom BPM (r_bpm.cpp): changes which frames count as beats for the effects after it: a steady beat at a set rate, only every few beats, or every frame that isn't a beat.</summary>
[AvsEffect(33)]
public sealed class CustomBpm : AvsEffect
{
    private int _enabled = 1;
    private int _arbitrary = 1;
    private int _skip;
    private int _invert;
    private int _arbitraryMilliseconds = 500;
    private int _skipValue = 1;
    private int _skipFirst;
    private int _count;
    private int _skipCount;
    private double _lastBeat = double.NaN;

    public CustomBpm(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _arbitrary = config.ReadInt(_arbitrary);
        _skip = config.ReadInt(_skip);
        _invert = config.ReadInt(_invert);
        _arbitraryMilliseconds = config.ReadInt(_arbitraryMilliseconds);
        _skipValue = config.ReadInt(_skipValue);
        _skipFirst = config.ReadInt(_skipFirst);
    }

    public override void Restart()
    {
        _count = 0;
        _skipCount = 0;
        _lastBeat = double.NaN;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        // AVS started the steady beat's clock when the effect was made.
        var now = Shared.Time * 1000.0;
        if (double.IsNaN(_lastBeat))
        {
            _lastBeat = now;
        }

        if (_enabled == 0)
        {
            return 0;
        }

        var isBeat = context.IsBeat;
        if (isBeat)
        {
            _count++;
        }

        if (_skipFirst != 0 && _count <= _skipFirst)
        {
            return isBeat ? ClearBeat : 0;
        }

        if (_arbitrary != 0)
        {
            if (now > _lastBeat + _arbitraryMilliseconds)
            {
                _lastBeat = now;
                return SetBeat;
            }

            return ClearBeat;
        }

        if (_skip != 0)
        {
            if (isBeat && ++_skipCount >= _skipValue + 1)
            {
                _skipCount = 0;
                return SetBeat;
            }

            return ClearBeat;
        }

        if (_invert != 0)
        {
            return isBeat ? ClearBeat : SetBeat;
        }

        return 0;
    }
}
