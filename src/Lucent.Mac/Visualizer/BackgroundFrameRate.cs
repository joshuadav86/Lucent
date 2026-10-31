using System.Runtime.InteropServices;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Visualizer;

/// <summary>
/// Keeps the visuals at full frame rate while another app has focus (Options › Graphics, "Full frame
/// rate in the background"). macOS gives the app in front the fast cores and the quickest timers;
/// an app behind it gets less, and its threads at the default quality of service can be moved to
/// the efficiency cores, so the visuals drop frames on a second screen as soon as you click away.
/// With this on, the render thread asks for the quality of service the screen's own refresh uses
/// (user-interactive), and while the visuals are on screen Lucent tells macOS it's doing
/// latency-critical work the user started, which keeps App Nap and timer coalescing off. Once the
/// visuals are minimized, hidden, or fully covered, that's let go, so macOS can nap Lucent as usual
/// (nothing is drawn while they're minimized or hidden anyway).
/// </summary>
internal static partial class BackgroundFrameRate
{
    private const int QosClassUserInteractive = 0x21;
    private const int QosClassDefault = 0x15;

    // NSActivityUserInitiatedAllowingIdleSystemSleep | NSActivityLatencyCritical: the Mac can still
    // sleep when idle, and the display isn't kept on.
    private const ulong ActivityOptions = (0x00FFFFFFUL & ~(1UL << 20)) | 0xFF00000000UL;

    private static volatile bool _on;
    private static readonly HashSet<object> Holders = new();
    private static IntPtr _activity;

    /// <summary>Whether the visuals keep full frame rate in the background. Set from the UI thread.</summary>
    public static bool On
    {
        get => _on;
        set
        {
            _on = value;
            UpdateActivity();
        }
    }

    /// <summary>
    /// Called by the render thread every frame: changes its own quality of service once the switch
    /// changes. <paramref name="applied"/> is the render thread's note of what it last set.
    /// </summary>
    public static void KeepUp(ref bool applied)
    {
        var on = _on;
        if (on == applied || !OperatingSystem.IsMacOS())
        {
            return;
        }

        applied = on;
        try
        {
            pthread_set_qos_class_self_np(on ? QosClassUserInteractive : QosClassDefault, 0);
        }
        catch (Exception)
        {
            // Left as it was; the visuals still draw.
        }
    }

    /// <summary>
    /// From the UI thread: whether <paramref name="holder"/> (a visuals view) is showing on screen
    /// now. While one is and the switch is on, App Nap is kept off.
    /// </summary>
    public static void Showing(object holder, bool showing)
    {
        if (showing ? Holders.Add(holder) : Holders.Remove(holder))
        {
            UpdateActivity();
        }
    }

    private static void UpdateActivity()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var wanted = _on && Holders.Count > 0;
        try
        {
            var processInfo = ObjC.Send(ObjC.Class("NSProcessInfo"), "processInfo");
            if (wanted && _activity == IntPtr.Zero)
            {
                var reason = StringHelper.Create("Drawing the visuals").NativePtr;
                var activity = Send(processInfo, ObjC.Selector("beginActivityWithOptions:reason:"), ActivityOptions, reason);
                _activity = activity == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(activity, "retain");
            }
            else if (!wanted && _activity != IntPtr.Zero)
            {
                ObjC.SendVoid(processInfo, "endActivity:", _activity);
                ObjC.Release(_activity);
                _activity = IntPtr.Zero;
            }
        }
        catch (Exception)
        {
            // macOS manages Lucent as it would any app.
        }
    }

    [LibraryImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static partial IntPtr Send(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

    [LibraryImport("/usr/lib/libSystem.B.dylib")]
    private static partial int pthread_set_qos_class_self_np(int qosClass, int relativePriority);
}
