using System.Diagnostics;

namespace Lucent.Visualizer;

/// <summary>
/// The mouse over the visuals, for the little hidden trick where moving it over a particle visual pushes
/// the dots aside, and holding the button down grabs a handful to drag around and throw. The window tells
/// it where the mouse is and what its button is doing (<see cref="MovedTo"/>, <see cref="Left"/>,
/// <see cref="Pressed"/>, <see cref="Released"/>) on the UI thread; the renderer calls <see cref="Update"/>
/// once a frame and passes the results to the shaders. Only a moving mouse pushes: once it rests, or
/// leaves, the push fades and the dots drift back.
/// <para>
/// The handful is the "ball" here: the patch of dots where the button went down, carried with the mouse and
/// thrown on when it's let go, its dots easing back home as <see cref="BallHeld"/> fades. The shaders lift
/// that patch afresh each frame from where the visual draws its dots, so it moves as one and stays crisp.
/// </para>
/// </summary>
public sealed class PointerStir
{
    /// <summary>How long after the mouse last moved it still counts as moving.</summary>
    private const double MovingSeconds = 0.15;

    private readonly object _gate = new();
    private double _x;
    private double _y;
    private bool _inside;
    private bool _held;
    private long _movedAt;

    /// <summary>How far the thrown handful carries: its speed falls by this much a second.</summary>
    private const float BallDamping = 2.5f;

    /// <summary>How fast the handful turns in the hand, in radians a second.</summary>
    private const float BallSpin = 0.8f;

    private long _lastUpdate;
    private bool _wasHeld;
    private float _ballVelocityX;
    private float _ballVelocityY;
    private bool _tracking;
    private float _lastX;
    private float _lastY;

    /// <summary>The "Mouse moves particles" option: off, the mouse leaves the dots alone.</summary>
    public bool On { get; set; } = true;

    /// <summary>Where the mouse is, in the shaders' p units (-aspect..aspect across, -1..1 down).</summary>
    public float X { get; private set; }

    public float Y { get; private set; }

    /// <summary>How fast the mouse is moving, in p units per second, smoothed.</summary>
    public float VelocityX { get; private set; }

    public float VelocityY { get; private set; }

    /// <summary>1 while the mouse moves over the visuals, fading to 0 once it rests or leaves.</summary>
    public float Strength { get; private set; }

    /// <summary>1 while the button is held down on the visuals, grabbing dots; 0 otherwise.</summary>
    public float Grab { get; private set; }

    /// <summary>Where the handful is held, or flies once thrown, in p units.</summary>
    public float BallX { get; private set; }

    public float BallY { get; private set; }

    /// <summary>Where the handful was picked up from, in p units.</summary>
    public float BallFromX { get; private set; }

    public float BallFromY { get; private set; }

    /// <summary>1 while the handful is held, easing to 0 after it's thrown as its dust drifts home.</summary>
    public float BallHeld { get; private set; }

    /// <summary>How far the handful has turned since it was picked up, in radians.</summary>
    public float BallTurn { get; private set; }

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

    /// <summary>The left button went down at (<paramref name="x"/>, <paramref name="y"/>) on the visuals.</summary>
    public void Pressed(double x, double y)
    {
        MovedTo(x, y);
        lock (_gate)
        {
            _held = true;
        }
    }

    /// <summary>The left button came up (or the window lost the mouse): whatever was grabbed is let go, at the mouse's speed.</summary>
    public void Released()
    {
        lock (_gate)
        {
            _held = false;
        }
    }

    /// <summary>Works out this frame's position, speed and strength. <paramref name="aspect"/> is the picture's width / height.</summary>
    public void Update(float aspect)
    {
        double x, y;
        bool inside;
        bool held;
        long movedAt;
        lock (_gate)
        {
            (x, y, inside, held, movedAt) = (_x, _y, _inside, _held, _movedAt);
        }

        // Turned off, it's as if the mouse were never there; anything already pushed drifts back.
        if (!On)
        {
            inside = false;
            held = false;
        }

        // While the button is held the window keeps the mouse, so dragging past the edge still counts.
        inside |= held;
        Grab = held ? 1f : 0f;

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
        var follow = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / 0.08f) : 0f;
        VelocityX += (rawX - VelocityX) * follow;
        VelocityY += (rawY - VelocityY) * follow;
        UpdateBall(held, deltaTime);
        var target = moving ? 1f : 0f;
        var ease = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / (moving ? 0.08f : 0.4f)) : 0f;
        Strength += (target - Strength) * ease;
        if (Strength < 1e-3f)
        {
            Strength = 0f;
        }
    }

    /// <summary>The held or thrown handful (see the class notes).</summary>
    private void UpdateBall(bool held, float deltaTime)
    {
        if (held)
        {
            if (!_wasHeld && BallHeld < 0.02f)
            {
                // Picked up fresh: from right here.
                (BallFromX, BallFromY) = (X, Y);
                (BallX, BallY) = (X, Y);
                BallTurn = 0f;
            }

            // Held, it rides just behind the mouse (which also carries a still-flying handful back to the hand smoothly).
            var hold = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / 0.05f) : 0f;
            BallX += (X - BallX) * hold;
            BallY += (Y - BallY) * hold;
            (_ballVelocityX, _ballVelocityY) = (VelocityX, VelocityY);
        }
        else
        {
            // Thrown: it flies on with the mouse's speed, slowing, as the dust eases home.
            BallX += _ballVelocityX * deltaTime;
            BallY += _ballVelocityY * deltaTime;
            var slow = MathF.Exp(-BallDamping * deltaTime);
            _ballVelocityX *= slow;
            _ballVelocityY *= slow;
        }

        _wasHeld = held;
        BallTurn = (BallTurn + BallSpin * deltaTime) % (2f * MathF.PI);
        var ease = deltaTime > 0f ? 1f - MathF.Exp(-deltaTime / (held ? 0.1f : 0.45f)) : 0f;
        BallHeld += ((held ? 1f : 0f) - BallHeld) * ease;
        if (BallHeld < 1e-3f)
        {
            BallHeld = 0f;
        }
    }
}
