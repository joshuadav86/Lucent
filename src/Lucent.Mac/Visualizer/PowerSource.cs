using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using Lucent.Visualizer;

namespace Lucent.Mac.Visualizer;

/// <summary>
/// What the Mac says about its power and heat, for the Auto power mode: whether it has a battery and runs
/// on it (IOKit's power sources), whether Low Power Mode is on, and whether it's running hot (thermal state).
/// </summary>
internal static partial class PowerSource
{
    private const string IoKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // NSProcessInfoThermalStateSerious: macOS is already slowing things down to cool off.
    private const long ThermalStateSerious = 2;

    public static PowerState Read()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return default;
        }

        var (hasBattery, onBattery) = ReadBattery();
        var lowPower = false;
        var hot = false;
        try
        {
            var processInfo = ObjC.Send(ObjC.Class("NSProcessInfo"), "processInfo");
            if (ObjC.SendBool(processInfo, "respondsToSelector:", ObjC.Selector("isLowPowerModeEnabled")))
            {
                lowPower = ObjC.SendBool(processInfo, "isLowPowerModeEnabled");
            }

            hot = (long)ObjC.Send(processInfo, "thermalState") >= ThermalStateSerious;
        }
        catch (Exception)
        {
            // Left as normal.
        }

        return new PowerState(hasBattery, onBattery, lowPower, hot);
    }

    private static (bool HasBattery, bool OnBattery) ReadBattery()
    {
        try
        {
            var info = IOPSCopyPowerSourcesInfo();
            if (info == IntPtr.Zero)
            {
                return (false, false);
            }

            try
            {
                // "Battery Power", "AC Power" or "UPS Power"; CF strings, arrays and dictionaries are also
                // their NS counterparts, so they're read with plain messages.
                var providing = IOPSGetProvidingPowerSourceType(info);
                var onBattery = providing != IntPtr.Zero && Text(providing) == "Battery Power";

                var hasBattery = false;
                var list = IOPSCopyPowerSourcesList(info);
                if (list != IntPtr.Zero)
                {
                    try
                    {
                        var typeKey = StringHelper.Create("Type").NativePtr;
                        var count = (long)ObjC.Send(list, "count");
                        for (var i = 0; i < count && !hasBattery; i++)
                        {
                            var source = ObjC.Send(list, "objectAtIndex:", (IntPtr)i);
                            var description = IOPSGetPowerSourceDescription(info, source);
                            var type = description == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(description, "objectForKey:", typeKey);
                            hasBattery = type != IntPtr.Zero && Text(type) == "InternalBattery";
                        }
                    }
                    finally
                    {
                        CFRelease(list);
                    }
                }

                return (hasBattery || onBattery, onBattery);
            }
            finally
            {
                CFRelease(info);
            }
        }
        catch (Exception)
        {
            return (false, false);
        }
    }

    private static string? Text(IntPtr nsString) => Marshal.PtrToStringUTF8(ObjC.Send(nsString, "UTF8String"));

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSCopyPowerSourcesInfo();

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSCopyPowerSourcesList(IntPtr snapshot);

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSGetPowerSourceDescription(IntPtr snapshot, IntPtr source);

    [LibraryImport(IoKit)]
    private static partial IntPtr IOPSGetProvidingPowerSourceType(IntPtr snapshot);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);
}
