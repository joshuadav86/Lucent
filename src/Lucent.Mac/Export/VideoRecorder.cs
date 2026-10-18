using System.Diagnostics;
using Lucent.Mac.Interop;
using Lucent.Mac.Visuals;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac.Export;

/// <summary>
/// Records a song played through a visual as an MP4 video, as the Windows app's VideoRecorder does. The
/// visual is drawn off screen with Metal, frame by frame at a fixed step (1/30 or 1/60 of a second), each
/// frame reacting to the sound from the file at that moment, so nothing is ever dropped however long a
/// frame takes to draw or encode: a recording may run faster or slower than real time. The song itself
/// becomes the soundtrack. The visuals window is left alone: recording uses an engine of its own.
/// </summary>
public static class VideoRecorder
{
    /// <summary>Records on a thread of its own. Cancelling stops it and removes the unfinished file.</summary>
    public static Task<VideoRecordingResult> RecordAsync(VideoRecordingOptions options, IProgress<VideoRecordingProgress>? progress,
        CancellationToken cancellation) =>
        RecordAsync(options, progress, cancellation, files: null);

    /// <summary>The same, finding the visuals' shaders with <paramref name="files"/> (the self-test's own cache, say).</summary>
    internal static Task<VideoRecordingResult> RecordAsync(VideoRecordingOptions options, IProgress<VideoRecordingProgress>? progress,
        CancellationToken cancellation, MetalShaderFiles? files)
    {
        var done = new TaskCompletionSource<VideoRecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                // For what Metal and AVFoundation hand back while getting ready (each frame has a pool of its own).
                using var pool = AutoreleasePool.Begin();
                done.SetResult(Record(options, files ?? new MetalShaderFiles(), progress, cancellation));
            }
            catch (OperationCanceledException)
            {
                done.SetCanceled(cancellation);
            }
            catch (Exception ex)
            {
                done.SetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "Video recorder",
            // The music and the visuals on screen come first.
            Priority = ThreadPriority.BelowNormal,
        };

