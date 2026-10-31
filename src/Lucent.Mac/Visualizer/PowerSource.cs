using System.Runtime.InteropServices;
using Lucent.Mac.Interop;

namespace Lucent.Mac.Visualizer;

/// <summary>Whether the Mac runs on battery, for "Power saver on battery" (IOKit's power sources).</summary>
internal static partial class PowerSource
{
    private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    /// <summary>True while the Mac is running on its battery; false on power, on a Mac without one, or if macOS can't say.</summary>
    public static bool OnBattery()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return false;
        }

        try
        {
            var info = IOPSCopyPowerSourcesInfo();
            if (info == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                // "Battery Power", "AC Power" or "UPS Power"; a CFString, which is also an NSString.
                var type = IOPSGetProvidingPowerSourceType(info);
                if (type == IntPtr.Zero)
                {
                    return false;
                }

                var text = Marshal.PtrToStringUTF8(ObjC.Send(type, "UTF8String"));
                return string.Equals(text, "Battery Power", StringComparison.Ordinal);
            }
            finally
            {
                CFRelease(info);
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSCopyPowerSourcesInfo();

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSGetProvidingPowerSourceType(IntPtr snapshot);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);
}
