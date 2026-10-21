namespace WinampInspiredVisualizer.Visualizer.MilkDrop;

/// <summary>What a MilkDrop preset is told about the frame it's drawing.</summary>
/// <param name="Time">Seconds since the visualizer started.</param>
/// <param name="DeltaTime">Seconds since the last frame.</param>
/// <param name="Progress">How far through its turn the preset is, 0..1 (MilkDrop's progress).</param>
/// <param name="Width">The size of the image it draws into, in pixels.</param>
/// <param name="Height">The size of the image it draws into, in pixels.</param>
/// <param name="MeshWidth">How many cells across the warp mesh has (MilkDrop's meshx).</param>
/// <param name="MeshHeight">How many cells down the warp mesh has (MilkDrop's meshy).</param>
public readonly record struct MilkFrameInput(double Time, double DeltaTime, double Progress, int Width, int Height, int MeshWidth, int MeshHeight);

/// <summary>
/// Runs a MilkDrop preset on the CPU, one frame at a time, the way MilkDrop does: the per-frame
/// equations, then the per-pixel equations at every point of the warp mesh (working out where each
/// point reads last frame's image from), then the custom shapes, custom waves, the built-in wave,
/// borders and motion vectors as triangles to draw over the warped image. It also fills the
/// constants the preset's shaders read (q1..q32, time, bass...). Drawing it all is up to the
/// renderer: see <see cref="Mesh"/>, <see cref="Geometry"/> and <see cref="Constants"/>.
/// Equations that don't compile are left out (and described in <see cref="Warnings"/>); the rest
/// of the preset still runs.
/// </summary>
public sealed class MilkPresetRunner
{
    /// <summary>Float4 registers in the shader constants; matches the cbuffer in Shaders/MilkDrop.hlsli.</summary>
    public const int ConstantRegisters = 124;

    /// <summary>
    /// How often a preset moves on a frame. MilkDrop's equations count frames, not seconds (a counter
    /// that changes colour every frame, a stretch applied every frame), so on a 144 Hz screen they ran
    /// more than twice as fast as written and some turned into strobes; between steps the last image
    /// is shown again.
    /// </summary>
    public const double FramesPerSecond = 60.0;

    public const int ConstantFloats = ConstantRegisters * 4;

    /// <summary>Each mesh point: screen x, y (-1..1, y up), where it reads from (u, v), where it is (u, v), rad and ang.</summary>
    public const int MeshFloatsPerVertex = 8;

    private const int QCount = 32;
    private const int TCount = 8;
    private const int MaxWavePoints = 512;

    // The values every set of equations can read; set before each runs.
    private static readonly string[] InputNames =
    {
        "time", "fps", "frame", "progress", "bass", "mid", "treb", "bass_att", "mid_att", "treb_att",
        "vol", "vol_att", "meshx", "meshy", "pixelsx", "pixelsy", "aspectx", "aspecty",
    };

    private enum V
    {
        Zoom, ZoomExp, Rot, Warp, Cx, Cy, Dx, Dy, Sx, Sy,
        Decay, Gamma, EchoZoom, EchoAlpha, EchoOrient,
        WaveMode, WaveAdditive, WaveUseDots, WaveThick, ModWaveAlphaByVolume, WaveBrighten,
        Wrap, DarkenCenter, RedBlue, Brighten, Darken, Solarize, Invert,
        WaveA, WaveScale, WaveSmoothing, WaveMystery, ModWaveAlphaStart, ModWaveAlphaEnd,
        WarpAnimSpeed, WarpScale, FShader,
        WaveR, WaveG, WaveB, WaveX, WaveY,
        ObSize, ObR, ObG, ObB, ObA, IbSize, IbR, IbG, IbB, IbA,
        MvX, MvY, MvDx, MvDy, MvL, MvR, MvG, MvB, MvA,
        B1n, B2n, B3n, B1x, B2x, B3x, B1ed,
    }

    // The per-frame variables, in the order of V: their name in the equations, their key in the file, and MilkDrop 2's default.
    private static readonly (string Name, string Key, double Default)[] FrameVariables =
    {
        ("zoom", "zoom", 1), ("zoomexp", "fzoomexponent", 1), ("rot", "rot", 0), ("warp", "warp", 1),
        ("cx", "cx", 0.5), ("cy", "cy", 0.5), ("dx", "dx", 0), ("dy", "dy", 0), ("sx", "sx", 1), ("sy", "sy", 1),
        ("decay", "fdecay", 0.98), ("gamma", "fgammaadj", 2), ("echo_zoom", "fvideoechozoom", 2),
        ("echo_alpha", "fvideoechoalpha", 0), ("echo_orient", "nvideoechoorientation", 0),
        ("wave_mode", "nwavemode", 0), ("wave_additive", "badditivewaves", 0), ("wave_usedots", "bwavedots", 0),
        ("wave_thick", "bwavethick", 0), ("modwavealphabyvolume", "bmodwavealphabyvolume", 0), ("wave_brighten", "bmaximizewavecolor", 1),
        ("wrap", "btexwrap", 1), ("darken_center", "bdarkencenter", 0), ("red_blue", "bredbluestereo", 0),
        ("brighten", "bbrighten", 0), ("darken", "bdarken", 0), ("solarize", "bsolarize", 0), ("invert", "binvert", 0),
        ("wave_a", "fwavealpha", 0.8), ("wave_scale", "fwavescale", 1), ("wave_smoothing", "fwavesmoothing", 0.75),
        ("wave_mystery", "fwaveparam", 0), ("modwavealphastart", "fmodwavealphastart", 0.75), ("modwavealphaend", "fmodwavealphaend", 0.95),
        ("warpanimspeed", "fwarpanimspeed", 1), ("warpscale", "fwarpscale", 1), ("fshader", "fshader", 0),
        ("wave_r", "wave_r", 1), ("wave_g", "wave_g", 1), ("wave_b", "wave_b", 1), ("wave_x", "wave_x", 0.5), ("wave_y", "wave_y", 0.5),
        ("ob_size", "ob_size", 0.01), ("ob_r", "ob_r", 0), ("ob_g", "ob_g", 0), ("ob_b", "ob_b", 0), ("ob_a", "ob_a", 0),
        ("ib_size", "ib_size", 0.01), ("ib_r", "ib_r", 0.25), ("ib_g", "ib_g", 0.25), ("ib_b", "ib_b", 0.25), ("ib_a", "ib_a", 0),
        ("mv_x", "nmotionvectorsx", 12), ("mv_y", "nmotionvectorsy", 9), ("mv_dx", "mv_dx", 0), ("mv_dy", "mv_dy", 0),
        ("mv_l", "mv_l", 0.9), ("mv_r", "mv_r", 1), ("mv_g", "mv_g", 1), ("mv_b", "mv_b", 1), ("mv_a", "mv_a", 0),
        ("b1n", "b1n", 0), ("b2n", "b2n", 0), ("b3n", "b3n", 0), ("b1x", "b1x", 1), ("b2x", "b2x", 1), ("b3x", "b3x", 1), ("b1ed", "b1ed", 0.25),
    };