        thread.Start();
        return done.Task;
    }

    private static VideoRecordingResult Record(VideoRecordingOptions options, MetalShaderFiles files,
        IProgress<VideoRecordingProgress>? progress, CancellationToken cancellation)
    {
        var clock = Stopwatch.StartNew();
        var beats = options.Beats;
        if (options.Auto && beats is null)
        {
            progress?.Report(new VideoRecordingProgress("Finding the beat…", 0, 0));
            beats = BeatMap.Analyze(options.TrackPath, cancellation);
        }

        var encoder = new EncoderChoice();
        try
        {
            return RecordOnce(options, files, beats, encoder, allowHardware: true, progress, clock, cancellation);
        }
        catch (VideoEncoderException ex) when (encoder.Hardware)
        {
            // The hardware encoder can turn a recording down partway; macOS's own one is slower but dependable.
            ErrorLog.Write("Video recording (hardware encoder)", ex);
            progress?.Report(new VideoRecordingProgress("The Mac's video encoder stopped, so starting again with macOS's own…", 0, 0));
            return RecordOnce(options, files, beats, encoder, allowHardware: false, progress, clock, cancellation);
        }
    }

    private static VideoRecordingResult RecordOnce(VideoRecordingOptions options, MetalShaderFiles files, BeatMap? beats,
        EncoderChoice encoder, bool allowHardware, IProgress<VideoRecordingProgress>? progress, Stopwatch clock,
        CancellationToken cancellation)
    {
        var fps = options.FrameRate;
        var width = options.Width;
        var height = options.Height;
        var temporary = SafeFile.TemporaryPathFor(options.OutputPath);
        var committed = false;
        try
        {
            var device = MetalSupport.TryCreateDevice()
                ?? throw new InvalidOperationException("This Mac can't run Metal, so the visuals can't be drawn.");
            using var audio = new RecordingAudio(options.TrackPath);
            var rate = audio.SampleRate;
            // Which frames to draw, and how much sound goes with each (shared with the Windows recorder).
            var timeline = new VideoTimeline(options, rate, audio.Duration);
            var samplesPerFrame = timeline.SamplesPerFrame;
            var frames = timeline.Frames;
            var warmUpFrames = timeline.WarmUpFrames;
            audio.Skip(timeline.SkippedSamples, cancellation);

            var analyzer = new AudioAnalyzer();
            using var engine = VisualEngine.CreateOffscreen(device, analyzer, files, options.Presets);
            var start = Math.Clamp(options.PresetIndex, 0, Math.Max(0, options.Presets.Count - 1));
            engine.PresetIndex = start;
            engine.AutoCycle = options.Auto;
            engine.AutoCycleChoices = options.AutoChoices;
            engine.AutoCycleSeconds = options.AutoCycleSeconds;
            engine.TransitionSeconds = options.TransitionSeconds;
            engine.MorphTransitionsOnly = options.MorphTransitionsOnly;
            engine.Quality = options.Detail;
            engine.BloomStrength = options.BloomStrength;
            engine.TargetPixels = Math.Max(options.TargetPixels, width * height);
            engine.SongBeats = beats;

            var wanted = options.Auto
                ? (options.AutoChoices ?? Enumerable.Range(0, options.Presets.Count).ToList()).Append(start).ToList()
                : new List<int> { start };
            progress?.Report(new VideoRecordingProgress("Getting the visuals ready…", 0, 0));
            engine.OpenOffscreen(width, height, wanted, (compiled, total) =>
                progress?.Report(new VideoRecordingProgress("Getting the visuals ready…", (double)compiled / total, 0)), cancellation);

            using var reader = new VideoFrameReader(engine.Device, width, height);
            using var writer = Mp4Writer.Create(temporary, width, height, fps, options.VideoBitrate, rate, options.AudioBitrate, allowHardware);
            SafeFile.Hide(temporary);
            encoder.Hardware = writer.HardwareAllowed;

            var pcm = Array.Empty<byte>();
            var deltaTime = timeline.DeltaTime;
            long written = 0;
            var recordingStarted = clock.Elapsed;
            var lastReport = TimeSpan.Zero;

            void WriteOldestPicture() => writer.WriteVideo(reader.ReadOldest, written++);

            for (var frame = -warmUpFrames; frame < frames; frame++)
            {
                cancellation.ThrowIfCancellationRequested();
                // Metal and AVFoundation hand back objects to be freed when the pool drains, every frame.
                using var pool = AutoreleasePool.Begin();

                // Each frame hears the sound up to its own moment, then the sound that plays while it's shown goes in the soundtrack.
                analyzer.Update(deltaTime, audio.Latest, rate);
                var commands = engine.DrawOffscreen(timeline.Clock(frame), deltaTime, timeline.SongPosition(frame));
                var bytes = audio.Read(samplesPerFrame, ref pcm);
                if (frame < 0)
                {
                    commands.Commit();
                    continue;
                }

                writer.WriteAudio(pcm, bytes, (long)frame * samplesPerFrame);
                if (reader.Pending == VideoFrameReader.Depth)
                {
                    WriteOldestPicture();
                }

                reader.Submit(commands, engine.OffscreenOutput);

                var now = clock.Elapsed;
                if (progress is not null && now - lastReport > TimeSpan.FromMilliseconds(200))
                {
                    lastReport = now;
                    progress.Report(timeline.Progress(frame, (now - recordingStarted).TotalSeconds));
                }
            }

            using (AutoreleasePool.Begin())
            {
                while (reader.Pending > 0)
                {
                    WriteOldestPicture();
                }

                progress?.Report(new VideoRecordingProgress("Finishing the file…", 1, 0));
                writer.Finish();
            }

            var note = writer.HardwareEncoder
                ? "Encoded by the Mac's video encoder hardware."
                : "Encoded by macOS's own H.264 encoder.";
            writer.Dispose();

            SafeFile.Commit(temporary, options.OutputPath, replace: true);
            committed = true;
            return new VideoRecordingResult(options.OutputPath, timeline.Length, clock.Elapsed, note);
        }
        finally
        {
            if (!committed)
            {
                SafeFile.Discard(temporary);
            }
        }
    }

    /// <summary>Whether an attempt let only the hardware encoder encode, so a failure can be put down to it.</summary>
    private sealed class EncoderChoice
    {
        public bool Hardware { get; set; }
    }
}
