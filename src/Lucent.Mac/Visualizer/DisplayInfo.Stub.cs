// STUB: owned by visuals worker
namespace Lucent.Mac.Visualizer;

public readonly record struct DisplayInfo(bool HdrOn, float SdrWhiteNits, float PeakNits)
{
    public static DisplayInfo Standard => new(false, 80f, 80f);
}
