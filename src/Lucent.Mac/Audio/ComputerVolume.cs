using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// The volume of the Mac's default output (the one the menu bar's Sound control and the volume keys set), which every
/// app's sound goes through. macOS applies it after the process tap that <see cref="SystemAudio"/> listens with, so
/// turning it down doesn't change what the visuals and analyzer hear.
/// </summary>
/// <remarks>Windows' Audio/ComputerVolume.cs does the same with the default output's endpoint volume.</remarks>
internal static unsafe class ComputerVolume
{
    private const uint VirtualMainVolumeProperty = 0x766D7663; // 'vmvc': the one volume macOS shows for the whole output
    private const uint VolumeScalarProperty = 0x766F6C6D;      // 'volm': one channel's (or the main element's) volume
    private const uint MuteProperty = 0x6D757465;              // 'mute'
    private const uint DecibelsProperty = 0x766D6462;          // 'vmdb': an element's volume in decibels
    private const uint DecibelRangeProperty = 0x76646223;      // 'vdb#': the decibels it goes from and to

    /// <summary>The volume from 0 to 1 and whether it's muted, or null when the output has no volume to read (some HDMI and digital outputs).</summary>
    public static (double Level, bool Muted)? Read()
    {
        try
        {
            if (DefaultOutput() is not { } device)
            {
                return null;
            }

            float? level = Get(device, VirtualMainVolumeProperty, ElementMain) ?? Get(device, VolumeScalarProperty, ElementMain);
            if (level is null && Get(device, VolumeScalarProperty, 1) is { } left)
            {
                level = Get(device, VolumeScalarProperty, 2) is { } right ? Math.Max(left, right) : left;
            }

            if (level is not { } found)
            {
                return null;
            }

            var mute = new AudioObjectPropertyAddress(MuteProperty, ScopeOutput);
            uint muted = 0;
            var size = (uint)sizeof(uint);
            var isMuted = AudioObjectGetPropertyData(device, &mute, 0, null, ref size, &muted) == 0 && muted != 0;
            return (Math.Clamp(found, 0, 1), isMuted);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Sets the volume, unmuting when it's turned up from muted. Does nothing when the output has no volume.</summary>
    public static void Set(double level)
    {
        try
        {
            if (DefaultOutput() is not { } device)
            {
                return;
            }

            var value = (float)Math.Clamp(level, 0, 1);
            if (!Put(device, VirtualMainVolumeProperty, ElementMain, value) && !Put(device, VolumeScalarProperty, ElementMain, value))
            {
                // No main volume: set the left and right channels' instead.
                Put(device, VolumeScalarProperty, 1, value);
                Put(device, VolumeScalarProperty, 2, value);
            }

            if (value > 0)
            {
                var mute = new AudioObjectPropertyAddress(MuteProperty, ScopeOutput);
                uint off = 0;
                AudioObjectSetPropertyData(device, &mute, 0, null, sizeof(uint), &off);
            }
        }
        catch (Exception)
        {
            // The output went away between reads: the next read shows whatever's there now.
        }
    }

    /// <summary>
    /// Turns the volume down by <paramref name="decibels"/> (as far as the output goes). False when the output has no
    /// volume in decibels to change.
    /// </summary>
    public static bool TurnDown(double decibels)
    {
        try
        {
            if (DefaultOutput() is not { } device)
            {
                return false;
            }

            // The main element where the output has one, otherwise the left and right channels.
            var turned = false;
            foreach (var element in Get(device, DecibelsProperty, ElementMain) is null ? new uint[] { 1, 2 } : new uint[] { ElementMain })
            {
                if (Get(device, DecibelsProperty, element) is not { } now)
                {
                    continue;
                }

                var wanted = now - decibels;
                var range = new AudioObjectPropertyAddress(DecibelRangeProperty, ScopeOutput) { Element = element };
                var limits = default(AudioValueRange);
                var size = (uint)sizeof(AudioValueRange);
                if (AudioObjectGetPropertyData(device, &range, 0, null, ref size, &limits) == 0)
                {
                    wanted = Math.Clamp(wanted, limits.Minimum, limits.Maximum);
                }

                turned |= Put(device, DecibelsProperty, element, (float)wanted);
            }

            return turned;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static uint? DefaultOutput()
    {
        var address = new AudioObjectPropertyAddress(DefaultOutputDeviceProperty);
        uint device = 0;
        var size = (uint)sizeof(uint);
        return AudioObjectGetPropertyData(SystemObject, &address, 0, null, ref size, &device) == 0 && device != 0 ? device : null;
    }

    private static float? Get(uint device, uint selector, uint element)
    {
        var address = new AudioObjectPropertyAddress(selector, ScopeOutput) { Element = element };
        float value = 0;
        var size = (uint)sizeof(float);
        return AudioObjectGetPropertyData(device, &address, 0, null, ref size, &value) == 0 ? value : null;
    }

    private static bool Put(uint device, uint selector, uint element, float value)
    {
        var address = new AudioObjectPropertyAddress(selector, ScopeOutput) { Element = element };
        return AudioObjectSetPropertyData(device, &address, 0, null, sizeof(float), &value) == 0;
    }
}
