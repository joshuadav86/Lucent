using System.Runtime.InteropServices;

namespace Lucent.Visualizer;

/// <summary>
/// Keeps the visuals at full frame rate while another app has focus (on in the Smoothest and Balanced
/// Power modes, off in Power saver; see Lucent.Visualizer.VisualsPower). Windows 11 runs a visible window that isn't in front at a lower quality
/// of service than the one in front: on a laptop or a CPU with efficiency cores its threads can be
/// moved to the slower cores and run at lower clock speeds, and the visuals drop frames on a second
/// screen as soon as you click away. Turning off power throttling for Lucent asks for full speed, and
/// the render thread runs a step above normal priority so a busy app in front doesn't crowd it out.
/// Nothing is drawn at all while the visuals are minimized or hidden, so this costs nothing then.
/// </summary>
internal static class BackgroundFrameRate
{
    private const int ProcessPowerThrottling = 4;
    private const uint PowerThrottlingCurrentVersion = 1;
    private const uint PowerThrottlingExecutionSpeed = 0x1;

    private static volatile bool _on;

    /// <summary>Whether the visuals keep full frame rate in the background. Set from the UI thread.</summary>
    public static bool On
    {
        get => _on;
        set
        {
            _on = value;
            ApplyToProcess(value);
        }
    }

    /// <summary>
    /// Called by the render thread every frame: raises or lowers its own priority once the switch
    /// changes. <paramref name="applied"/> is the render thread's note of what it last set.
    /// </summary>
    public static void KeepUp(ref bool applied)
    {
        var on = _on;
        if (on == applied)
        {
            return;
        }

        applied = on;
        try
        {
            Thread.CurrentThread.Priority = on ? ThreadPriority.AboveNormal : ThreadPriority.Normal;
        }
        catch (Exception)
        {
            // Left as it was; the visuals still draw.
        }
    }

    /// <summary>
    /// On: opts Lucent out of execution-speed throttling (EcoQoS and the in-between quality of
    /// service unfocused windows get). Off: hands the choice back to Windows.
    /// </summary>
    private static void ApplyToProcess(bool on)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 16299))
        {
            return;
        }

        var state = new PowerThrottlingState
        {
            Version = PowerThrottlingCurrentVersion,
            ControlMask = on ? PowerThrottlingExecutionSpeed : 0,
            StateMask = 0,
        };
        try
        {
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PowerThrottlingState>());
        }
        catch (Exception)
        {
            // Older Windows without the call: nothing to turn off.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(IntPtr process, int informationClass, ref PowerThrottlingState information, uint size);
}