    // A custom shape's variables: name in the equations, key in the file (after shapecode_N_), default.
    private static readonly (string Name, string Key, double Default)[] ShapeVariables =
    {
        ("x", "x", 0.5), ("y", "y", 0.5), ("rad", "rad", 0.1), ("ang", "ang", 0), ("tex_ang", "tex_ang", 0), ("tex_zoom", "tex_zoom", 1),
        ("sides", "sides", 4), ("textured", "textured", 0), ("additive", "additive", 0), ("thick", "thickoutline", 0),
        ("r", "r", 1), ("g", "g", 0), ("b", "b", 0), ("a", "a", 1), ("r2", "r2", 0), ("g2", "g2", 1), ("b2", "b2", 0), ("a2", "a2", 0),
        ("border_r", "border_r", 1), ("border_g", "border_g", 1), ("border_b", "border_b", 1), ("border_a", "border_a", 0.1),
    };

    private readonly MilkPreset _preset;
    private readonly List<string> _warnings = new();
    private readonly Random _random = new();

    private readonly EelContext _frame;
    private readonly EelCode _frameInit;
    private readonly EelCode _perFrame;
    private readonly int[] _frameInputs;
    private readonly int[] _frameSlots;
    private readonly int[] _frameQ;
    private readonly double[] _base = new double[FrameVariables.Length];
    private readonly double[] _values = new double[FrameVariables.Length];
    private readonly double[] _qAfterInit = new double[QCount];
    private readonly double[] _q = new double[QCount];

    private readonly EelContext _pixel;
    private readonly EelCode? _perPixel;
    private readonly int[] _pixelInputs;
    private readonly int[] _pixelWarp;
    private readonly int[] _pixelQ;
    private readonly int _pixelX;
    private readonly int _pixelY;
    private readonly int _pixelRad;
    private readonly int _pixelAng;

    private readonly List<WaveRunner> _waves = new();
    private readonly List<ShapeRunner> _shapes = new();
    private readonly double[] _inputs = new double[InputNames.Length];

    // The mesh's fixed layout (where each point is), rebuilt when its size or the image's shape changes.
    private int _meshWidth;
    private int _meshHeight;
    private double _layoutAspectX;
    private double _layoutAspectY;
    private double[] _meshX = Array.Empty<double>();
    private double[] _meshY = Array.Empty<double>();
    private double[] _meshRad = Array.Empty<double>();
    private double[] _meshAng = Array.Empty<double>();

    // Scratch space for waves.
    private readonly float[] _waveL = new float[MilkAudio.WaveformSamples];
    private readonly float[] _waveR = new float[MilkAudio.WaveformSamples];
    private readonly float[] _xs = new float[MilkAudio.WaveformSamples + 1];
    private readonly float[] _ys = new float[MilkAudio.WaveformSamples + 1];
    private readonly MilkColor[] _colors = new MilkColor[MilkAudio.WaveformSamples + 1];
    private readonly float[] _shapeX = new float[102];
    private readonly float[] _shapeY = new float[102];
    private readonly MilkColor[] _shapeColors = new MilkColor[102];

    private readonly double[] _randPreset = new double[4];
    private readonly double[] _randStart = new double[4];
    private readonly double[] _rotationPhases = new double[24 * 6];
    private bool _started;
    private int _frameNumber;
    private double _fps = 60;
    private double _nextStep = double.NegativeInfinity;
    private double _lastStep = double.NaN;
    private double _aspectX = 1;
    private double _aspectY = 1;

    public MilkPresetRunner(MilkPreset preset, EelGlobals globals)
    {
        _preset = preset;
        for (var i = 0; i < 4; i++)
        {
            _randPreset[i] = _random.NextDouble();
            _randStart[i] = _random.NextDouble();
        }

        for (var i = 0; i < _rotationPhases.Length; i++)
        {
            _rotationPhases[i] = _random.NextDouble();
        }

        for (var i = 0; i < FrameVariables.Length; i++)
        {
            var (name, key, fallback) = FrameVariables[i];
            _base[i] = preset.Value(key, preset.Value(name, fallback));
        }

        _frame = new EelContext(globals);
        _frameInputs = Indices(_frame, InputNames);
        _frameSlots = Indices(_frame, FrameVariables.Select(v => v.Name));
        _frameQ = Indices(_frame, Enumerable.Range(1, QCount).Select(i => $"q{i}"));
        _frameInit = CompileBlock(preset.PerFrameInit, _frame, "Per-frame init equations");
        _perFrame = CompileBlock(preset.PerFrame, _frame, "Per-frame equations");

        _pixel = new EelContext(globals);
        _pixelInputs = Indices(_pixel, InputNames);
        _pixelWarp = Indices(_pixel, FrameVariables.Take((int)V.Sy + 1).Select(v => v.Name));
        _pixelQ = Indices(_pixel, Enumerable.Range(1, QCount).Select(i => $"q{i}"));
        _pixelX = _pixel.Index("x");
        _pixelY = _pixel.Index("y");
        _pixelRad = _pixel.Index("rad");
        _pixelAng = _pixel.Index("ang");
        if (!string.IsNullOrWhiteSpace(preset.PerPixel))
        {
            _perPixel = CompileBlock(preset.PerPixel, _pixel, "Per-pixel equations");
        }

        foreach (var wave in preset.Waves.Where(wave => wave.Value("enabled", 0) != 0))
        {
            _waves.Add(new WaveRunner(this, wave, globals));
        }

        foreach (var shape in preset.Shapes.Where(shape => shape.Value("enabled", 0) != 0))
        {
            _shapes.Add(new ShapeRunner(this, shape, globals));
        }
    }

    public string Name => _preset.Name;

    /// <summary>Parts of the preset that couldn't be compiled and were left out.</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>The warp mesh, <see cref="MeshFloatsPerVertex"/> floats per point, (meshx + 1) x (meshy + 1) points, row by row from the top.</summary>
    public float[] Mesh { get; private set; } = Array.Empty<float>();

    public int MeshWidth => _meshWidth;

    public int MeshHeight => _meshHeight;

    /// <summary>What to draw over the warped image this frame.</summary>
    public MilkGeometry Geometry { get; } = new();

    /// <summary>The shader constants for this frame (<see cref="ConstantRegisters"/> float4s), laid out as in Shaders/MilkDrop.hlsli.</summary>
    public float[] Constants { get; } = new float[ConstantFloats];

    /// <summary>Whether the image wraps around at the edges when warped (MilkDrop's wrap / bTexWrap), for the classic warp.</summary>
    public bool Wrap => _values[(int)V.Wrap] != 0;

    /// <summary>
    /// Starts the preset again from the beginning (its variables cleared and its init equations run
    /// on the next frame), as MilkDrop does each time a preset is loaded.
    /// </summary>
    public void Restart()
    {
        _started = false;
        _frameNumber = 0;
        _nextStep = double.NegativeInfinity;
        _lastStep = double.NaN;
        Array.Clear(_frame.Values);
        Array.Clear(_pixel.Values);
        foreach (var wave in _waves)
        {
            wave.Clear();
        }

        foreach (var shape in _shapes)
        {
            shape.Clear();
        }
    }

