using System.Runtime.InteropServices;

namespace Lucent.Visualizer;

/// <summary>Whether the PC runs on battery, for "Power saver on battery" (GetSystemPowerStatus).</summary>
internal static class PowerSource
{
    /// <summary>True while the PC is running on its battery; false on power, on a PC without one, or if Windows can't say.</summary>
    public static bool OnBattery()
    {
        try
        {
            // ACLineStatus: 0 offline (on battery), 1 online, 255 unknown.
            return GetSystemPowerStatus(out var status) && status.ACLineStatus == 0;
        }
        catch (Exception)
        {
            return false;
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
