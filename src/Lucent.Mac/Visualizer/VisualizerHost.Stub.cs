// STUB: owned by visuals worker
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac.Visualizer;

public sealed class VisualizerHost : IDisposable
{
    public VisualizerHost(AudioCapture capture)
    {
    }

    public event EventHandler<DisplayInfo>? DisplayChanged;

    public SongClock SongClock { get; } = new();

    public string? StartPresetName { get; set; }

    public int TargetPixels { get; set; }

    public float Quality { get; set; }

    public float BloomStrength { get; set; }

    public bool Hdr { get; set; }

    public bool AutoCycle { get; set; }

    public float AutoCycleSeconds { get; set; }

    public float TransitionSeconds { get; set; }

    public bool MorphTransitionsOnly { get; set; }

    public void Dispose() => DisplayChanged?.GetType();
}
