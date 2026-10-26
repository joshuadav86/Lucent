namespace Lucent.Visualizer;

/// <summary>How the visualizer has been drawing over the last second.</summary>
public readonly record struct FrameStats(double FramesPerSecond, int Width, int Height, bool Hdr);
