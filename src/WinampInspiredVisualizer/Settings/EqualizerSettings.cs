namespace WinampInspiredVisualizer.Settings;

/// <summary>The equalizer's settings, kept between runs as part of <see cref="AppSettings"/>.</summary>
public sealed class EqualizerSettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>How many sliders the equalizer has: 3, 4, 5, 8, 10, 16, 24 or 31.</summary>
    public int BandCount { get; set; } = 10;

    /// <summary>Each slider's gain in decibels, lowest band first.</summary>
    public List<double> Gains { get; set; } = new();

    public double PreampDb { get; set; }

    /// <summary>The preset the sliders were last set from, or null once they've been moved by hand.</summary>
    public string? PresetName { get; set; } = "Flat";

    /// <summary>Presets people have saved themselves.</summary>
    public List<SavedEqualizerPreset> CustomPresets { get; set; } = new();

    /// <summary>Where the equalizer window was last, in screen units, or null to place it beside the player.</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }
}

/// <summary>A preset someone saved: their curve as frequency and gain pairs, so it fits any band count.</summary>
public sealed class SavedEqualizerPreset
{
    public string Name { get; set; } = "";

    public List<double> Frequencies { get; set; } = new();

    public List<double> Gains { get; set; } = new();
}
