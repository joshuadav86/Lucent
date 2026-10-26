using NAudio.CoreAudioApi;

namespace WinampInspiredVisualizer.Audio;

/// <summary>A sound card, headphones, DAC or other output Windows knows about.</summary>
public sealed record OutputDevice(string Id, string Name);

/// <summary>The outputs music can be played through.</summary>
public static class OutputDevices
{
    /// <summary>Every output that's plugged in and switched on, by name.</summary>
    public static IReadOnlyList<OutputDevice> List()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .Select(device =>
                {
                    using (device)
                    {
                        return new OutputDevice(device.ID, device.FriendlyName);
                    }
                })
                .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            // No audio service, or no outputs at all: the default output is still offered.
            ErrorLog.Write("Audio outputs", ex);
            return Array.Empty<OutputDevice>();
        }
    }

    /// <summary>
    /// Opens the output with this id, or Windows' default output when <paramref name="id"/> is null or
    /// that output has gone (unplugged, say). Null when there's no output at all.
    /// </summary>
    public static MMDevice? Open(string? id)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (id is not null)
        {
            try
            {
                var device = enumerator.GetDevice(id);
                if (device.State == DeviceState.Active)
                {
                    return device;
                }

                device.Dispose();
            }
            catch (Exception)
            {
                // Gone since it was chosen: fall back to the default below.
            }
        }

        try
        {
            return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
