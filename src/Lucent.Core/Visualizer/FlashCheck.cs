namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Watches a visual's brightness frame by frame and counts how often it swings between light and dark, so the
/// self-tests can point out a visual that strobes (a MilkDrop preset like "Crossroads" flashed the whole screen
/// on every beat). A swing is a change of a tenth of full brightness in the frame as a whole, the threshold the
/// WCAG flashing guideline uses; two swings, up and back, are one flash.
/// </summary>
public sealed class FlashCheck
{
    /// <summary>How far the whole picture's brightness (0 to 1) has to move to count as a swing.</summary>
    public const double Swing = 0.1;

    /// <summary>More flashes a second than this is a strobe (WCAG allows three).</summary>
    public const double MostFlashesPerSecond = 3;

    // Rising (1), falling (-1), or not yet moved a whole swing (0), and the brightest it has been while
    // rising or the darkest while falling. Before the first swing, the darkest and brightest so far.
    private int _direction;
    private double _extreme;
    private double _darkest = double.MaxValue;
    private double _brightest = double.MinValue;

    /// <summary>How many frames it has seen.</summary>
    public int Frames { get; private set; }

    /// <summary>How many times the brightness has swung up or down by <see cref="Swing"/> or more.</summary>
    public int Swings { get; private set; }

    /// <summary>Adds the next frame's brightness, from 0 (black) to 1 (white).</summary>
    public void Add(double brightness)
    {
        Frames++;
        if (_direction == 0)
        {
            _darkest = Math.Min(_darkest, brightness);
            _brightest = Math.Max(_brightest, brightness);
            if (brightness - _darkest >= Swing || _brightest - brightness >= Swing)
            {
                _direction = brightness - _darkest >= Swing ? 1 : -1;
                _extreme = brightness;
                Swings++;
            }

            return;
        }

        if ((brightness - _extreme) * _direction > 0)
        {
            _extreme = brightness;
        }
        else if (Math.Abs(brightness - _extreme) >= Swing)
        {
            _direction = -_direction;
            _extreme = brightness;
            Swings++;
        }
    }

    /// <summary>Flashes a second over the frames seen, each <paramref name="frameSeconds"/> long.</summary>
    public double FlashesPerSecond(double frameSeconds) =>
        Frames < 2 ? 0 : Swings / 2.0 / ((Frames - 1) * frameSeconds);

    /// <summary>Whether it flashed more than <see cref="MostFlashesPerSecond"/> times a second.</summary>
    public bool IsStrobing(double frameSeconds) => FlashesPerSecond(frameSeconds) > MostFlashesPerSecond;
}
