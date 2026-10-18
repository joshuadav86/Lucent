using Avalonia.Controls;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>The graphics options, as items for the visuals' right-click menu. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>Adds Resolution, Detail and Glow submenus, the HDR and frame rate switches, and a way to all the options.</summary>
    public void AddGraphicsItems(ItemsControl menu)
    {
        menu.Items.Add(Choices("Resolution", new[]
        {
            (RenderResolution.Window, "Window size"),
            (RenderResolution.FullHd, "1080p"),
            (RenderResolution.QuadHd, "1440p"),
            (RenderResolution.UltraHd, "4K"),
        }, _settings.Resolution, value => _settings.Resolution = value));
        menu.Items.Add(Choices("Detail", new[]
        {
            (DetailLevel.Low, "Low"),
            (DetailLevel.Normal, "Normal"),
            (DetailLevel.High, "High"),
            (DetailLevel.Ultra, "Ultra"),
        }, _settings.Detail, value => _settings.Detail = value));
        menu.Items.Add(Choices("Glow", new[]
        {
            (GlowLevel.Off, "Off"),
            (GlowLevel.Soft, "Soft"),
            (GlowLevel.Normal, "Normal"),
            (GlowLevel.Strong, "Strong"),
        }, _settings.Glow, value => _settings.Glow = value));

        var hdr = new MenuItem
        {
            Header = "HDR",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.Hdr,
            IsEnabled = _display.HdrOn || _settings.Hdr,
        };
        ToolTip.SetTip(hdr, _display.HdrOn ? null : HdrOffHint);
        hdr.Click += (_, _) => ChangeGraphics(() => _settings.Hdr = !_settings.Hdr);
        menu.Items.Add(hdr);

        var frameRate = new MenuItem { Header = "Show frame rate", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.ShowFrameRate };
        frameRate.Click += (_, _) => ChangeGraphics(() => _settings.ShowFrameRate = !_settings.ShowFrameRate);
        menu.Items.Add(frameRate);

        menu.Items.Add(new Separator());
        var options = new MenuItem { Header = "All options…", InputGesture = Avalonia.Input.KeyGesture.Parse("O") };
        options.Click += (_, _) =>
        {
            if (_visuals.IsFullscreen)
            {
                _visuals.ToggleFullscreen();
            }

            if (!OptionsPopup.IsOpen)
            {
                ToggleOptions();
            }
        };
        menu.Items.Add(options);
    }

    /// <summary>Brings the options panel's switches and choices in line with the settings, after they change elsewhere.</summary>
    public void ShowOptionsState()
    {
        ShowOptionValues();
        UpdateHdrHint();
    }

    private MenuItem Choices<T>(string header, (T Value, string Label)[] choices, T current, Action<T> choose)
        where T : struct, Enum
    {
        var parent = new MenuItem { Header = header };
        foreach (var (value, label) in choices)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = value.Equals(current) };
            item.Click += (_, _) => ChangeGraphics(() => choose(value));
            parent.Items.Add(item);
        }

        return parent;
    }

    private void ChangeGraphics(Action change)
    {
        change();
        ApplyVisualizerSettings();
        ShowOptionsState();
        _settings.Save();
    }
}
