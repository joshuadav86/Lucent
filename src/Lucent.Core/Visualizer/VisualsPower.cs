using Lucent.Settings;

namespace Lucent.Visualizer;

/// <summary>
/// How hard the visuals work (Options › Graphics, "Power"). Set from the UI thread, read by the render
/// threads each frame.
/// <list type="bullet">
/// <item>Smoothest: one frame per screen refresh, kept up while another app has focus.</item>
/// <item>Balanced: about 60 fps. On a faster screen each frame stays up for whole refreshes (two at
/// 120 or 144 Hz), about half the CPU, GPU and compositor work there.</item>
/// <item>Power saver: about 30 fps at half the pixels, and the system may slow Lucent down while
/// another app has focus.</item>
/// </list>
/// With <see cref="SaverOnBattery"/> on, Power saver takes over while the computer runs on battery.
/// </summary>
public static class VisualsPower
{
    // The most refreshes a frame can stay up for: DXGI's sync interval stops at 4.
    private const int MaxRefreshesPerFrame = 4;

    private static volatile PowerMode _mode = PowerMode.Balanced;
    private static volatile bool _saverOnBattery;
    private static volatile bool _onBattery;

    /// <summary>The mode chosen in Options.</summary>
    public static PowerMode Mode
    {
        get => _mode;
        set => _mode = value;
    }

    /// <summary>Use Power saver while on battery, whatever <see cref="Mode"/> is.</summary>
    public static bool SaverOnBattery
    {
        get => _saverOnBattery;
        set => _saverOnBattery = value;
    }

    /// <summary>Whether the computer runs on battery now (checked every few seconds by each app).</summary>
    public static bool OnBattery
    {
        get => _onBattery;
        set => _onBattery = value;
    }

    /// <summary>The mode in use now: <see cref="Mode"/>, or Power saver while on battery with <see cref="SaverOnBattery"/> on.</summary>
    public static PowerMode Effective => _saverOnBattery && _onBattery ? PowerMode.PowerSaver : _mode;

    /// <summary>Whether the visuals ask the system to keep them at full frame rate while another app has focus.</summary>
    public static bool FullRateInBackground => Effective != PowerMode.PowerSaver;

    /// <summary>The share of pixels to draw: half in Power saver, all of them otherwise.</summary>
    public static double PixelShare => Effective == PowerMode.PowerSaver ? 0.5 : 1.0;

    /// <summary>The most frames a second the mode draws, or 0 for the screen's own rate.</summary>
    public static int MaxFrameRate => Effective switch
    {
        PowerMode.Balanced => 60,
        PowerMode.PowerSaver => 30,
        _ => 0,
    };

    /// <summary>
    /// How many screen refreshes each frame stays up for: 1 for the screen's own rate, and 1 too when
    /// the screen is already about as slow as the mode's limit (a 75 Hz screen in Balanced).
    /// </summary>
    public static int RefreshesPerFrame(int refreshRate)
    {
        var limit = MaxFrameRate;
        if (limit <= 0 || refreshRate < limit * 1.5)
        {
            return 1;
        }

        return Math.Clamp((int)Math.Round(refreshRate / (double)limit), 1, MaxRefreshesPerFrame);
    }

    /// <summary>The frame rate the visuals can reach on a screen at <paramref name="refreshRate"/>.</summary>
    public static int FrameRate(int refreshRate) => Math.Max(1, refreshRate / RefreshesPerFrame(refreshRate));

    /// <summary>
    /// The least time each frame should stay on screen, in seconds, or 0 for no limit. A little under the
    /// refreshes it should take, so timing noise never rounds it up to one refresh more.
    /// </summary>
    public static double MinimumFrameSeconds(int refreshRate)
    {
        var refreshes = RefreshesPerFrame(refreshRate);
        return refreshes <= 1 ? 0 : (refreshes - 0.25) / refreshRate;
    }
}