    /// <summary>Seconds since the preset last moved on (what to pass as <see cref="MilkFrameInput.DeltaTime"/>).</summary>
    public double StepDelta { get; private set; } = 1.0 / FramesPerSecond;

    /// <summary>
    /// Whether it's time (<paramref name="time"/> in seconds) for the preset to move on a frame, at most
    /// <see cref="FramesPerSecond"/> times a second; <paramref name="force"/> when there's no image to show
    /// again (it was just made, or remade at a new size). When it is, call <see cref="Render"/>.
    /// </summary>
    public bool StepDue(double time, bool force = false)
    {
        if (time < _lastStep)
        {
            // The clock started again.
            _nextStep = double.NegativeInfinity;
            _lastStep = double.NaN;
        }

        if (!force && time < _nextStep)
        {
            return false;
        }

        // Steady steps, but never a run of catch-up frames after a pause.
        _nextStep = Math.Max(_nextStep + 1.0 / FramesPerSecond, time - 0.5 / FramesPerSecond);
        StepDelta = double.IsNaN(_lastStep) ? 1.0 / FramesPerSecond : Math.Clamp(time - _lastStep, 0.0, 0.1);
        _lastStep = time;
        return true;
    }

    /// <summary>Runs the preset for one frame.</summary>
    public void Render(MilkFrameInput input, MilkAudio audio)
    {
        var width = Math.Max(1, input.Width);
        var height = Math.Max(1, input.Height);
        _aspectX = height > width ? (double)width / height : 1.0;
        _aspectY = width > height ? (double)height / width : 1.0;
        if (input.DeltaTime > 0)
        {
            _fps += (1.0 / input.DeltaTime - _fps) * 0.1;
        }

        SetInputs(input, audio, width, height);
        if (!_started)
        {
            Start();
        }

        // Per-frame: back to the preset's own values (and q to what init left), then the equations.
        var values = _frame.Values;
        Write(values, _frameInputs, _inputs);
        Write(values, _frameSlots, _base);
        Write(values, _frameQ, _qAfterInit);
        _perFrame.Run();
        values = _frame.Values;
        for (var i = 0; i < _frameSlots.Length; i++)
        {
            _values[i] = Safe(values[_frameSlots[i]], _base[i]);
        }

        for (var i = 0; i < QCount; i++)
        {
            _q[i] = Safe(values[_frameQ[i]], 0);
        }

        BuildMesh(input);
        Geometry.Clear(width, height);
        DrawMotionVectors();
        foreach (var shape in _shapes)
        {
            shape.Draw();
        }

        foreach (var wave in _waves)
        {
            wave.Draw(audio);
        }

        DrawWave(input.Time, audio);
        DrawDarkenCenter();
        DrawBorders();
        Geometry.Flush();
        WriteConstants(input, audio, width, height);
        _frameNumber++;
    }

    private void Start()
    {
        _started = true;
        var values = _frame.Values;
        Write(values, _frameInputs, _inputs);
        Write(values, _frameSlots, _base);
        _frameInit.Run();
        values = _frame.Values;
        for (var i = 0; i < QCount; i++)
        {
            _qAfterInit[i] = Safe(values[_frameQ[i]], 0);
            _q[i] = _qAfterInit[i];
        }

        foreach (var wave in _waves)
        {
            wave.Start();
        }

        foreach (var shape in _shapes)
        {
            shape.Start();
        }
    }

    private void SetInputs(MilkFrameInput input, MilkAudio audio, int width, int height)
    {
        _inputs[0] = input.Time;
        _inputs[1] = _fps;
        _inputs[2] = _frameNumber;
        _inputs[3] = Math.Clamp(input.Progress, 0, 1);
        _inputs[4] = audio.Bass;
        _inputs[5] = audio.Mid;
        _inputs[6] = audio.Treb;
        _inputs[7] = audio.BassAtt;
        _inputs[8] = audio.MidAtt;
        _inputs[9] = audio.TrebAtt;
        _inputs[10] = audio.Vol;
        _inputs[11] = audio.VolAtt;
        _inputs[12] = input.MeshWidth;
        _inputs[13] = input.MeshHeight;
        _inputs[14] = width;
        _inputs[15] = height;
        _inputs[16] = _aspectX;
        _inputs[17] = _aspectY;
    }

    private double this[V variable] => _values[(int)variable];

    // ---- The warp mesh ----

    private void BuildMesh(MilkFrameInput input)
    {
        var meshWidth = Math.Clamp(input.MeshWidth, 8, 192);
        var meshHeight = Math.Clamp(input.MeshHeight, 6, 144);
        if (meshWidth != _meshWidth || meshHeight != _meshHeight || _aspectX != _layoutAspectX || _aspectY != _layoutAspectY)
        {
            LayOutMesh(meshWidth, meshHeight);
        }

        var time = input.Time;
        var warpTime = time * this[V.WarpAnimSpeed];
        var warpScaleInverse = Math.Abs(this[V.WarpScale]) < 0.001 ? 1000.0 : 1.0 / this[V.WarpScale];
        var f0 = 11.68 + 4.0 * Math.Cos(time * 1.413 + 10);
        var f1 = 8.77 + 3.0 * Math.Cos(time * 1.113 + 7);
        var f2 = 10.54 + 3.0 * Math.Cos(time * 1.233 + 3);
        var f3 = 11.49 + 4.0 * Math.Cos(time * 0.933 + 5);

        var pixel = _perPixel is null ? null : _pixel.Values;
        if (pixel is not null)
        {
            Write(pixel, _pixelInputs, _inputs);
            Write(pixel, _pixelQ, _q);
        }

        var ax = _aspectX;
        var ay = _aspectY;
        var mesh = Mesh;
        var count = _meshX.Length;
        for (var n = 0; n < count; n++)
        {
            var vx = _meshX[n];
            var vy = _meshY[n];
            var rad = _meshRad[n];
            double zoom = this[V.Zoom], zoomExp = this[V.ZoomExp], rot = this[V.Rot], warp = this[V.Warp];
            double cx = this[V.Cx], cy = this[V.Cy], dx = this[V.Dx], dy = this[V.Dy], sx = this[V.Sx], sy = this[V.Sy];

            if (pixel is not null)
            {
                pixel[_pixelX] = vx * 0.5 + 0.5;
                pixel[_pixelY] = -vy * 0.5 + 0.5;
                pixel[_pixelRad] = rad;
                pixel[_pixelAng] = _meshAng[n];
                for (var i = 0; i < _pixelWarp.Length; i++)
                {
                    pixel[_pixelWarp[i]] = _values[i];
                }

                _perPixel!.Run();
                zoom = Safe(pixel[_pixelWarp[0]], zoom);
                zoomExp = Safe(pixel[_pixelWarp[1]], zoomExp);
                rot = Safe(pixel[_pixelWarp[2]], rot);
                warp = Safe(pixel[_pixelWarp[3]], warp);
                cx = Safe(pixel[_pixelWarp[4]], cx);
                cy = Safe(pixel[_pixelWarp[5]], cy);
                dx = Safe(pixel[_pixelWarp[6]], dx);
                dy = Safe(pixel[_pixelWarp[7]], dy);
                sx = Safe(pixel[_pixelWarp[8]], sx);
                sy = Safe(pixel[_pixelWarp[9]], sy);
            }

            // Zoom, stronger or weaker towards the edges with zoomexp, in a square space so rotation stays round.
            var zoomHere = Math.Pow(Math.Abs(zoom) < 1e-6 ? 1e-6 : zoom, Math.Pow(zoomExp, rad * 2.0 - 1.0));
            var zoomInverse = double.IsFinite(zoomHere) && Math.Abs(zoomHere) > 1e-6 ? 1.0 / zoomHere : 1.0;
            var u = vx * 0.5 * ax * zoomInverse + 0.5;
            var v = -vy * 0.5 * ay * zoomInverse + 0.5;
            var centreU = (cx - 0.5) * ax + 0.5;
            var centreV = (cy - 0.5) * ay + 0.5;

            // Stretch.
            u = (u - centreU) / (Math.Abs(sx) < 1e-6 ? 1e-6 : sx) + centreU;
            v = (v - centreV) / (Math.Abs(sy) < 1e-6 ? 1e-6 : sy) + centreV;

            // MilkDrop's wobbly warp.
            if (warp != 0)
            {
                var amount = warp * 0.0035;
                u += amount * Math.Sin(warpTime * 0.333 + warpScaleInverse * (vx * f0 - vy * f3));
                v += amount * Math.Cos(warpTime * 0.375 - warpScaleInverse * (vx * f2 + vy * f1));
                u += amount * Math.Cos(warpTime * 0.753 - warpScaleInverse * (vx * f1 - vy * f2));
                v += amount * Math.Sin(warpTime * 0.825 + warpScaleInverse * (vx * f0 + vy * f3));
            }

            // Rotation about the centre.
            if (rot != 0)
            {
                var cos = Math.Cos(rot);
                var sin = Math.Sin(rot);
                var u2 = u - centreU;
                var v2 = v - centreV;
                u = u2 * cos - v2 * sin + centreU;
                v = u2 * sin + v2 * cos + centreV;
            }

            // Back out of the square space, then move.
            u = (u - 0.5) / ax + 0.5 - dx;
            v = (v - 0.5) / ay + 0.5 - dy;

            var at = n * MeshFloatsPerVertex;
            mesh[at + 2] = double.IsFinite(u) ? (float)u : (float)(vx * 0.5 + 0.5);
            mesh[at + 3] = double.IsFinite(v) ? (float)v : (float)(-vy * 0.5 + 0.5);
        }
    }

