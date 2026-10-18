using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// An output Lucent has to itself (Core Audio's hog mode), running at the sample rate of the music
/// playing: the Mac's version of exclusive mode. Letting go puts its sample rate back as it was and
/// gives it back to every app.
/// </summary>
/// <remarks>
/// While Lucent has it, no other app can play through it, and nothing else is mixed in. The music goes
/// out as 32-bit float, which carries 16- and 24-bit samples exactly, so at full volume with the
/// equalizer off the DAC receives what's in the file. macOS lets go of hog mode by itself if Lucent quits
/// unexpectedly.
/// </remarks>
public sealed unsafe class ExclusiveDevice : IDisposable
{
    // How long a new sample rate gets to take hold before it counts as refused.
    private static readonly TimeSpan RateChangeWait = TimeSpan.FromSeconds(1);

    private readonly double _previousRate;
    private bool _released;

    private ExclusiveDevice(OutputDevice device, double previousRate)
    {
        Device = device;
        _previousRate = previousRate;
    }

    public OutputDevice Device { get; }

    /// <summary>The rate the output is running at now, in samples per second (0 if it won't say).</summary>
    public double SampleRate => ReadRate(Device.ObjectId);

    /// <summary>Takes the output for Lucent alone. Throws if another app has it, or it can't be taken.</summary>
    public static ExclusiveDevice Take(OutputDevice device)
    {
        var owner = ReadOwner(device.ObjectId);
        if (owner != -1 && owner != Environment.ProcessId)
        {
            throw new IOException($"Another app (process {owner}) has {device.Name} to itself.");
        }

        if (owner == -1)
        {
            // Setting hog mode toggles it: taken when no one has it, let go when this app has it.
            var address = new AudioObjectPropertyAddress(HogModeProperty);
            var pid = Environment.ProcessId;
            Check(AudioObjectSetPropertyData(device.ObjectId, &address, 0, null, sizeof(int), &pid), $"take {device.Name} for Lucent alone");
            if (ReadOwner(device.ObjectId) != Environment.ProcessId)
            {
                throw new IOException($"{device.Name} wouldn't let Lucent have it to itself.");
            }
        }

        return new ExclusiveDevice(device, ReadRate(device.ObjectId));
    }

    /// <summary>
    /// Switches the output to <paramref name="sampleRate"/>, waiting for it to take hold. False when the
    /// output doesn't run at that rate, or wouldn't change to it.
    /// </summary>
    public bool TrySetSampleRate(int sampleRate)
    {
        if (Math.Abs(SampleRate - sampleRate) < 0.5)
        {
            return true;
        }

        if (!AvailableRates(Device.ObjectId).Any(range => sampleRate >= range.Minimum - 0.5 && sampleRate <= range.Maximum + 0.5))
        {
            return false;
        }

        if (!WriteRate(Device.ObjectId, sampleRate))
        {
            return false;
        }

        // The change happens a moment later, once the output has locked on to the new rate.
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < RateChangeWait)
        {
            if (Math.Abs(SampleRate - sampleRate) < 0.5)
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return false;
    }

    /// <summary>Puts the sample rate back as it was and gives the output back to every app.</summary>
    public void Dispose()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        try
        {
            if (_previousRate > 0 && Math.Abs(SampleRate - _previousRate) >= 0.5)
            {
                WriteRate(Device.ObjectId, _previousRate);
            }

            if (ReadOwner(Device.ObjectId) == Environment.ProcessId)
            {
                var address = new AudioObjectPropertyAddress(HogModeProperty);
                var pid = -1;
                AudioObjectSetPropertyData(Device.ObjectId, &address, 0, null, sizeof(int), &pid);
            }
        }
        catch (Exception ex)
        {
            // Unplugged, most likely; macOS has let go of it already.
            ErrorLog.Write("Exclusive output", ex);
        }
    }

    /// <summary>The process that has the output to itself, or -1 when no one has.</summary>
    internal static int ReadOwner(uint device)
    {
        var address = new AudioObjectPropertyAddress(HogModeProperty);
        var owner = -1;
        var size = (uint)sizeof(int);
        return AudioObjectGetPropertyData(device, &address, 0, null, ref size, &owner) == 0 ? owner : -1;
    }

    /// <summary>The rate the output is running at, in samples per second (0 if it won't say).</summary>
    internal static double ReadRate(uint device)
    {
        var address = new AudioObjectPropertyAddress(NominalSampleRateProperty);
        double rate = 0;
        var size = (uint)sizeof(double);
        return AudioObjectGetPropertyData(device, &address, 0, null, ref size, &rate) == 0 ? rate : 0;
    }

    /// <summary>The rates the output can run at.</summary>
    internal static IReadOnlyList<AudioValueRange> AvailableRates(uint device)
    {
        var address = new AudioObjectPropertyAddress(AvailableSampleRatesProperty);
        if (AudioObjectGetPropertyDataSize(device, &address, 0, null, out var size) != 0 || size < sizeof(AudioValueRange))
        {
            return Array.Empty<AudioValueRange>();
        }

        var ranges = new AudioValueRange[size / sizeof(AudioValueRange)];
        fixed (AudioValueRange* data = ranges)
        {
            if (AudioObjectGetPropertyData(device, &address, 0, null, ref size, data) != 0)
            {
                return Array.Empty<AudioValueRange>();
            }
        }

        return ranges.Take((int)(size / sizeof(AudioValueRange))).ToList();
    }

    private static bool WriteRate(uint device, double rate)
    {
        var address = new AudioObjectPropertyAddress(NominalSampleRateProperty);
        return AudioObjectSetPropertyData(device, &address, 0, null, sizeof(double), &rate) == 0;
    }
}
