using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>A sound card, headphones, DAC or other output macOS knows about.</summary>
/// <param name="Id">The device's UID, which stays the same from one start to the next.</param>
public sealed record OutputDevice(string Id, string Name)
{
    /// <summary>Core Audio's number for the device while it's connected (it can change once it's unplugged).</summary>
    internal uint ObjectId { get; init; }
}

/// <summary>The outputs music can be played through.</summary>
public static unsafe class OutputDevices
{
    /// <summary>Every output that's connected, by name.</summary>
    public static IReadOnlyList<OutputDevice> List()
    {
        try
        {
            return Enumerate();
        }
        catch (Exception ex)
        {
            // No outputs at all, or Core Audio not answering: the default output is still offered.
            ErrorLog.Write("Audio outputs", ex);
            return Array.Empty<OutputDevice>();
        }
    }

    /// <summary>Every output that's connected, by name. Throws if Core Audio can't be asked.</summary>
    public static IReadOnlyList<OutputDevice> Enumerate()
    {
        var address = new AudioObjectPropertyAddress(DevicesProperty);
        Check(AudioObjectGetPropertyDataSize(SystemObject, &address, 0, null, out var size), "list the sound devices");
        var ids = new uint[size / sizeof(uint)];
        if (ids.Length > 0)
        {
            fixed (uint* data = ids)
            {
                Check(AudioObjectGetPropertyData(SystemObject, &address, 0, null, ref size, data), "list the sound devices");
            }
        }

        return ids.Take((int)(size / sizeof(uint)))
            .Where(HasOutput)
            .Select(Describe)
            .OfType<OutputDevice>()
            .OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Finds the output with this id, or macOS's default output when <paramref name="id"/> is null or
    /// that output has gone (unplugged, say). Null when there's no output at all.
    /// </summary>
    public static OutputDevice? Open(string? id)
    {
        if (id is not null)
        {
            var chosen = List().FirstOrDefault(device => device.Id == id);
            if (chosen is not null)
            {
                return chosen;
            }

            // Gone since it was chosen: fall back to the default below.
        }

        try
        {
            var address = new AudioObjectPropertyAddress(DefaultOutputDeviceProperty);
            uint device = 0;
            var size = (uint)sizeof(uint);
            if (AudioObjectGetPropertyData(SystemObject, &address, 0, null, ref size, &device) != 0 || device == 0)
            {
                return null;
            }

            return Describe(device);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Devices with output streams play sound; microphones and other inputs have none.
    private static bool HasOutput(uint device)
    {
        var address = new AudioObjectPropertyAddress(StreamsProperty, ScopeOutput);
        return AudioObjectGetPropertyDataSize(device, &address, 0, null, out var size) == 0 && size > 0;
    }

    // The device's UID and name, or null when it won't say (it's just been unplugged, say).
    private static OutputDevice? Describe(uint device)
    {
        var id = ReadText(device, DeviceUidProperty);
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        return new OutputDevice(id, ReadText(device, NameProperty) is { Length: > 0 } name ? name : id) { ObjectId = device };
    }

    // A text property of a device (a CFString Core Audio makes for us, and we release).
    private static string? ReadText(uint device, uint property)
    {
        var address = new AudioObjectPropertyAddress(property);
        var text = IntPtr.Zero;
        var size = (uint)sizeof(IntPtr);
        if (AudioObjectGetPropertyData(device, &address, 0, null, ref size, &text) != 0 || text == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return ReadString(text);
        }
        finally
        {
            CFRelease(text);
        }
    }
}
