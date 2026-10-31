using System.Runtime.InteropServices;

namespace Lucent.Visualizer;

/// <summary>
/// What Windows says about the PC's power, for the Auto power mode (GetSystemPowerStatus): whether it has a
/// battery and runs on it, and whether Energy Saver (Battery saver) is on. Windows has no simple way to
/// ask whether the PC runs hot, so that's left out.
/// </summary>
internal static class PowerSource
{
    public static PowerState Read()
    {
        try
        {
            if (!GetSystemPowerStatus(out var status))
            {
                return default;
            }

            // ACLineStatus: 0 on battery, 1 plugged in, 255 unknown. BatteryFlag: 128 no battery, 255 unknown.
            // SystemStatusFlag: 1 while Energy Saver (Battery saver) is on.
            var hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
            var onBattery = hasBattery && status.ACLineStatus == 0;
            return new PowerState(hasBattery, onBattery, status.SystemStatusFlag == 1, Hot: false);
        }
        catch (Exception)
        {
            return default;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
}
