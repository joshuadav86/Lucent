using SharpMetal.Metal;

namespace Lucent.Mac.Visuals;

/// <summary>Finds the Mac's graphics chip for the visuals, or says there's none to use.</summary>
public static class MetalSupport
{
    /// <summary>
    /// The default Metal device, or null when there isn't one: a Mac without Metal, or the screenshot
    /// tool running on Linux, where Metal's library isn't there at all.
    /// </summary>
    public static MTLDevice? TryCreateDevice()
    {
        try
        {
            var device = MTLDevice.CreateSystemDefaultDevice();
            return device.NativePtr == IntPtr.Zero ? null : device;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            return null;
        }
    }
}
