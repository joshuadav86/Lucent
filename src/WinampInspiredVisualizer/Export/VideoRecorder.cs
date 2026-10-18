using System.Diagnostics;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// Records a song played through a visual as an MP4 video. The visual is drawn off screen, frame by
/// frame at a fixed step (1/30 or 1/60 of a second), each frame reacting to the sound from the
/// file at that moment, so nothing is ever dropped however long a frame takes to draw or encode:
/// a recording may run faster or slower than real time. The song itself becomes the soundtrack.
/// The visuals window is left alone: recording uses a graphics device of its own.
/// </summary>
public static class VideoRecorder
{
    /// <summary>Records on a thread of its own. Cancelling stops it and removes the unfinished file.</summary>
    public static Task<VideoRecordingResult> RecordAsync(VideoRecordingOptions options, IProgress<VideoRecordingProgress>? progress,
        CancellationToken cancellation)
    {
        var done = new TaskCompletionSource<VideoRecordingResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                done.SetResult(Record(options, progress, cancellation));
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

        // Media Foundation's encoders are COM objects that expect a multithreaded apartment.
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        return done.Task;
    }

    private static VideoRecordingResult Record(VideoRecordingOptions options, IProgress<VideoRecordingProgress>? progress,
        CancellationToken cancellation)
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
            return RecordOnce(options, beats, encoder, allowHardware: true, progress, clock, cancellation);
        }
        catch (SharpGen.Runtime.SharpGenException ex) when (encoder.Hardware)
        {
            // Graphics card encoders occasionally fail partway; Windows' own one is slower but dependable.
            ErrorLog.Write("Video recording (graphics card encoder)", ex);
            progress?.Report(new VideoRecordingProgress("The graphics card's encoder stopped, so starting again with Windows' own…", 0, 0));
            return RecordOnce(options, beats, encoder, allowHardware: false, progress, clock, cancellation);
        }
    }

    private static VideoRecordingResult RecordOnce(VideoRecordingOptions options, BeatMap? beats, EncoderChoice encoder,
        bool allowHardware, IProgress<VideoRecordingProgress>? progress, Stopwatch clock, CancellationToken cancellation)
    {
        var fps = options.FrameRate;
        var width = options.Width;
        var height = options.Height;
        var temporary = SafeFile.TemporaryPathFor(options.OutputPath);
        var committed = false;
        try
        {
            using var audio = new RecordingAudio(options.TrackPath);
            var rate = audio.SampleRate;
            // Which frames to draw, and how much sound goes with each (shared with the Mac's recorder).
            var timeline = new VideoTimeline(options, rate, audio.Duration);
            var samplesPerFrame = timeline.SamplesPerFrame;
            var frames = timeline.Frames;
            var warmUpFrames = timeline.WarmUpFrames;
            audio.Skip(timeline.SkippedSamples, cancellation);

            var analyzer = new AudioAnalyzer();
            using var renderer = Renderer.CreateOffscreen(analyzer, options.Presets);
            var start = Math.Clamp(options.PresetIndex, 0, Math.Max(0, options.Presets.Count - 1));
            renderer.PresetIndex = start;
            renderer.AutoCycle = options.Auto;
            renderer.AutoCycleChoices = options.AutoChoices;
            renderer.AutoCycleSeconds = options.AutoCycleSeconds;
            renderer.TransitionSeconds = options.TransitionSeconds;
            renderer.MorphTransitionsOnly = options.MorphTransitionsOnly;
            renderer.Quality = options.Detail;
            renderer.BloomStrength = options.BloomStrength;
            renderer.TargetPixels = Math.Max(options.TargetPixels, width * height);
            renderer.SongBeats = beats;

            var wanted = options.Auto
                ? (options.AutoChoices ?? Enumerable.Range(0, options.Presets.Count).ToList()).Append(start).ToList()
                : new List<int> { start };
            progress?.Report(new VideoRecordingProgress("Getting the visuals ready…", 0, 0));
            renderer.OpenOffscreen(width, height, wanted, (compiled, total) =>
                progress?.Report(new VideoRecordingProgress("Getting the visuals ready…", (double)compiled / total, 0)), cancellation);

            using var converter = new VideoFrameConverter(renderer.Device, renderer.Context, width, height);
            using var writer = Mp4Writer.Create(temporary, width, height, fps, options.VideoBitrate, rate, options.AudioBitrate, allowHardware);
            SafeFile.Hide(temporary);
            encoder.Hardware = writer.HardwareAllowed;

            var picture = new byte[converter.FrameBytes];
            var pcm = Array.Empty<byte>();
            var deltaTime = timeline.DeltaTime;
            long written = 0;
            var recordingStarted = clock.Elapsed;
            var lastReport = TimeSpan.Zero;

            void WriteOldestPicture()
            {
                converter.ReadOldest(picture);
                writer.WriteVideo(picture, converter.FrameBytes, written++);
            }

            for (var frame = -warmUpFrames; frame < frames; frame++)
            {
                cancellation.ThrowIfCancellationRequested();

                // Each frame hears the sound up to its own moment, then the sound that plays while it's shown goes in the soundtrack.
                analyzer.Update(deltaTime, audio.Latest, rate);
                var image = renderer.DrawOffscreen(timeline.Clock(frame), deltaTime, timeline.SongPosition(frame));
                var bytes = audio.Read(samplesPerFrame, ref pcm);
                if (frame < 0)
                {
                    continue;
                }

                writer.WriteAudio(pcm, bytes, (long)frame * samplesPerFrame);
                if (converter.Pending == VideoFrameConverter.Depth)
                {
                    WriteOldestPicture();
                }

                converter.Submit(image);

                var now = clock.Elapsed;
                if (progress is not null && now - lastReport > TimeSpan.FromMilliseconds(200))
                {
                    lastReport = now;
                    progress.Report(timeline.Progress(frame, (now - recordingStarted).TotalSeconds));
                }
            }

            while (converter.Pending > 0)
            {
                WriteOldestPicture();
            }

            progress?.Report(new VideoRecordingProgress("Finishing the file…", 1, 0));
            writer.Finish();
            var note = writer.HardwareEncoder
                ? $"Encoded by the graphics card{(writer.EncoderName is { Length: > 0 } name ? $" ({name})" : string.Empty)}."
                : "Encoded by Windows' own H.264 encoder.";
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

    /// <summary>Whether an attempt let Windows use the graphics card's encoder, so a failure can be put down to it.</summary>
    private sealed class EncoderChoice
    {
        public bool Hardware { get; set; }
    }
}