    private void LayOutMesh(int meshWidth, int meshHeight)
    {
        _meshWidth = meshWidth;
        _meshHeight = meshHeight;
        _layoutAspectX = _aspectX;
        _layoutAspectY = _aspectY;
        var count = (meshWidth + 1) * (meshHeight + 1);
        _meshX = new double[count];
        _meshY = new double[count];
        _meshRad = new double[count];
        _meshAng = new double[count];
        Mesh = new float[count * MeshFloatsPerVertex];
        for (var j = 0; j <= meshHeight; j++)
        {
            for (var i = 0; i <= meshWidth; i++)
            {
                var n = j * (meshWidth + 1) + i;
                var vx = i / (double)meshWidth * 2.0 - 1.0;
                var vy = 1.0 - j / (double)meshHeight * 2.0;
                _meshX[n] = vx;
                _meshY[n] = vy;
                _meshRad[n] = Math.Sqrt(vx * vx * _aspectX * _aspectX + vy * vy * _aspectY * _aspectY) * 0.7071067;
                _meshAng[n] = Math.Atan2(vy * _aspectY, vx * _aspectX);
                var at = n * MeshFloatsPerVertex;
                Mesh[at] = (float)vx;
                Mesh[at + 1] = (float)vy;
                Mesh[at + 4] = (float)(vx * 0.5 + 0.5);
                Mesh[at + 5] = (float)(-vy * 0.5 + 0.5);
                Mesh[at + 6] = (float)_meshRad[n];
                Mesh[at + 7] = (float)_meshAng[n];
            }
        }
    }

    /// <summary>Where the warp reads from for a point on screen (0..1, y down), from the mesh.</summary>
    private (double U, double V) SourceAt(double x, double y)
    {
        var fx = Math.Clamp(x, 0, 1) * _meshWidth;
        var fy = Math.Clamp(y, 0, 1) * _meshHeight;
        var i = Math.Min((int)fx, _meshWidth - 1);
        var j = Math.Min((int)fy, _meshHeight - 1);
        var tx = fx - i;
        var ty = fy - j;
        var row = _meshWidth + 1;
        double Read(int n, int offset) => Mesh[n * MeshFloatsPerVertex + offset];
        var a = j * row + i;
        var b = a + 1;
        var c = a + row;
        var d = c + 1;
        var u = (Read(a, 2) * (1 - tx) + Read(b, 2) * tx) * (1 - ty) + (Read(c, 2) * (1 - tx) + Read(d, 2) * tx) * ty;
        var v = (Read(a, 3) * (1 - tx) + Read(b, 3) * tx) * (1 - ty) + (Read(c, 3) * (1 - tx) + Read(d, 3) * tx) * ty;
        return (u, v);
    }

    // ---- Drawing over the warped image ----

    private void DrawMotionVectors()
    {
        var alpha = this[V.MvA];
        var across = (int)Math.Min(this[V.MvX], 64);
        var down = (int)Math.Min(this[V.MvY], 48);
        if (alpha <= 0.001 || across < 1 || down < 1)
        {
            return;
        }

        var color = MilkColor.From(this[V.MvR], this[V.MvG], this[V.MvB], alpha);
        var length = this[V.MvL];
        Geometry.Use(additive: false, textured: false);
        for (var j = 0; j < down; j++)
        {
            for (var i = 0; i < across; i++)
            {
                var x = (i + 0.25) / across + this[V.MvDx];
                var y = (j + 0.25) / down + this[V.MvDy];
                if (x < 0 || x > 1 || y < 0 || y > 1)
                {
                    continue;
                }

                // A short line pointing the way the image is moving here.
                var (u, v) = SourceAt(x, y);
                var startX = x * 2 - 1;
                var startY = 1 - y * 2;
                var endX = startX + (x - u) * 2 * length;
                var endY = startY - (y - v) * 2 * length;
                Geometry.Line((float)startX, (float)startY, color, (float)endX, (float)endY, color, Geometry.LineWidth);
            }
        }
    }

