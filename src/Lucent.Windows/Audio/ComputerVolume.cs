using NAudio.CoreAudioApi;

namespace Lucent.Audio;

/// <summary>
/// The volume of Windows' default output (the one the taskbar's speaker sets), which every app's sound goes through.
/// Windows applies it after the mix that <see cref="SystemAudio"/> listens to, so turning it down doesn't change what
/// the visuals and analyzer hear.
/// </summary>
/// <remarks>The Mac's Audio/ComputerVolume.cs does the same with the default output's Core Audio volume.</remarks>
public static class ComputerVolume
{
    /// <summary>The volume from 0 to 1 and whether it's muted, or null when there's no output to read.</summary>
    public static (double Level, bool Muted)? Read()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            return (volume.MasterVolumeLevelScalar, volume.Mute);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Sets the volume, unmuting when it's turned up from muted. Does nothing when there's no output.</summary>
    public static void Set(double level)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            volume.MasterVolumeLevelScalar = (float)Math.Clamp(level, 0, 1);
            if (volume.Mute && level > 0)
            {
                volume.Mute = false;
            }
        }
        catch (Exception)
        {
            // The output went away between reads: the next read shows whatever's there now.
        }
    }

    /// <summary>
    /// Turns the volume down by <paramref name="decibels"/> (as far as the output goes). False when there's no output
    /// or Windows wouldn't change it.
    /// </summary>
    public static bool TurnDown(double decibels)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = device.AudioEndpointVolume;
            var range = volume.VolumeRange;
            volume.MasterVolumeLevel = (float)Math.Clamp(volume.MasterVolumeLevel - decibels, range.MinDecibels, range.MaxDecibels);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
