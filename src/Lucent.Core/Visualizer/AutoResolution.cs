namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// The Automatic resolution: draws a visual that can't keep up with the screen at fewer pixels until it
/// can, and goes back up when there's room. Shared by the Windows and Mac renderers, which call
/// <see cref="Frame"/> after showing each frame and draw at <see cref="PixelShare"/> of the window's pixels.
/// </summary>
public sealed class AutoResolution
{
    /// <summary>The share of the window's pixels each step draws: each step about a fifth fewer.</summary>
    private static readonly double[] Shares = { 1.0, 0.8, 0.64, 0.512, 0.41, 0.33, 0.26 };

    /// <summary>How long each frame rate measurement lasts.</summary>
    private const double MeasureSeconds = 0.5;

    /// <summary>Slow measurements in a row before drawing smaller, so one hitch doesn't count.</summary>
    private const int SlowMeasurementsToStepDown = 2;

    /// <summary>How long a visual has to keep up before trying a bigger size.</summary>
    private const double GoodSecondsToStepUp = 2.0;

    /// <summary>A gap this long between frames means the window was hidden or off screen, not slow.</summary>
    private const double PausedGapSeconds = 0.9;

    /// <summary>How long after a visual starts (and compiles) before measuring it.</summary>
    private const double SettleSeconds = 1.0;

    /// <summary>The step each visual last settled on, so it starts there next time.</summary>
    private readonly Dictionary<int, int> _settled = new();

    private int _step;
    private int _tooSlowStep = -1;
    private int _lastTooSlow = -1;
    private int _cameUpFrom = -1;
    private double _tooSlowUntil;
    private double _forgetSeconds = 30.0;
    private int _preset = int.MinValue;
    private double _measureStart = -1.0;
    private double _lastFrame = -1.0;
    private double _ignoreUntil;
    private int _frames;
    private int _slowMeasurements;
    private double _goodSeconds;

    /// <summary>The share of the window's pixels to draw, from 1 (all of them) down to about a quarter.</summary>
    public double PixelShare => Shares[_step];

    /// <summary>
    /// Call once each frame is shown, with the time in seconds, the screen's refresh rate and the visual
    /// on screen. Returns true when <see cref="PixelShare"/> changed.
    /// </summary>
    public bool Frame(double now, int refreshRate, int preset)
    {
        if (preset != _preset)
        {
            // A visual starts at the size it last settled on, or full size the first time, and stays
            // there a while before trying bigger.
            _preset = preset;
            _lastFrame = now;
            var start = _settled.TryGetValue(preset, out var settled) ? settled : 0;
            _forgetSeconds = 30.0;
            _tooSlowStep = start - 1;
            _lastTooSlow = start - 1;
            _cameUpFrom = -1;
            _tooSlowUntil = now + _forgetSeconds;
            _ignoreUntil = now + SettleSeconds;
            var changed = _step != start;
            _step = start;
            Restart();
            return changed;
        }

        var gap = _lastFrame < 0 ? 0 : now - _lastFrame;
        _lastFrame = now;
        if (now < _ignoreUntil || gap > PausedGapSeconds)
        {
            Restart();
            return false;
        }

        if (_measureStart < 0)
        {
            _measureStart = now;
            _frames = 0;
            return false;
        }

        _frames++;
        var elapsed = now - _measureStart;
        if (elapsed < MeasureSeconds)
        {
            return false;
        }

        // Aim for the screen's own rate, but no higher than 60: past that, sharpness matters more.
        var goal = Math.Clamp(refreshRate, 24, 60) * 0.9;
        var frameRate = _frames / elapsed;
        _measureStart = now;
        _frames = 0;

        if (_tooSlowStep >= 0 && now >= _tooSlowUntil)
        {
            _tooSlowStep = -1;
        }

        if (frameRate < goal)
        {
            _goodSeconds = 0;
            if (++_slowMeasurements < SlowMeasurementsToStepDown || _step == Shares.Length - 1)
            {
                return false;
            }

            // Straight after a step up, the size it came from kept up: go back there. Otherwise jump
            // towards the share that should keep up. Drawing time goes roughly with pixels, but a frame
            // rate held to the screen's refresh hides how close it came, so go only halfway (in steps).
            var next = _step + 1;
            if (_cameUpFrom > _step)
            {
                next = _cameUpFrom;
            }
            else
            {
                var needed = Shares[_step] * Math.Sqrt(frameRate / goal);
                while (next < Shares.Length - 1 && Shares[next] > needed)
                {
                    next++;
                }
            }

            // Coming back to a size that was too slow last time means it still is: wait longer before trying it again.
            if (_step == _lastTooSlow)
            {
                _forgetSeconds = Math.Min(_forgetSeconds * 2, 300.0);
            }

            _tooSlowStep = _step;
            _lastTooSlow = _step;
            _tooSlowUntil = now + _forgetSeconds;
            _step = next;
            _cameUpFrom = -1;
            _settled[_preset] = _step;
            Restart();
            return true;
        }

        _slowMeasurements = 0;
        _goodSeconds += elapsed;
        if (_step == 0 || _goodSeconds < GoodSecondsToStepUp || _step - 1 <= _tooSlowStep)
        {
            return false;
        }

        // The screen's refresh caps the frame rate, so there's no telling how much room there is: try one step up.
        _cameUpFrom = _step;
        _step--;
        _settled[_preset] = _step;
        Restart();
        return true;
    }

    /// <summary>Starts measuring afresh: the frames just after a size change are slower than the rest.</summary>
    private void Restart()
    {
        _measureStart = -1.0;
        _frames = 0;
        _slowMeasurements = 0;
        _goodSeconds = 0;
    }
}