    /// <summary>MilkDrop's built-in waveform, in one of its eight styles (wave_mode 0-7).</summary>
    private void DrawWave(double time, MilkAudio audio)
    {
        var alpha = this[V.WaveA];
        if (this[V.ModWaveAlphaByVolume] != 0)
        {
            var start = this[V.ModWaveAlphaStart];
            var end = this[V.ModWaveAlphaEnd];
            alpha *= Math.Clamp((audio.Vol - start) / Math.Max(end - start, 0.001), 0, 1);
        }

        var mode = (int)Math.Abs(this[V.WaveMode]) % 8;
        if (mode == 3)
        {
            // Like 2, but it fades with the treble.
            alpha *= Math.Clamp(audio.Treb * audio.Treb * 0.5, 0, 1);
        }

        if (alpha < 0.001)
        {
            return;
        }

        double r = Math.Clamp(this[V.WaveR], 0, 1), g = Math.Clamp(this[V.WaveG], 0, 1), b = Math.Clamp(this[V.WaveB], 0, 1);
        if (this[V.WaveBrighten] != 0)
        {
            var brightest = Math.Max(r, Math.Max(g, b));
            if (brightest > 0.01)
            {
                r /= brightest;
                g /= brightest;
                b /= brightest;
            }
        }

        var color = MilkColor.From(r, g, b, alpha);
        var scale = this[V.WaveScale];
        var smoothing = Math.Clamp(this[V.WaveSmoothing], 0, 0.98);
        var count = MilkAudio.WaveformSamples;
        Smooth(audio.WaveLeft, _waveL, count, scale, smoothing);
        Smooth(audio.WaveRight, _waveR, count, scale, smoothing);

        var cx = this[V.WaveX] * 2 - 1;
        var cy = this[V.WaveY] * 2 - 1;
        var mystery = this[V.WaveMystery];
        double ax = _aspectX, ay = _aspectY;
        var points = 0;
        var loop = false;
        switch (mode)
        {
            case 0:
            {
                // A ring whose radius follows the wave; the ends are blended so it closes smoothly.
                points = count / 2;
                loop = true;
                for (var i = 0; i < points; i++)
                {
                    var radius = 0.5 + 0.4 * _waveR[i] + mystery;
                    var blend = points / 10;
                    if (i < blend)
                    {
                        var mix = 0.5 - 0.5 * Math.Cos(i / (double)blend * Math.PI);
                        var other = 0.5 + 0.4 * _waveR[i + points] + mystery;
                        radius = other * (1 - mix) + radius * mix;
                    }

                    var angle = i / (double)points * Math.PI * 2 + time * 0.2;
                    _xs[i] = (float)(radius * Math.Cos(angle) * ay + cx);
                    _ys[i] = (float)(radius * Math.Sin(angle) * ax + cy);
                }

                break;
            }

            case 1:
                // A spiralling X-Y scope.
                points = count / 2;
                for (var i = 0; i < points; i++)
                {
                    var radius = 0.53 + 0.43 * _waveR[i] + mystery;
                    var angle = _waveL[i + 32] * Math.PI * 0.5 + time * 2.3;
                    _xs[i] = (float)(radius * Math.Cos(angle) * ay + cx);
                    _ys[i] = (float)(radius * Math.Sin(angle) * ax + cy);
                }

                break;
            case 2:
            case 3:
                // Left against right, centred.
                points = count - 32;
                for (var i = 0; i < points; i++)
                {
                    _xs[i] = (float)(_waveR[i] * ay + cx);
                    _ys[i] = (float)(_waveL[i + 32] * ax + cy);
                }

                break;
            case 4:
            {
                // A horizontal wave that lags behind itself, smoothed by the mystery value.
                points = count - 25;
                var w1 = 0.45 + 0.5 * (mystery * 0.5 + 0.5);
                var w2 = 1 - w1;
                for (var i = 0; i < points; i++)
                {
                    var x = -1.0 + 2.0 * i / points + _waveL[i + 25] * 0.44;
                    var y = _waveL[i] * 0.47 + cy;
                    if (i > 1)
                    {
                        x = x * w2 + w1 * (_xs[i - 1] * 2.0 - _xs[i - 2]);
                        y = y * w2 + w1 * (_ys[i - 1] * 2.0 - _ys[i - 2]);
                    }

                    _xs[i] = (float)x;
                    _ys[i] = (float)y;
                }

                break;
            }

            case 5:
            {
                // The "explosive hash": products of the two channels, slowly turning.
                points = count - 32;
                var cos = Math.Cos(time * 0.3);
                var sin = Math.Sin(time * 0.3);
                for (var i = 0; i < points; i++)
                {
                    var x0 = _waveR[i] * _waveL[i + 32] + _waveL[i] * _waveR[i + 32];
                    var y0 = _waveR[i] * _waveR[i] - _waveL[i + 32] * _waveL[i + 32];
                    _xs[i] = (float)((x0 * cos - y0 * sin) * ay + cx);
                    _ys[i] = (float)((x0 * sin + y0 * cos) * ax + cy);
                }

                break;
            }

            default:
            {
                // 6: a straight wave across the screen at an angle set by the mystery value.
                // 7: two of them, one per channel, apart by wave_y.
                // As in MilkDrop, the line sits wave_x away from the centre, measured across it (not
                // along x): a level line (mystery 0) is moved up and down by wave_x, and a standing one
                // (mystery 1) sits on the left for wave_x above 0.5.
                points = 256;
                var angle = mystery * Math.PI * 0.5;
                var dirX = Math.Cos(angle);
                var dirY = Math.Sin(angle);
                var lines = mode == 7 ? 2 : 1;
                for (var line = 0; line < lines; line++)
                {
                    var source = line == 0 ? _waveL : _waveR;
                    var apart = mode == 7 ? (line == 0 ? 1 : -1) * (this[V.WaveY] * 0.5) : 0.0;
                    var originX = -dirY * (cx + apart);
                    var originY = dirX * (cx + apart);
                    for (var i = 0; i < points; i++)
                    {
                        var along = (i / (double)(points - 1) * 2 - 1) * 1.5;
                        var offset = source[i] * 0.25;
                        _xs[i] = (float)(originX + dirX * along - dirY * offset);
                        _ys[i] = (float)(originY + dirY * along + dirX * offset);
                        _colors[i] = color;
                    }

                    Geometry.Use(this[V.WaveAdditive] != 0, textured: false);
                    Geometry.Strip(_xs, _ys, _colors, points, loop: false, this[V.WaveUseDots] != 0, this[V.WaveThick] != 0);
                }

                return;
            }
        }

        for (var i = 0; i < points; i++)
        {
            _colors[i] = color;
        }

        Geometry.Use(this[V.WaveAdditive] != 0, textured: false);
        Geometry.Strip(_xs, _ys, _colors, points, loop, this[V.WaveUseDots] != 0, this[V.WaveThick] != 0);
    }

    /// <summary>Scales the wave and runs it through a simple low-pass, as wave_smoothing asks.</summary>
    private static void Smooth(float[] source, float[] destination, int count, double scale, double smoothing)
    {
        var previous = source[0] * scale;
        for (var i = 0; i < count; i++)
        {
            previous = previous * smoothing + source[i] * scale * (1 - smoothing);
            destination[i] = (float)previous;
        }
    }

