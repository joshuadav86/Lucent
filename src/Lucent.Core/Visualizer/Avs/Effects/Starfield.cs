namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Starfield (r_stars.cpp): stars flying out from the centre toward the viewer, optionally speeding up on beats.</summary>
[AvsEffect(27)]
public sealed class Starfield : AvsEffect
{
    private readonly Star[] _stars = new Star[4096];
    private int _enabled = 1;
    private int _colour = 0xffffff;
    private int _starCount = 350;
    private int _starsSetting = 350;
    private int _xOffset;
    private int _yOffset;
    private int _zOffset = 255;
    private float _warpSpeed = 6;
    private float _currentSpeed = 6;
    private int _blend;
    private int _blendAverage;
    private int _width;
    private int _height;
    private int _onBeat;
    private float _beatSpeed = 4;
    private float _speedStep;
    private int _beatFrames = 15;
    private int _framesLeft;

    public Starfield(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _warpSpeed = config.ReadFloat(_warpSpeed);
        _currentSpeed = _warpSpeed;
        _starsSetting = config.ReadInt(_starsSetting);
        _onBeat = config.ReadInt(_onBeat);
        _beatSpeed = config.ReadFloat(_beatSpeed);
        _beatFrames = config.ReadInt(_beatFrames);
    }

    public override void Restart()
    {
        // Scatter the stars afresh on the next frame.
        _width = 0;
        _height = 0;
        _framesLeft = 0;
        _currentSpeed = _warpSpeed;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_onBeat != 0 && context.IsBeat)
        {
            _currentSpeed = _beatSpeed;
            _speedStep = (_warpSpeed - _currentSpeed) / _beatFrames;
            _framesLeft = _beatFrames;
        }

        if (_width != w || _height != h)
        {
            _width = w;
            _height = h;
            _xOffset = _width / 2;
            _yOffset = _height / 2;
            InitializeStars();
        }

        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 3 : 0;
        var dotSize = RenderHelpers.DotSize(context);
        for (var i = 0; i < _starCount; i++)
        {
            ref var star = ref _stars[i];
            if ((int)star.Z > 0)
            {
                var nx = (star.X << 7) / (int)star.Z + _xOffset;
                var ny = (star.Y << 7) / (int)star.Z + _yOffset;
                if (nx > 0 && nx < w && ny > 0 && ny < h)
                {
                    var c = (int)((255 - (int)star.Z) * star.Speed);
                    c = _colour != 0xffffff ? AdaptColour(c | (c << 8) | (c << 16), _colour, c >> 4) : c | (c << 8) | (c << 16);
                    AvsDraw.Dot(framebuffer, nx, ny, w, h, c, dotSize, mode);
                    star.Z -= star.Speed * _currentSpeed;
                }
                else
                {
                    CreateStar(ref star);
                }
            }
            else
            {
                CreateStar(ref star);
            }
        }

        if (_framesLeft == 0)
        {
            _currentSpeed = _warpSpeed;
        }
        else
        {
            _currentSpeed = Math.Max(0, _currentSpeed + _speedStep);
            _framesLeft--;
        }

        return 0;
    }

    /// <summary>BLEND_ADAPT: a mix of a grey and the star colour, in sixteenths.</summary>
    private static int AdaptColour(int a, int b, int divisor) =>
        (int)((((uint)a >> 4) & 0x0F0F0F) * (uint)(16 - divisor) + (((uint)b >> 4) & 0x0F0F0F) * (uint)divisor);

    private void InitializeStars()
    {
        // MulDiv: rounded.
        _starCount = (int)Math.Clamp(((long)_starsSetting * _width * _height + 512 * 384 / 2) / (512 * 384), int.MinValue, int.MaxValue);
        if (_starCount > 4095)
        {
            _starCount = 4095;
        }

        var random = Shared.Random;
        for (var i = 0; i < _starCount; i++)
        {
            ref var star = ref _stars[i];
            star.X = random.Next(_width) - _xOffset;
            star.Y = random.Next(_height) - _yOffset;
            star.Z = random.Next(255);
            star.Speed = (random.Next(9) + 1) / 10f;
        }
    }

    private void CreateStar(ref Star star)
    {
        star.X = Shared.Random.Next(_width) - _xOffset;
        star.Y = Shared.Random.Next(_height) - _yOffset;
        star.Z = _zOffset;
    }

    private struct Star
    {
        public int X;
        public int Y;
        public float Z;
        public float Speed;
    }
}
