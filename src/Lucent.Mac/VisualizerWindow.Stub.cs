// STUB: owned by visuals worker
using Lucent.Mac.Visualizer;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

public partial class VisualizerWindow : ToolWindow
{
    public VisualizerWindow()
    {
        Title = "Lucent Visuals";
        PlacementName = "Visuals";
        Width = 860;
        Height = 520;
        MinWidth = 320;
        MinHeight = 200;
    }

    public bool IsFullscreen => false;

    public void Attach(VisualizerHost host, AppSettings settings)
    {
    }

    public void ShowFrameRate(bool show)
    {
    }

    public void ChangePreset(int step)
    {
    }

    public void TogglePresetPicker()
    {
    }

    public void ToggleFavorite()
    {
    }

    public void ToggleAutoCycle()
    {
    }

    public void UpdateAutoCycleButton()
    {
    }

    public void ToggleFullscreen()
    {
    }
}
