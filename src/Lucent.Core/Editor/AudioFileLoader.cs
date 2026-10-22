using System.IO;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Editor;

/// <summary>What the editor knows about the file it opened, for saving it back the same way.</summary>
public sealed record SourceFileInfo(string Path, int SampleRate, int Channels, int BitsPerSample, bool IsFloat, int BitrateKbps)
{
    public string Extension => System.IO.Path.GetExtension(Path).ToLowerInvariant();
}

/// <summary>A problem with a music file, with a message that can be shown to the user as it is.</summary>
public sealed class AudioFileException : Exception
{
    public AudioFileException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>Decodes a whole music file into memory for editing.</summary>
public static class AudioFileLoader
{
    /// <summary>Longer files would need gigabytes of memory to edit.</summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromHours(1);

    /// <summary>
    /// Decodes every sample of the file, with any decoder Lucent plays through. Files with more than two
    /// channels are mixed down to stereo. Throws <see cref="AudioFileException"/> with a friendly message on failure.
    /// </summary>
    public static (AudioBuffer Audio, SourceFileInfo Info, bool MixedDown) Load(string path, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        WaveStream reader;
        try
        {
            reader = AudioReaderFactory.Open(path);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AudioFileException("The file couldn't be opened. It may be damaged, or in a format Windows can't decode.", ex);
        }

        using (reader)
        {
            var samples = reader.ToSampleProvider();
            var rate = samples.WaveFormat.SampleRate;
            var sourceChannels = samples.WaveFormat.Channels;
            var expected = reader.WaveFormat.BlockAlign > 0 ? reader.Length / reader.WaveFormat.BlockAlign : 0;
            if (reader.TotalTime > MaxDuration || expected > MaxDuration.TotalSeconds * rate)
            {
                throw new AudioFileException("This file is longer than an hour, which is more than the editor can hold in memory.");
            }

            var channels = Math.Min(2, sourceChannels);
            var capacity = (int)Math.Clamp(expected + rate, rate, MaxDuration.TotalSeconds * rate + rate);
            var output = Enumerable.Range(0, channels).Select(_ => new float[capacity]).ToArray();
            var chunk = new float[16384 * sourceChannels];
            var frames = 0;

            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var read = samples.Read(chunk, 0, chunk.Length) / sourceChannels;
                if (read == 0)
                {
                    break;
                }

                if (frames + read > output[0].Length)
                {
                    // The length in the file's header was an underestimate.
                    var grown = (int)Math.Min(Array.MaxLength, Math.Max(frames + read, output[0].Length * 3L / 2));
                    if (grown > MaxDuration.TotalSeconds * rate + rate)
                    {
                        throw new AudioFileException("This file is longer than an hour, which is more than the editor can hold in memory.");
                    }

                    for (var c = 0; c < channels; c++)
                    {
                        Array.Resize(ref output[c], grown);
                    }
                }

                CopyFrames(chunk, read, sourceChannels, output, frames);
                frames += read;
                if (expected > 0)
                {
                    progress?.Report(Math.Min(1, (double)frames / expected));
                }
            }

            for (var c = 0; c < channels; c++)
            {
                Array.Resize(ref output[c], frames);
            }

            return (new AudioBuffer(output, rate), ReadInfo(path, rate, sourceChannels), sourceChannels > 2);
        }
    }

    /// <summary>De-interleaves samples, mixing extra channels (centre, surrounds) into left and right.</summary>
    internal static void CopyFrames(float[] chunk, int frames, int sourceChannels, float[][] output, int at)
    {
        if (sourceChannels <= 2)
        {
            for (var i = 0; i < frames; i++)
            {
                for (var c = 0; c < sourceChannels; c++)
                {
                    output[c][at + i] = chunk[i * sourceChannels + c];
                }
            }

            return;
        }

        // A simple downmix: front left/right as they are, everything else shared between both sides
        // at -3 dB, then scaled so the sum can't clip.
        var extra = sourceChannels - 2;
        var scale = 1f / (1f + extra * 0.7071f);
        for (var i = 0; i < frames; i++)
        {
            var frame = i * sourceChannels;
            var shared = 0f;
            for (var c = 2; c < sourceChannels; c++)
            {
                shared += chunk[frame + c] * 0.7071f;
            }

            output[0][at + i] = (chunk[frame] + shared) * scale;
            output[1][at + i] = (chunk[frame + 1] + shared) * scale;
        }
    }

    private static SourceFileInfo ReadInfo(string path, int sampleRate, int channels)
    {
        var bits = 16;
        var isFloat = false;
        var bitrate = 0;
        try
        {
            // TagLib can't tell 32-bit float from 32-bit integer WAV (and doesn't read Wave64 or CAF), so the PCM
            // reader is asked for uncompressed files.
            using var pcm = PcmFileReader.TryOpen(path);
            if (pcm is not null)
            {
                bits = Math.Min(32, pcm.SourceBits);
                isFloat = pcm.IsSourceFloat;
            }
            else
            {
                using var file = TagLib.File.Create(path);
                if (file.Properties is { } properties)
                {
                    bits = properties.BitsPerSample > 0 ? properties.BitsPerSample : 16;
                    bitrate = properties.AudioBitrate;
                }
            }
        }
        catch (Exception)
        {
            // Unknown details just mean saving with sensible defaults.
        }

        return new SourceFileInfo(path, sampleRate, channels, bits, isFloat, bitrate);
    }
}