    private void DrawDarkenCenter()
    {
        if (this[V.DarkenCenter] == 0)
        {
            return;
        }

        const int Sides = 24;
        const double Radius = 0.05;
        var centre = new MilkColor(0, 0, 0, 3f / 32f);
        var edge = new MilkColor(0, 0, 0, 0);
        Geometry.Use(additive: false, textured: false);
        for (var i = 0; i < Sides; i++)
        {
            var a0 = i / (double)Sides * Math.PI * 2;
            var a1 = (i + 1) / (double)Sides * Math.PI * 2;
            Geometry.Triangle(
                0, 0, centre,
                (float)(Math.Cos(a0) * Radius * _aspectY), (float)(Math.Sin(a0) * Radius * _aspectX), edge,
                (float)(Math.Cos(a1) * Radius * _aspectY), (float)(Math.Sin(a1) * Radius * _aspectX), edge);
        }
    }

    private void DrawBorders()
    {
        // ob_size and ib_size are fractions of the screen; 0.5 fills it.
        var outer = Math.Clamp(this[V.ObSize], 0, 0.5) * 2;
        var inner = Math.Clamp(this[V.IbSize], 0, 0.5) * 2;
        Border(1.0, 1.0 - outer, MilkColor.From(this[V.ObR], this[V.ObG], this[V.ObB], this[V.ObA]));
        Border(1.0 - outer, 1.0 - outer - inner, MilkColor.From(this[V.IbR], this[V.IbG], this[V.IbB], this[V.IbA]));
    }

    private void Border(double outside, double inside, MilkColor color)
    {
        if (color.A < 0.001 || outside - inside < 1e-5)
        {
            return;
        }

        inside = Math.Max(inside, 0);
        float o = (float)outside, i = (float)inside;
        Geometry.Use(additive: false, textured: false);
        Geometry.Rectangle(-o, i, o, o, color);
        Geometry.Rectangle(-o, -o, o, -i, color);
        Geometry.Rectangle(-o, -i, -i, i, color);
        Geometry.Rectangle(i, -i, o, i, color);
    }

    // ---- Shader constants ----

    private void WriteConstants(MilkFrameInput input, MilkAudio audio, int width, int height)
    {
        var c = Constants;
        var time = input.Time;
        Set(c, 0, _aspectX, _aspectY, 1 / _aspectX, 1 / _aspectY);
        Set(c, 1, width, height, 1.0 / width, 1.0 / height);
        Set(c, 2, time, _fps, _frameNumber, Math.Clamp(input.Progress, 0, 1));
        Set(c, 3, audio.Bass, audio.Mid, audio.Treb, audio.Vol);
        Set(c, 4, audio.BassAtt, audio.MidAtt, audio.TrebAtt, audio.VolAtt);
        Set(c, 5, _random.NextDouble(), _random.NextDouble(), _random.NextDouble(), _random.NextDouble());
        Set(c, 6, _randPreset[0], _randPreset[1], _randPreset[2], _randPreset[3]);

        // Gently wandering values the shaders can use, from MilkDrop 2.
        Set(c, 7, Roam(Math.Cos, time, 0.329, 1.2), Roam(Math.Cos, time, 1.293, 3.9), Roam(Math.Cos, time, 5.070, 2.5), Roam(Math.Cos, time, 20.051, 5.4));
        Set(c, 8, Roam(Math.Sin, time, 0.329, 1.2), Roam(Math.Sin, time, 1.293, 3.9), Roam(Math.Sin, time, 5.070, 2.5), Roam(Math.Sin, time, 20.051, 5.4));
        Set(c, 9, Roam(Math.Cos, time, 0.005, 1.2), Roam(Math.Cos, time, 0.008, 3.9), Roam(Math.Cos, time, 0.013, 2.5), Roam(Math.Cos, time, 0.022, 5.4));
        Set(c, 10, Roam(Math.Sin, time, 0.005, 1.2), Roam(Math.Sin, time, 0.008, 3.9), Roam(Math.Sin, time, 0.013, 2.5), Roam(Math.Sin, time, 0.022, 5.4));
        for (var i = 0; i < 8; i++)
        {
            Set(c, 11 + i, _q[i * 4], _q[i * 4 + 1], _q[i * 4 + 2], _q[i * 4 + 3]);
        }

        // The blur ranges, kept apart a little as MilkDrop does so the blur textures keep some precision.
        var (min1, max1) = BlurRange(V.B1n, V.B1x);
        var (min2, max2) = BlurRange(V.B2n, V.B2x);
        var (min3, max3) = BlurRange(V.B3n, V.B3x);
        Set(c, 19, min1, max1, min2, max2);
        Set(c, 20, min3, max3, Math.Clamp(this[V.Decay], 0, 1), Math.Max(this[V.Gamma], 0));
        Set(c, 21, Math.Abs(this[V.EchoZoom]) < 0.001 ? 1 : this[V.EchoZoom], Math.Clamp(this[V.EchoAlpha], 0, 1), (int)Math.Abs(this[V.EchoOrient]) % 4, this[V.FShader]);
        Set(c, 22, this[V.Brighten] != 0 ? 1 : 0, this[V.Darken] != 0 ? 1 : 0, this[V.Solarize] != 0 ? 1 : 0, this[V.Invert] != 0 ? 1 : 0);
        Set(c, 23, Wrap ? 1 : 0, _meshWidth, _meshHeight, this[V.B1ed]);

        // hue_shader: four slowly changing colours at the corners, blended across the screen.
        for (var corner = 0; corner < 4; corner++)
        {
            var red = 0.6 + 0.3 * Math.Sin(time * 30 * 0.0143 + 3 + corner * 21 + _randStart[3]);
            var green = 0.6 + 0.3 * Math.Sin(time * 30 * 0.0107 + 1 + corner * 13 + _randStart[1]);
            var blue = 0.6 + 0.3 * Math.Sin(time * 30 * 0.0129 + 6 + corner * 9 + _randStart[2]);
            var brightest = Math.Max(red, Math.Max(green, blue));
            Set(c, 24 + corner, 0.5 + 0.5 * red / brightest, 0.5 + 0.5 * green / brightest, 0.5 + 0.5 * blue / brightest, 1);
        }

        WriteRotations(c, time);
    }

    private (double Min, double Max) BlurRange(V min, V max)
    {
        var low = Math.Clamp(this[min], -10, 10);
        var high = Math.Clamp(this[max], -10, 10);
        if (high - low < 0.1)
        {
            var middle = (low + high) * 0.5;
            low = middle - 0.05;
            high = middle + 0.05;
        }

        return (low, high);
    }

    private static double Roam(Func<double, double> wave, double time, double speed, double phase) =>
        0.5 + 0.5 * wave(time * speed + phase);

