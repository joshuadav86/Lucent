using System.Windows.Threading;
using Lucent.Settings;
using Lucent.Visualizer;

namespace Lucent;

/// <summary>The Power choice (Auto, Smoothest, Balanced, Power saver). Kept in its own file.</summary>
public partial class MainWindow
{
    private DispatcherTimer? _powerCheck;

    /// <summary>
    /// Hands the Power choice to the visuals. With Auto, also reads the computer's power and heat now and
    /// every few seconds after, so Auto follows it being unplugged, put in low power mode, or running hot.
    /// </summary>
    private void ApplyPower()
    {
        VisualsPower.Mode = _settings.Power;
        if (_settings.Power == PowerMode.Auto)
        {
            VisualsPower.State = PowerSource.Read();
            if (_powerCheck is null)
            {
                _powerCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
                _powerCheck.Tick += (_, _) => CheckPower();
            }

            _powerCheck.Start();
        }
        else
        {
            _powerCheck?.Stop();
        }

        BackgroundFrameRate.On = VisualsPower.FullRateInBackground;
    }

    private void CheckPower()
    {
        var state = PowerSource.Read();
        if (state != VisualsPower.State)
        {
            VisualsPower.State = state;
            BackgroundFrameRate.On = VisualsPower.FullRateInBackground;
        }
    }
}
