using Avalonia.Controls;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.ClassicSkins;

namespace Lucent.Mac;

// The equalizer in a classic skin: Winamp 2's equalizer (eqmain.bmp) with its ten sliders, in place of
// Lucent's own. Lucent.Core's ClassicEqualizer draws it; this feeds it the settings and acts on its sliders.
public partial class EqualizerWindow
{
    private ClassicEqualizer? _classic;
    private ClassicSurfaceView? _classicView;
    private object? _lucentContent;

    /// <summary>The classic skin the player shows, and its zoom, or null for Lucent's own look. A new equalizer takes it on as it opens.</summary>
    internal static (ClassicSkin Skin, double Zoom)? ClassicSkin { get; set; }

    /// <summary>Shows this equalizer in a classic skin, or as Lucent's own with null.</summary>
    internal void SetClassicSkin(ClassicSkin? skin, double zoom)
    {
        if (skin is null)
        {
            if (_classic is null)
            {
                return;
            }

            _classic = null;
            Content = _lucentContent;
            // Back to Lucent's band counts, keeping the sound the ten sliders made.
            Equalizer.ChangeBands(Eq, EqualizerBands.ForCount(Eq.BandCount));
            BuildBands();
            ShowSettings();
            FitToContent();
            Apply();
            return;
        }

        FinishGlide();
        if (_classic is null)
        {
            _lucentContent = Content;
            _classic = new ClassicEqualizer(skin);
            _classic.Command += OnClassicCommand;
            _classic.BandChanged += (band, db) =>
            {
                Eq.Gains[band] = db;
                Eq.PresetName = null;
                ClassicChanged();
            };
            _classic.PreampChanged += db =>
            {
                Eq.PreampDb = db;
                ClassicChanged();
            };
            _classicView = new ClassicSurfaceView(_classic);
            Activated += (_, _) => RefreshClassic();
            Deactivated += (_, _) => RefreshClassic();
        }

        // Ten sliders, as on Winamp's, sounding as close as they can to what was set before.
        Equalizer.ChangeBands(Eq, EqualizerBands.Classic);
        _player.Equalizer.Apply(Eq);
        _classic.Skin = skin;
        _classicView!.Zoom = zoom;
        Content = _classicView;
        RefreshClassic();
        Width = ClassicSprites.WindowWidth * _classicView.Scale;
        Height = ClassicSprites.WindowHeight * _classicView.Scale;
    }

    /// <summary>Draws the classic equalizer again with the settings as they are now.</summary>
    private void RefreshClassic()
    {
        if (_classic is null)
        {
            return;
        }

        _classic.Enabled = Eq.Enabled;
        _classic.PreampDb = Eq.PreampDb;
        for (var band = 0; band < ClassicEqualizer.BandCount; band++)
        {
            _classic.Gains[band] = band < Eq.Gains.Count ? Eq.Gains[band] : 0;
        }

        _classic.IsActive = IsActive;
        _classicView!.Refresh();
    }

    // A slider moved: on with the change, as Lucent's own sliders do.
    private void ClassicChanged()
    {
        Eq.Enabled = true;
        _player.Equalizer.Apply(Eq);
        SaveSoon();
        RefreshClassic();
    }

    private void OnClassicCommand(ClassicEqualizerCommand command)
    {
        switch (command)
        {
            case ClassicEqualizerCommand.ToggleOn:
                Eq.Enabled = !Eq.Enabled;
                _player.Equalizer.Apply(Eq);
                SaveSoon();
                RefreshClassic();
                break;
            case ClassicEqualizerCommand.Presets:
                ShowClassicPresets();
                break;
            case ClassicEqualizerCommand.Close:
                Close();
                break;
        }
    }

    /// <summary>The PRESETS menu: Lucent's presets and the ones saved in Lucent's own equalizer.</summary>
    private void ShowClassicPresets()
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (var preset in EqualizerPresets.BuiltIn)
        {
            menu.Items.Add(ClassicPresetItem(preset.Name, preset.Curve));
        }

        if (Eq.CustomPresets.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var saved in Eq.CustomPresets)
            {
                menu.Items.Add(ClassicPresetItem(saved.Name, CurveOf(saved)));
            }
        }

        menu.Open(_classicView);
    }

    private MenuItem ClassicPresetItem(string name, EqualizerCurve curve)
    {
        var item = new MenuItem
        {
            Header = name,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = string.Equals(name, Eq.PresetName, StringComparison.OrdinalIgnoreCase),
        };
        item.Click += (_, _) =>
        {
            Eq.Gains = curve.Sample(EqualizerBands.Classic).ToList();
            Eq.PresetName = name;
            ClassicChanged();
        };
        return item;
    }
}