    /// <summary>
    /// rot_s1..4 (fixed for the preset), rot_d, rot_f, rot_vf and rot_uf (turning slowly to very fast)
    /// and rot_rand (new every frame): 3D rotations with an offset, as float4x3 rows.
    /// </summary>
    private void WriteRotations(float[] c, double time)
    {
        var speeds = new[] { 0.0, 0.03, 0.12, 0.5, 2.0 };
        for (var matrix = 0; matrix < 24; matrix++)
        {
            var group = matrix / 4;
            var phases = _rotationPhases.AsSpan(matrix * 6, 6);
            double ax, ay, az, tx, ty, tz;
            if (group == 5)
            {
                ax = _random.NextDouble() * Math.PI * 2;
                ay = _random.NextDouble() * Math.PI * 2;
                az = _random.NextDouble() * Math.PI * 2;
                tx = _random.NextDouble() * 2 - 1;
                ty = _random.NextDouble() * 2 - 1;
                tz = _random.NextDouble() * 2 - 1;
            }
            else
            {
                var t = time * speeds[group];
                ax = phases[0] * Math.PI * 2 + t * (0.5 + phases[3]);
                ay = phases[1] * Math.PI * 2 + t * (0.5 + phases[4]);
                az = phases[2] * Math.PI * 2 + t * (0.5 + phases[5]);
                tx = Math.Cos(t * (0.7 + phases[4]) + phases[0] * 6.28);
                ty = Math.Cos(t * (0.7 + phases[5]) + phases[1] * 6.28);
                tz = Math.Cos(t * (0.7 + phases[3]) + phases[2] * 6.28);
            }

            double cx = Math.Cos(ax), sx = Math.Sin(ax), cy = Math.Cos(ay), sy = Math.Sin(ay), cz = Math.Cos(az), sz = Math.Sin(az);

            // Rx * Ry * Rz.
            var register = 28 + matrix * 4;
            Set(c, register, cy * cz, cy * sz, -sy, 0);
            Set(c, register + 1, sx * sy * cz - cx * sz, sx * sy * sz + cx * cz, sx * cy, 0);
            Set(c, register + 2, cx * sy * cz + sx * sz, cx * sy * sz - sx * cz, cx * cy, 0);
            Set(c, register + 3, tx, ty, tz, 0);
        }
    }

    private static void Set(float[] constants, int register, double x, double y, double z, double w)
    {
        var at = register * 4;
        constants[at] = (float)x;
        constants[at + 1] = (float)y;
        constants[at + 2] = (float)z;
        constants[at + 3] = (float)w;
    }

    // ---- Helpers ----

