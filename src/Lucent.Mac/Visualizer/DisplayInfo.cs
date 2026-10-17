using Lucent.Mac.Interop;

namespace Lucent.Mac.Visualizer;

/// <summary>
/// What the screen showing the visualizer can do: whether it can show brighter than normal white (HDR,
/// which macOS calls EDR: an HDR screen, or a Mac's own XDR or Liquid Retina screen), the brightness of
/// normal (SDR) white, and its peak brightness. As on Windows, brightness is in nits where 80 is the
/// unit the shaders count in; on a Mac that unit is the screen's own normal white, whatever its
/// brightness setting, so <see cref="SdrWhiteNits"/> is always 80 and <see cref="PeakNits"/> is 80
/// times how many times brighter than normal white the screen can go right now.
/// </summary>
public readonly record struct DisplayInfo(bool HdrOn, float SdrWhiteNits, float PeakNits)
{
    public static DisplayInfo Standard => new(false, 80f, 80f);

    /// <summary>Describes the screen that shows most of the given NSWindow. Call it on the UI thread.</summary>
    public static DisplayInfo ForWindow(IntPtr window)
    {
        if (!OperatingSystem.IsMacOS() || window == IntPtr.Zero)
        {
            return Standard;
        }

        var screen = ObjC.Send(window, "screen");
        if (screen == IntPtr.Zero)
        {
            return Standard;
        }

        // How far above normal white the screen could go at best (1 on a screen without HDR), and how
        // far it can right now: that depends on the brightness setting, and reads 1 until something on
        // screen asks for HDR.
        var potential = ObjC.SendDouble(screen, "maximumPotentialExtendedDynamicRangeColorComponentValue");
        var current = ObjC.SendDouble(screen, "maximumExtendedDynamicRangeColorComponentValue");
        var hdrOn = potential > 1.0;
        var headroom = current > 1.0 ? current : potential;
        var peak = (float)(80.0 * Math.Round(Math.Clamp(headroom, 1.0, 125.0), 2));
        return new DisplayInfo(hdrOn, 80f, Math.Max(peak, 80f * 1.25f));
    }
}
