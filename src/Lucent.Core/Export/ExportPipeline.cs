using System.IO;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// The steps every save and conversion goes through: encode to a hidden temporary file, copy the
/// tags and artwork, check the new file plays, then move it into place.
/// </summary>
public static class ExportPipeline
{
    /// <summary>Writes <paramref name="source"/> to <paramref name="finalPath"/>. Returns a note for the user, if any.</summary>
    /// <param name="tagSource">The file whose tags and artwork the new file gets, or null for none.</param>
    /// <param name="audioChanged">True if the audio was edited (drops ReplayGain values that no longer apply).</param>
    /// <param name="replaceExisting">True to replace a file already at <paramref name="finalPath"/> (the user agreed).</param>
    public static string? Write(ISampleProvider source, long expectedFrames, string finalPath, string? tagSource,
        bool audioChanged, ExportOptions options, bool replaceExisting, Action<double>? progress, CancellationToken cancellation)
    {
        var prepared = Prepare(source, expectedFrames, finalPath, tagSource, audioChanged, options, progress, cancellation);
        prepared.Commit(replaceExisting);
        return prepared.Note;
    }

    /// <summary>
    /// Does all the slow work (encoding, tags, checking) into a hidden temporary file, leaving only the
    /// instant final step: <see cref="PreparedFile.Commit"/> or <see cref="PreparedFile.Discard"/>.
    /// </summary>
    public static PreparedFile Prepare(ISampleProvider source, long expectedFrames, string finalPath, string? tagSource,
        bool audioChanged, ExportOptions options, Action<double>? progress, CancellationToken cancellation)
    {
        var temporary = SafeFile.TemporaryPathFor(finalPath);
        try
        {
            var sourceRate = source.WaveFormat.SampleRate;
            if (options.Mono && source.WaveFormat.Channels > 1)
            {
                source = new MonoMixdown(source);
            }

            var note = AudioEncoder.Encode(source, temporary, options, expectedFrames, fraction => progress?.Invoke(fraction * 0.95),
                cancellation);
            SafeFile.Hide(temporary);
            cancellation.ThrowIfCancellationRequested();

            if (tagSource is not null && !TagCopier.Copy(tagSource, temporary, audioChanged))
            {
                note = Join(note, "The tags couldn't be copied, so the new file has none.");
            }

            var expected = expectedFrames > 0 ? TimeSpan.FromSeconds((double)expectedFrames / sourceRate) : (TimeSpan?)null;
            Verify(temporary, expected);
            cancellation.ThrowIfCancellationRequested();
            progress?.Invoke(1);
            return new PreparedFile(temporary, finalPath, note);
        }
        catch
        {
            SafeFile.Discard(temporary);
            throw;
        }
    }

    /// <summary>Opens the new file with the same decoders Lucent plays with, and checks its length looks right.</summary>
    private static void Verify(string path, TimeSpan? expected)
    {
        try
        {
            using var reader = AudioReaderFactory.Open(path);
            var samples = reader.ToSampleProvider();
            var buffer = new float[samples.WaveFormat.SampleRate * samples.WaveFormat.Channels];
            var read = samples.Read(buffer, 0, buffer.Length);
            if (expected is { } length && length > TimeSpan.FromSeconds(0.1))
            {
                var difference = Math.Abs((reader.TotalTime - length).TotalSeconds);
                if (read == 0 || difference > Math.Max(1.0, length.TotalSeconds * 0.02))
                {
                    throw new InvalidDataException($"The new file is {reader.TotalTime} long instead of {length}.");
                }
            }
        }
        catch (Exception ex)
        {
            throw new AudioFileException("The new file didn't play back correctly, so it wasn't kept. Your original file is untouched.", ex);
        }
    }

    private static string? Join(string? first, string second) => first is null ? second : $"{first} {second}";
}

/// <summary>Mixes every channel into one, at the average of their levels, so nothing clips that didn't before.</summary>
public sealed class MonoMixdown : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _buffer = Array.Empty<float>();

    public MonoMixdown(ISampleProvider source)
    {
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _source.WaveFormat.Channels;
        if (_buffer.Length < count * channels)
        {
            _buffer = new float[count * channels];
        }

        var frames = _source.Read(_buffer, 0, count * channels) / channels;
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                sum += _buffer[i * channels + c];
            }

            buffer[offset + i] = sum / channels;
        }

        return frames;
    }
}

/// <summary>A finished, checked file waiting in a hidden temporary file to be moved into place.</summary>
public sealed class PreparedFile
{
    internal PreparedFile(string temporaryPath, string finalPath, string? note)
    {
        TemporaryPath = temporaryPath;
        FinalPath = finalPath;
        Note = note;
    }

    public string TemporaryPath { get; }

    public string FinalPath { get; }

    /// <summary>Anything the user should know about the new file.</summary>
    public string? Note { get; }

    /// <summary>Moves the file into place (see <see cref="SafeFile.Commit"/>). On failure the temporary file is deleted.</summary>
    public void Commit(bool replaceExisting)
    {
        try
        {
            SafeFile.Commit(TemporaryPath, FinalPath, replaceExisting);
        }
        catch
        {
            Discard();
            throw;
        }
    }

    public void Discard() => SafeFile.Discard(TemporaryPath);
}
