using Avalonia.Controls;
using Lucent.Settings;
using Lucent.Visualizer;

namespace Lucent.Mac;

/// <summary>The graphics options, as items for the visuals' right-click menu. Kept in its own file.</summary>
public partial class MainWindow
{
    /// <summary>Adds Resolution, Detail and Glow submenus, the build-up, HDR and frame rate switches, and a way to all the options.</summary>
    public void AddGraphicsItems(ItemsControl menu)
    {
        menu.Items.Add(Choices("Resolution", new[]
        {
            (RenderResolution.Auto, "Automatic"),
            (RenderResolution.Window, "Window size"),
            (RenderResolution.FullHd, "1080p"),
            (RenderResolution.QuadHd, "1440p"),
            (RenderResolution.UltraHd, "4K"),
        }, _settings.Resolution, value => _settings.Resolution = value, MenuTips.Resolution));
        menu.Items.Add(Choices("Detail", new[]
        {
            (DetailLevel.Low, "Low"),
            (DetailLevel.Normal, "Normal"),
            (DetailLevel.High, "High"),
            (DetailLevel.Ultra, "Ultra"),
        }, _settings.Detail, value => _settings.Detail = value, MenuTips.Detail));
        menu.Items.Add(Choices("Glow", new[]
        {
            (GlowLevel.Off, "Off"),
            (GlowLevel.Soft, "Soft"),
            (GlowLevel.Normal, "Normal"),
            (GlowLevel.Strong, "Strong"),
        }, _settings.Glow, value => _settings.Glow = value, MenuTips.Glow));
        menu.Items.Add(Choices("Power", new[]
        {
            (PowerMode.Smoothest, "Smoothest"),
            (PowerMode.Balanced, "Balanced"),
            (PowerMode.PowerSaver, "Power saver"),
        }, _settings.Power, value => _settings.Power = value, MenuTips.Power));

        var buildUps = new MenuItem { Header = "Build-ups and drops", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.BuildUpsAndDrops };
        ToolTip.SetTip(buildUps, MenuTips.BuildUpsAndDrops);
        buildUps.Click += (_, _) => ChangeGraphics(() => _settings.BuildUpsAndDrops = !_settings.BuildUpsAndDrops);
        menu.Items.Add(buildUps);

        var mouseParticles = new MenuItem { Header = "Mouse moves particles", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.MouseMovesParticles };
        ToolTip.SetTip(mouseParticles, MenuTips.MouseMovesParticles);
        mouseParticles.Click += (_, _) => ChangeGraphics(() => _settings.MouseMovesParticles = !_settings.MouseMovesParticles);
        menu.Items.Add(mouseParticles);

        var hdr = new MenuItem
        {
            Header = "HDR",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _settings.Hdr,
            IsEnabled = _display.HdrOn || _settings.Hdr,
        };
        ToolTip.SetTip(hdr, _display.HdrOn ? MenuTips.Hdr : HdrOffHint);
        hdr.Click += (_, _) => ChangeGraphics(() => _settings.Hdr = !_settings.Hdr);
        menu.Items.Add(hdr);

        var frameRate = new MenuItem { Header = "Show frame rate", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.ShowFrameRate };
        ToolTip.SetTip(frameRate, MenuTips.FrameRate);
        frameRate.Click += (_, _) => ChangeGraphics(() => _settings.ShowFrameRate = !_settings.ShowFrameRate);
        menu.Items.Add(frameRate);

        var battery = new MenuItem { Header = "Power saver on battery", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.PowerSaverOnBattery };
        ToolTip.SetTip(battery, MenuTips.PowerSaverOnBattery);
        battery.Click += (_, _) => ChangeGraphics(() => _settings.PowerSaverOnBattery = !_settings.PowerSaverOnBattery);
        menu.Items.Add(battery);

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

    private MenuItem Choices<T>(string header, (T Value, string Label)[] choices, T current, Action<T> choose, string tip)
        where T : struct, Enum
    {
        var parent = new MenuItem { Header = header };
        ToolTip.SetTip(parent, tip);
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
