namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Color Map (Unconed's colormap.ape, e_colormap.cpp): looks each pixel up in a 256-colour
/// gradient, keyed by one of its channels or its brightness, and blends the result in one of ten ways.
/// Up to eight gradients can be stored and switched between on beats, fading from one to the next.
/// </summary>
[AvsEffect("Color Map")]
public sealed class ColorMap : AvsEffect
{
    private const int MapCount = 8;
    private const int AnimationSteps = 256;
    private const int BaseSize = 16;
    private const int MapHeaderSize = 12 + 48;
    private const int ColourSize = 12;

    private readonly bool[] _enabled = new bool[MapCount];
    private readonly int[][] _baked = new int[MapCount][];
    private readonly int[] _tween = new int[256];
    private readonly int[] _shown = new int[256];
    private int _key;
    private int _blendMode;
    private int _cycleMode;
    private int _alpha;
    private bool _dontSkipFastBeats;
    private int _cycleSpeed = 11;
    private int _currentMap;
    private int _nextMap;
    private int _animationStep = AnimationSteps;

    public ColorMap(AvsShared shared)
        : base(shared)
    {
        for (var i = 0; i < MapCount; i++)
        {
            _baked[i] = new int[256];
            Bake(_baked[i], new List<(int Position, int Colour)> { (0, 0x000000), (255, 0xffffff) });
        }

        _enabled[0] = true;
        Array.Copy(_baked[0], _tween, 256);
    }

    public override void Load(AvsConfigReader config)
    {
        if (config.Length < BaseSize)
        {
            return;
        }

        _key = config.ReadInt();
        _blendMode = config.ReadInt();
        _cycleMode = config.ReadInt();
        _alpha = config.ReadByte();
        config.ReadByte(); // unused
        _dontSkipFastBeats = config.ReadByte() != 0;
        _cycleSpeed = config.ReadByte();

        // Eight headers (enabled, number of colours, an id, and a 48-byte file name), then each map's colours.
        var lengths = new long[MapCount];
        for (var map = 0; map < MapCount; map++)
        {
            var at = BaseSize + map * MapHeaderSize;
            if (config.Length - at < MapHeaderSize)
            {
                continue;
            }

            config.Position = at;
            _enabled[map] = config.ReadInt() != 0;
            lengths[map] = (uint)config.ReadInt();
        }

        var position = BaseSize + MapCount * MapHeaderSize;
        var foundEnabled = false;
        for (var map = 0; map < MapCount; map++)
        {
            var colours = new List<(int Position, int Colour)>();
            var complete = true;
            for (long i = 0; i < lengths[map]; i++)
            {
                if (config.Length - position < ColourSize)
                {
                    complete = false;
                    break;
                }

                config.Position = position;
                var at = config.ReadInt();
                var colour = config.ReadInt();
                position += ColourSize;
                colours.Add((at, colour));
            }

            if (!complete)
            {
                // As the plug-in: a map cut short keeps its old gradient, and the maps after it aren't read.
                break;
            }

            Bake(_baked[map], colours);

            if (_enabled[map] && !foundEnabled)
            {
                _currentMap = map;
                foundEnabled = true;
            }
        }

        if (!foundEnabled)
        {
            _currentMap = 0;
        }

        _nextMap = _currentMap;
        Array.Copy(_baked[_currentMap], _tween, 256);
    }

    /// <summary>Spreads a map's colours over 256 steps, fading between neighbours (an empty map keeps its old gradient).</summary>
    private static void Bake(int[] baked, List<(int Position, int Colour)> colours)
    {
        if (colours.Count == 0)
        {
            return;
        }

        // By position, then by colour where two share one (as the plug-in sorted them).
        colours.Sort((a, b) => a.Position != b.Position ? a.Position.CompareTo(b.Position) : (a.Colour & 0xffffff).CompareTo(b.Colour & 0xffffff));
        var index = 0;
        var first = colours[0];
        for (; index < Math.Clamp(first.Position, 0, 256); index++)
        {
            baked[index] = first.Colour & 0xffffff;
        }

        for (var c = 0; c < colours.Count - 1; c++)
        {
            var from = colours[c];
            var to = colours[c + 1];
            var span = to.Position - from.Position;
            for (; index < Math.Clamp(to.Position, 0, 256); index++)
            {
                var lerp = (int)(256 * (float)(index - from.Position) / span);
                baked[index] = AvsBlend.Adjust(to.Colour & 0xffffff, from.Colour & 0xffffff, lerp & 0xff);
            }
        }

        var last = colours[^1].Colour & 0xffffff;
        for (; index < 256; index++)
        {
            baked[index] = last;
        }
    }

    /// <summary>The map to draw with this frame: the current one, or on its way to the next.</summary>
    private int[] MapThisFrame(bool isBeat)
    {
        _nextMap = ((_nextMap % MapCount) + MapCount) % MapCount;
        _currentMap = ((_currentMap % MapCount) + MapCount) % MapCount;
        if (_cycleMode == 0)
        {
            _animationStep = 0;
            return _baked[_currentMap];
        }

        _animationStep = Math.Min(_animationStep + _cycleSpeed, AnimationSteps);
        if (isBeat && (!_dontSkipFastBeats || _animationStep == AnimationSteps))
        {
            if (Array.IndexOf(_enabled, true) >= 0)
            {
                do
                {
                    _nextMap = _cycleMode == 1 ? Shared.Random.Next(MapCount) : (_nextMap + 1) % MapCount;
                }
                while (!_enabled[_nextMap]);
            }

            _animationStep = 0;
        }

        if (_animationStep == AnimationSteps)
        {
            _currentMap = _nextMap;
        }

        if (_animationStep == 0 || _animationStep == AnimationSteps || _currentMap == _nextMap)
        {
            Array.Copy(_baked[_currentMap], _tween, 256);
        }
        else
        {
            int[] from = _baked[_currentMap], to = _baked[_nextMap];
            var step = _animationStep & 0xff;
            for (var i = 0; i < 256; i++)
            {
                _tween[i] = AvsBlend.Adjust(to[i], from[i], step);
            }
        }

        return _tween;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var mode = _blendMode;
        if (mode is < 0 or > 9)
        {
            return 0;
        }

        // A copy, so the rows drawn in parallel all see the same map.
        Array.Copy(MapThisFrame(context.IsBeat), _shown, 256);
        var map = _shown;
        var key = _key;
        var alpha = Math.Clamp(_alpha, 0, 255);
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var p = framebuffer[at];
                int r = (p >> 16) & 0xff, g = (p >> 8) & 0xff, b = p & 0xff;
                var index = key switch
                {
                    0 => r,
                    1 => g,
                    2 => b,
                    3 => Math.Min((r + g + b) / 2, 255),
                    4 => Math.Max(r, Math.Max(g, b)),
                    _ => (r + g + b) / 3,
                };
                framebuffer[at] = Blend(p & 0xffffff, map[index], mode, alpha);
            }
        });
        return 0;
    }

    private static int Blend(int under, int colour, int mode, int alpha) => mode switch
    {
        0 => colour,
        1 => AvsBlend.Add(under, colour),
        2 => AvsBlend.Max(under, colour),
        3 => AvsBlend.Min(under, colour),
        4 => Half(under, colour),
        5 => AvsBlend.Subtract(under, colour),
        6 => AvsBlend.Subtract(colour, under),
        7 => Times(under, colour),
        8 => under ^ colour,
        _ => Mix(under, colour, alpha),
    };

    // The plug-in's own maths for 50/50 (rounding up), multiply and adjustable (both dividing by 256),
    // which aren't quite AVS's usual blends.
    private static int Half(int a, int b) =>
        (((a & 0xff) + (b & 0xff) + 1) >> 1)
        | ((((a >> 8) & 0xff) + ((b >> 8) & 0xff) + 1) >> 1 << 8)
        | ((((a >> 16) & 0xff) + ((b >> 16) & 0xff) + 1) >> 1 << 16);

    private static int Times(int a, int b) =>
        (((a & 0xff) * (b & 0xff)) >> 8)
        | ((((a >> 8) & 0xff) * ((b >> 8) & 0xff)) >> 8 << 8)
        | ((((a >> 16) & 0xff) * ((b >> 16) & 0xff)) >> 8 << 16);

    private static int Mix(int a, int b, int v)
    {
        var u = 255 - v;
        return (((a & 0xff) * u + (b & 0xff) * v) >> 8)
            | (((((a >> 8) & 0xff) * u + ((b >> 8) & 0xff) * v) >> 8) << 8)
            | (((((a >> 16) & 0xff) * u + ((b >> 16) & 0xff) * v) >> 8) << 16);
    }
}
