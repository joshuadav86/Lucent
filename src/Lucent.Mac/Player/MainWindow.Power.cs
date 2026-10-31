using Avalonia.Threading;
using Lucent.Mac.Visualizer;
using Lucent.Settings;
using Lucent.Visualizer;

namespace Lucent.Mac;

/// <summary>The Power choice (Smoothest, Balanced, Power saver) and Power saver on battery. Kept in its own file.</summary>
public partial class MainWindow
{
    private DispatcherTimer? _batteryCheck;

    /// <summary>
    /// Hands the Power choice to the visuals, and while Power saver should take over on battery, looks
    /// every few seconds whether the computer is on battery.
    /// </summary>
    private void ApplyPower()
    {
        VisualsPower.Mode = _settings.Power;
        VisualsPower.SaverOnBattery = _settings.PowerSaverOnBattery;
        if (_settings.PowerSaverOnBattery && _settings.Power != PowerMode.PowerSaver)
        {
            VisualsPower.OnBattery = PowerSource.OnBattery();
            if (_batteryCheck is null)
            {
                _batteryCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _batteryCheck.Tick += (_, _) => CheckBattery();
            }

            _batteryCheck.Start();
        }
        else
        {
            _batteryCheck?.Stop();
        }

        BackgroundFrameRate.On = VisualsPower.FullRateInBackground;
    }

    private void CheckBattery()
    {
        var onBattery = PowerSource.OnBattery();
        if (onBattery != VisualsPower.OnBattery)
        {
            VisualsPower.OnBattery = onBattery;
            BackgroundFrameRate.On = VisualsPower.FullRateInBackground;
        }
    }
}