    private EelCode CompileBlock(string code, EelContext context, string label)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return EelCode.Empty(context);
        }

        var warnings = new List<string>();
        try
        {
            var compiled = EelCompiler.Compile(code, context, warnings);
            _warnings.AddRange(warnings.Distinct().Select(warning => $"{label}: {warning}"));
            return compiled;
        }
        catch (EelException ex)
        {
            _warnings.Add($"{label} were left out: {ex.Message}");
            return EelCode.Empty(context);
        }
    }

    private static int[] Indices(EelContext context, IEnumerable<string> names) => names.Select(context.Index).ToArray();

    private static void Write(double[] values, int[] slots, double[] from)
    {
        for (var i = 0; i < slots.Length; i++)
        {
            values[slots[i]] = from[i];
        }
    }

    private static double Safe(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    /// <summary>A custom wave: up to 512 points from the sound, placed and coloured by its per-point equations.</summary>
    private sealed class WaveRunner
    {
        private readonly MilkPresetRunner _owner;
        private readonly MilkCustomWave _wave;
        private readonly EelContext _context;
        private readonly EelCode _init;
        private readonly EelCode _perFrame;
        private readonly EelCode? _perPoint;
        private readonly int[] _inputs;
        private readonly int[] _q;
        private readonly int[] _t;
        private readonly double[] _tAfterInit = new double[TCount];
        private readonly int _r, _g, _b, _a, _samples, _sample, _value1, _value2, _x, _y;
        private readonly float[] _data1 = new float[MaxWavePoints];
        private readonly float[] _data2 = new float[MaxWavePoints];

        public WaveRunner(MilkPresetRunner owner, MilkCustomWave wave, EelGlobals globals)
        {
            _owner = owner;
            _wave = wave;
            _context = new EelContext(globals);
            _inputs = Indices(_context, InputNames);
            _q = Indices(_context, Enumerable.Range(1, QCount).Select(i => $"q{i}"));
            _t = Indices(_context, Enumerable.Range(1, TCount).Select(i => $"t{i}"));
            _r = _context.Index("r");
            _g = _context.Index("g");
            _b = _context.Index("b");
            _a = _context.Index("a");
            _samples = _context.Index("samples");
            _sample = _context.Index("sample");
            _value1 = _context.Index("value1");
            _value2 = _context.Index("value2");
            _x = _context.Index("x");
            _y = _context.Index("y");
            var label = $"Custom wave {wave.Index + 1}";
            _init = owner.CompileBlock(wave.Init, _context, $"{label} init equations");
            _perFrame = owner.CompileBlock(wave.PerFrame, _context, $"{label} per-frame equations");
            if (!string.IsNullOrWhiteSpace(wave.PerPoint))
            {
                _perPoint = owner.CompileBlock(wave.PerPoint, _context, $"{label} per-point equations");
            }
        }

        public void Clear() => Array.Clear(_context.Values);

        public void Start()
        {
            SetCommon(_owner._qAfterInit);
            _init.Run();
            var values = _context.Values;
            for (var i = 0; i < TCount; i++)
            {
                _tAfterInit[i] = Safe(values[_t[i]], 0);
            }
        }

        private void SetCommon(double[] q)
        {
            var values = _context.Values;
            Write(values, _inputs, _owner._inputs);
            Write(values, _q, q);
            values[_r] = _wave.Value("r", 1);
            values[_g] = _wave.Value("g", 1);
            values[_b] = _wave.Value("b", 1);
            values[_a] = _wave.Value("a", 1);
            values[_samples] = _wave.Value("samples", MaxWavePoints);
        }

        public void Draw(MilkAudio audio)
        {
            SetCommon(_owner._q);
            Write(_context.Values, _t, _tAfterInit);
            _perFrame.Run();
            var values = _context.Values;
            var count = (int)Math.Clamp(Safe(values[_samples], 0), 0, MaxWavePoints);
            if (count < 2)
            {
                return;
            }

            // The sound for each point: the waveform (left and right) or the spectrum.
            var spectrum = _wave.Value("bspectrum", 0) != 0;
            var separation = (int)Math.Clamp(_wave.Value("sep", 0), 0, MaxWavePoints);
            var scaling = _wave.Value("scaling", 1) * (spectrum ? 1.0 : 0.5);
            var smoothing = Math.Clamp(_wave.Value("smoothing", 0.5), 0, 0.98);
            for (var i = 0; i < count; i++)
            {
                if (spectrum)
                {
                    var at = (int)((double)i / count * (MilkAudio.SpectrumSamples - separation));
                    _data1[i] = audio.Spectrum[Math.Min(at, MilkAudio.SpectrumSamples - 1)];
                    _data2[i] = audio.Spectrum[Math.Min(at + separation, MilkAudio.SpectrumSamples - 1)];
                }
                else
                {
                    _data1[i] = audio.WaveLeft[i];
                    _data2[i] = audio.WaveRight[Math.Min(i + separation, MilkAudio.WaveformSamples - 1)];
                }
            }

            Smooth(_data1, _data1, count, scaling, smoothing);
            Smooth(_data2, _data2, count, scaling, smoothing);

            double r = values[_r], g = values[_g], b = values[_b], a = values[_a];
            var xs = _owner._xs;
            var ys = _owner._ys;
            var colors = _owner._colors;
            for (var i = 0; i < count; i++)
            {
                values[_sample] = i / (double)(count - 1);
                values[_value1] = _data1[i];
                values[_value2] = _data2[i];
                values[_x] = 0.5 + _data1[i];
                values[_y] = 0.5 + _data2[i];
                values[_r] = r;
                values[_g] = g;
                values[_b] = b;
                values[_a] = a;
                _perPoint?.Run();
                xs[i] = (float)(Safe(values[_x], 0.5) * 2 - 1);
                ys[i] = (float)(Safe(values[_y], 0.5) * 2 - 1);
                colors[i] = MilkColor.From(values[_r], values[_g], values[_b], values[_a]);
            }

            var geometry = _owner.Geometry;
            geometry.Use(_wave.Value("badditive", 0) != 0, textured: false);
            geometry.Strip(xs, ys, colors, count, loop: false, _wave.Value("busedots", 0) != 0, _wave.Value("bdrawthick", 0) != 0);
        }
    }

    /// <summary>A custom shape: a filled polygon (optionally showing the last frame inside it), drawn up to num_inst times.</summary>
    private sealed class ShapeRunner
    {
        private readonly MilkPresetRunner _owner;
        private readonly MilkCustomShape _shape;
        private readonly EelContext _context;
        private readonly EelCode _init;
        private readonly EelCode _perFrame;
        private readonly int[] _inputs;
        private readonly int[] _q;
        private readonly int[] _t;
        private readonly int[] _slots;
        private readonly double[] _base;
        private readonly double[] _tAfterInit = new double[TCount];
        private readonly int _instance;
        private readonly int _instances;

        public ShapeRunner(MilkPresetRunner owner, MilkCustomShape shape, EelGlobals globals)
        {
            _owner = owner;
            _shape = shape;
            _context = new EelContext(globals);
            _inputs = Indices(_context, InputNames);
            _q = Indices(_context, Enumerable.Range(1, QCount).Select(i => $"q{i}"));
            _t = Indices(_context, Enumerable.Range(1, TCount).Select(i => $"t{i}"));
            _slots = Indices(_context, ShapeVariables.Select(v => v.Name));
            _base = ShapeVariables.Select(v => shape.Value(v.Key, v.Default)).ToArray();
            _instance = _context.Index("instance");
            _instances = _context.Index("num_inst");
            var label = $"Custom shape {shape.Index + 1}";
            _init = owner.CompileBlock(shape.Init, _context, $"{label} init equations");
            _perFrame = owner.CompileBlock(shape.PerFrame, _context, $"{label} per-frame equations");
        }

        private int Instances => (int)Math.Clamp(_shape.Value("num_inst", 1), 1, 1024);

        public void Clear() => Array.Clear(_context.Values);

        public void Start()
        {
            var values = _context.Values;
            Write(values, _inputs, _owner._inputs);
            Write(values, _q, _owner._qAfterInit);
            Write(values, _slots, _base);
            values[_instance] = 0;
            values[_instances] = Instances;
            _init.Run();
            values = _context.Values;
            for (var i = 0; i < TCount; i++)
            {
                _tAfterInit[i] = Safe(values[_t[i]], 0);
            }
        }

        public void Draw()
        {
            var instances = Instances;
            for (var instance = 0; instance < instances; instance++)
            {
                var values = _context.Values;
                Write(values, _inputs, _owner._inputs);
                Write(values, _q, _owner._q);
                Write(values, _t, _tAfterInit);
                Write(values, _slots, _base);
                values[_instance] = instance;
                values[_instances] = instances;
                _perFrame.Run();
                DrawOne(_context.Values);
            }
        }

        private double Read(double[] values, int variable) => Safe(values[_slots[variable]], _base[variable]);

        private static bool IsOn(double value) => Math.Abs(value) >= 1;

        private void DrawOne(double[] values)
        {
            var x = Read(values, 0) * 2 - 1;
            var y = Read(values, 1) * 2 - 1;
            var radius = Read(values, 2);
            var angle = Read(values, 3);
            var textureAngle = Read(values, 4);
            var textureZoom = Read(values, 5);
            var sides = (int)Math.Clamp(Read(values, 6), 3, 100);
            // MilkDrop reads these switches as whole numbers, so 0.8 is off: FractalDrop's darkening triangle sets
            // additive = 0.5 + 0.15 * (bass + bass_att) and is meant to stay blended, not added on.
            var textured = IsOn(Read(values, 7));
            var additive = IsOn(Read(values, 8));
            var thick = IsOn(Read(values, 9));
            var centre = MilkColor.From(Read(values, 10), Read(values, 11), Read(values, 12), Read(values, 13));
            var edge = MilkColor.From(Read(values, 14), Read(values, 15), Read(values, 16), Read(values, 17));
            var border = MilkColor.From(Read(values, 18), Read(values, 19), Read(values, 20), Read(values, 21));
            if (centre.A < 0.001 && edge.A < 0.001 && border.A < 0.001)
            {
                return;
            }

            var aspectX = _owner._aspectX;
            var aspectY = _owner._aspectY;
            var xs = _owner._shapeX;
            var ys = _owner._shapeY;
            var zoom = Math.Abs(textureZoom) < 1e-4 ? 1e-4 : textureZoom;
            var geometry = _owner.Geometry;
            for (var i = 0; i <= sides; i++)
            {
                var a = i / (double)sides * Math.PI * 2 + angle + Math.PI * 0.25;
                xs[i] = (float)(x + radius * Math.Cos(a) * aspectY);
                ys[i] = (float)(y + radius * Math.Sin(a) * aspectX);
            }

            if (centre.A >= 0.001 || edge.A >= 0.001)
            {
                geometry.Use(additive, textured);
                for (var i = 0; i < sides; i++)
                {
                    if (textured)
                    {
                        // The middle of the last frame, turned by tex_ang and scaled by tex_zoom.
                        var a0 = i / (double)sides * Math.PI * 2 + angle + Math.PI * 0.25 + textureAngle;
                        var a1 = (i + 1) / (double)sides * Math.PI * 2 + angle + Math.PI * 0.25 + textureAngle;
                        geometry.TexturedTriangle(
                            (float)x, (float)y, 0.5f, 0.5f, centre,
                            xs[i], ys[i], (float)(0.5 + 0.5 * Math.Cos(a0) / zoom * aspectY), (float)(0.5 - 0.5 * Math.Sin(a0) / zoom), edge,
                            xs[i + 1], ys[i + 1], (float)(0.5 + 0.5 * Math.Cos(a1) / zoom * aspectY), (float)(0.5 - 0.5 * Math.Sin(a1) / zoom), edge);
                    }
                    else
                    {
                        geometry.Triangle((float)x, (float)y, centre, xs[i], ys[i], edge, xs[i + 1], ys[i + 1], edge);
                    }
                }
            }

            if (border.A >= 0.001)
            {
                var colors = _owner._shapeColors;
                for (var i = 0; i < sides; i++)
                {
                    colors[i] = border;
                }

                geometry.Use(additive, textured: false);
                geometry.Strip(xs, ys, colors, sides, loop: true, dots: false, thick);
            }
        }
    }
}
