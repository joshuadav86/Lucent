using System.IO;
using NAudio.Wave;
using Lucent.Audio;
using Lucent.Editor;
using Lucent.Editor.Effects;

namespace Lucent.Export;

/// <summary>One file to convert and where its result goes.</summary>
public sealed record ConvertJob(string Source, string Destination, bool ReplaceExisting);

/// <summary>How one conversion went: an error message if it failed, and any note about what changed.</summary>
public sealed record ConvertResult(ConvertJob Job, string? Error, string? Note)
{
    public bool Succeeded => Error is null;
}

/// <summary>Converts music files to MP3, FLAC or WAV, several at a time.</summary>
public static class FileConverter
{
    /// <summary>A few files at once: encoding is CPU-heavy, but each file also waits on the disk.</summary>
    public static int Parallelism => Math.Clamp(Environment.ProcessorCount - 1, 1, 4);

    /// <summary>
    /// Where a file's converted copy goes: next to it, or in <see cref="ExportOptions.OutputFolder"/>,
    /// with the new extension. Never the source file itself.
    /// </summary>
    public static string DestinationFor(string source, ExportOptions options)
    {
        var folder = options.OutputFolder ?? Path.GetDirectoryName(source) ?? ".";
        var name = Path.GetFileNameWithoutExtension(source);
        var destination = Path.Combine(folder, name + options.Extension);
        if (string.Equals(Path.GetFullPath(destination), Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
        {
            // Same format in the same folder: the converter never overwrites the file it reads.
            destination = Path.Combine(folder, $"{name} (converted){options.Extension}");
        }

        return destination;
    }

    /// <summary>A free name like "Song (2).mp3" for when the user wants to keep an existing file.</summary>
    public static string NumberedName(string path, ISet<string> taken)
    {
        var folder = Path.GetDirectoryName(path) ?? ".";
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var n = 2; ; n++)
        {
            var candidate = Path.Combine(folder, $"{name} ({n}){extension}");
            if (!File.Exists(candidate) && !taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>
    /// Converts every job, reporting overall progress (0 to 1) and each finished file. Cancelling stops
    /// the files in progress and skips the rest; finished files are kept.
    /// </summary>
    /// <param name="steps">Processing to do to every song before it's written (normalizing, say); none for a plain conversion.</param>
    public static async Task<IReadOnlyList<ConvertResult>> ConvertAsync(IReadOnlyList<ConvertJob> jobs, ExportOptions options,
        IProgress<double>? progress, CancellationToken cancellation, IReadOnlyList<ProcessingStep>? steps = null)
    {
        steps ??= Array.Empty<ProcessingStep>();
        var fractions = new double[jobs.Count];
        var results = new ConvertResult?[jobs.Count];
        var gate = new object();

        void Report(int index, double fraction)
        {
            lock (gate)
            {
                fractions[index] = fraction;
                progress?.Report(fractions.Sum() / Math.Max(1, jobs.Count));
            }
        }

        // Processing holds each whole song in memory, so fewer at once.
        var parallel = new ParallelOptions
        {
            MaxDegreeOfParallelism = steps.Count > 0 ? Math.Min(2, Parallelism) : Parallelism,
            CancellationToken = cancellation,
        };
        try
        {
            await Parallel.ForEachAsync(Enumerable.Range(0, jobs.Count), parallel, (index, token) =>
            {
                results[index] = ConvertOne(jobs[index], options, steps, fraction => Report(index, fraction), token);
                Report(index, 1);
                return ValueTask.CompletedTask;
            });
        }
        catch (OperationCanceledException)
        {
            // Reported below as "cancelled" for every file that didn't finish.
        }

        return jobs.Select((job, i) => results[i] ?? new ConvertResult(job, "Cancelled", null)).ToList();
    }

    private static ConvertResult ConvertOne(ConvertJob job, ExportOptions options, IReadOnlyList<ProcessingStep> steps,
        Action<double> progress, CancellationToken cancellation)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Destination)!);
            if (steps.Count > 0)
            {
                var (audio, notes) = Process(job.Source, steps, fraction => progress(fraction * 0.6), cancellation);
                var written = ExportPipeline.Write(new AudioBufferSampleProvider(audio), audio.Frames, job.Destination, job.Source,
                    audioChanged: true, options, job.ReplaceExisting, fraction => progress(0.6 + fraction * 0.4), cancellation);
                if (written is not null)
                {
                    notes.Add(written);
                }

                return new ConvertResult(job, null, notes.Count > 0 ? string.Join(" ", notes.Distinct()) : null);
            }

            using var reader = AudioReaderFactory.Open(job.Source);
            var frames = reader.WaveFormat.BlockAlign > 0 ? reader.Length / reader.WaveFormat.BlockAlign : 0;
            var note = ExportPipeline.Write(reader.ToSampleProvider(), frames, job.Destination, job.Source, audioChanged: false,
                options, job.ReplaceExisting, progress, cancellation);
            return new ConvertResult(job, null, note);
        }
        catch (OperationCanceledException)
        {
            return new ConvertResult(job, "Cancelled", null);
        }
        catch (Exception ex)
        {
            return new ConvertResult(job, ex is Editor.AudioFileException ? ex.Message : FriendlyError(ex), null);
        }
    }

    /// <summary>Decodes a song and runs the steps over it, in order. Returns the result and anything the steps had to say.</summary>
    public static (AudioBuffer Audio, List<string> Notes) Process(string path, IReadOnlyList<ProcessingStep> steps, Action<double>? progress,
        CancellationToken cancellation)
    {
        var (audio, _, _) = AudioFileLoader.Load(path, new Reporter(f => progress?.Invoke(f * 0.3)), cancellation);
        var notes = new List<string>();
        for (var i = 0; i < steps.Count; i++)
        {
            var (effect, range) = steps[i].ToEffect(audio.Frames, audio.SampleRate);
            if (range.IsEmpty)
            {
                range = new FrameRange(0, audio.Frames);
            }

            var step = i;
            var context = new EffectContext(new Reporter(f => progress?.Invoke(0.3 + 0.7 * (step + f) / steps.Count)), cancellation);
            var result = effect.Process(audio.Slice(range.Start, range.Length), context);
            audio = audio.Splice(range.Start, range.Length, result);
            if (context.Note is { } note && effect is not ClickRemovalEffect)
            {
                notes.Add(note);
            }
        }

        return (audio, notes);
    }

    /// <summary>Plain-English wording for the usual reasons a conversion fails.</summary>
    public static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Lucent isn't allowed to write to that folder.",
        IOException io when SafeFile.IsFileInUse(io) => "The file is open in another program, or playing in Lucent.",
        IOException io when (io.HResult & 0xFFFF) == 112 => "The disk is full.", // ERROR_DISK_FULL
        IOException => $"The file couldn't be written ({ex.Message}).",
        _ => $"The file couldn't be read or converted ({ex.Message}).",
    };

    /// <summary>Passes progress straight on, on the same thread (Progress&lt;T&gt; would post it elsewhere, out of order).</summary>
    private sealed class Reporter : IProgress<double>
    {
        private readonly Action<double> _report;

        public Reporter(Action<double> report) => _report = report;

        public void Report(double value) => _report(value);
    }
}
