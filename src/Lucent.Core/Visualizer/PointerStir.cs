using System.Diagnostics;

namespace Lucent.Visualizer;

/// <summary>
/// The mouse over the visuals, for the little hidden trick where moving it over a particle visual pushes
/// the dots aside. The window tells it where the mouse is (<see cref="MovedTo"/>, <see cref="Left"/>) on
/// the UI thread; the renderer calls <see cref="Update"/> once a frame and passes the results to the
/// shaders. Only a moving mouse pushes: once it rests, or leaves, the push fades and the dots drift back.
/// </summary>
public sealed class PointerStir
{
    /// <summary>How long after the mouse last moved it still counts as moving.</summary>
    private const double MovingSeconds = 0.15;

    private readonly object _gate = new();
    private double _x;
    private double _y;
    private bool _inside;
    private long _movedAt;

    private long _lastUpdate;
    private bool _tracking;
    private float _lastX;
    private float _lastY;

    /// <summary>Where the mouse is, in the shaders' p units (-aspect..aspect across, -1..1 down).</summary>
    public float X { get; private set; }

    public float Y { get; private set; }

    /// <summary>How fast the mouse is moving, in p units per second, smoothed.</summary>
    public float VelocityX { get; private set; }

    public float VelocityY { get; private set; }

    /// <summary>1 while the mouse moves over the visuals, fading to 0 once it rests or leaves.</summary>
    public float Strength { get; private set; }

    /// <summary>The mouse is at (<paramref name="x"/>, <paramref name="y"/>), each 0..1 across and down the visuals.</summary>
    public void MovedTo(double x, double y)
    {
        lock (_gate)
        {
            if (_inside && x == _x && y == _y)
            {
                // Windows repeats a move message now and then without the mouse moving.
                return;
            }

            _x = x;
            _y = y;
            _inside = true;
            _movedAt = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>The mouse has left the visuals.</summary>
    public void Left()
    {
        lock (_gate)
        {
            _inside = false;
        }
    }

    /// <summary>Works out this frame's position, speed and strength. <paramref name="aspect"/> is the picture's width / height.</summary>
    public void Update(float aspect)
    {
        double x, y;
        bool inside;
        long movedAt;
        lock (_gate)
        {
            (x, y, inside, movedAt) = (_x, _y, _inside, _movedAt);
        }

        var now = Stopwatch.GetTimestamp();
        var deltaTime = _lastUpdate == 0 ? 0f : (float)Math.Min((now - _lastUpdate) / (double)Stopwatch.Frequency, 0.1);
        _lastUpdate = now;

        var moving = inside && (now - movedAt) / (double)Stopwatch.Frequency < MovingSeconds;
        var px = (float)((x - 0.5) * 2.0 * aspect);
        var py = (float)((y - 0.5) * 2.0);
        if (!inside)
        {
            _tracking = false;
        }
        else if (!_tracking)
        {
            // Just came in: no speed from wherever it was before.
            _tracking = true;
            (_lastX, _lastY) = (px, py);
            X = px;
            Y = py;
        }

        var rawX = 0f;
        var rawY = 0f;
        if (_tracking && deltaTime > 0f)
        {
            rawX = (px - _lastX) / deltaTime;
            rawY = (py - _lastY) / deltaTime;
            (_lastX, _lastY) = (px, py);
            X = px;
            Y = py;
        }

        // Speed is smoothed over a few frames; the push comes in quickly and goes gently.
        var follow = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / 0.05f) : 0f;
        VelocityX += (rawX - VelocityX) * follow;
        VelocityY += (rawY - VelocityY) * follow;
        var target = moving ? 1f : 0f;
        var ease = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / (moving ? 0.08f : 0.4f)) : 0f;
        Strength += (target - Strength) * ease;
        if (Strength < 1e-3f)
        {
            Strength = 0f;
        }
    }
}
