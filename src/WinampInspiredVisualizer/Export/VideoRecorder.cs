using System.Diagnostics;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer.Export;

/// <summary>How much detail the encoder keeps, and so how big the file is.</summary>
public enum VideoQuality
{
    Good,
    High,
    Best,
}

/// <summary>What to record: which song (and which part of it), drawn how, into which file.</summary>
public sealed record VideoRecordingOptions
{
    public required string TrackPath { get; init; }

    /// <summary>The MP4 file to make. It's replaced if it exists (the dialog asks first).</summary>
    public required string OutputPath { get; init; }

    public int Width { get; init; } = 1920;

    public int Height { get; init; } = 1080;

    /// <summary>30 or 60.</summary>
    public int FrameRate { get; init; } = 60;

    public VideoQuality Quality { get; init; } = VideoQuality.High;

    /// <summary>Where in the song the video starts.</summary>
    public TimeSpan From { get; init; }

    /// <summary>Where it ends, or null for the end of the song.</summary>
    public TimeSpan? To { get; init; }

    /// <summary>Every visual, as the visuals window lists them, so <see cref="PresetIndex"/> means the same.</summary>
    public required IReadOnlyList<PresetSource> Presets { get; init; }

    /// <summary>The visual to record (with <see cref="Auto"/>, the one to start on).</summary>
    public int PresetIndex { get; init; }

    /// <summary>Change visual every so often, on the beat, as Auto does on screen.</summary>
    public bool Auto { get; init; }

    /// <summary>The visuals Auto picks from (by index), or null for all of them.</summary>
    public IReadOnlyCollection<int>? AutoChoices { get; init; }

    public float AutoCycleSeconds { get; init; } = 25f;

    public float TransitionSeconds { get; init; } = 3f;

    public bool MorphTransitionsOnly { get; init; }

    /// <summary>Detail level for visuals, as on screen: 0 low, 1 normal, 2 high, 3 ultra.</summary>
    public float Detail { get; init; } = 1f;

    /// <summary>Glow, as on screen.</summary>
    public float BloomStrength { get; init; } = 1f;

    /// <summary>
    /// The number of pixels the visuals window draws (0 for its own size). Above the video's size,
    /// each frame is drawn bigger and shrunk, as on screen; it's never drawn smaller than the video.
    /// </summary>
    public int TargetPixels { get; init; }

    /// <summary>The song's bars, if they've been worked out already; with Auto they're worked out otherwise.</summary>
    public BeatMap? Beats { get; init; }

    /// <summary>The video's bitrate, in bits a second: more for bigger, faster pictures and for higher quality.</summary>
    public int VideoBitrate
    {
        get
        {
            var bitsPerPixel = Quality switch
            {
                VideoQuality.Good => 0.12,
                VideoQuality.High => 0.2,
                _ => 0.3,
            };
            var bitrate = (double)Width * Height * FrameRate * bitsPerPixel;
            return (int)Math.Clamp(bitrate, 4_000_000, 160_000_000);
        }
    }

    /// <summary>The soundtrack's bitrate (AAC), in bits a second.</summary>
    public int AudioBitrate => 192_000;
}

/// <summary>How a recording is getting on.</summary>
/// <param name="Stage">What's happening, in words.</param>
/// <param name="Fraction">How far through, 0 to 1 (for the stage that's happening).</param>
/// <param name="Speed">How many seconds of video are made each second (above 1 is faster than real time); 0 until known.</param>
public sealed record VideoRecordingProgress(string Stage, double Fraction, double Speed);

/// <summary>A finished recording.</summary>
/// <param name="Note">How it was encoded, for the user.</param>
public sealed record VideoRecordingResult(string Path, TimeSpan Length, TimeSpan TimeTaken, string Note);

/// <summary>
/// Records a song played through a visual as an MP4 video. The visual is drawn off screen, frame by
/// frame at a fixed step (1/30 or 1/60 of a second), each frame reacting to the sound from the
/// file at that moment, so nothing is ever dropped however long a frame takes to draw or encode:
/// a recording may run faster or slower than real time. The song itself becomes the soundtrack.
/// The visuals window is left alone: recording uses a graphics device of its own.
/// </summary>
public static class VideoRecorder
{
    // Drawn but not kept before a video that starts partway through a song, so the visual is already in full flow.
    private const double WarmUpSeconds = 2.0;

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
            // 44.1 and 48 kHz both divide by 30 and 60, so every frame has a whole number of samples.
            var samplesPerFrame = rate / fps;
            var songSeconds = audio.Duration.TotalSeconds;
            var from = Math.Clamp(options.From.TotalSeconds, 0, songSeconds);
            var to = Math.Clamp(options.To?.TotalSeconds ?? songSeconds, from, songSeconds);
            var frames = (long)Math.Ceiling((to - from) * fps - 1e-6);
            if (frames < fps / 2)
            {
                throw new InvalidOperationException("That's too short to record: choose at least half a second.");
            }

            // Start on a frame boundary, and warm up from a couple of seconds before if there's song there.
            var firstFrame = (long)Math.Round(from * fps);
            var warmUpFrames = (int)Math.Min((long)(WarmUpSeconds * fps), firstFrame);
            audio.Skip((firstFrame - warmUpFrames) * samplesPerFrame, cancellation);

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
            var deltaTime = 1f / fps;
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
                var image = renderer.DrawOffscreen((double)(frame + warmUpFrames) / fps, deltaTime, (double)(firstFrame + frame) / fps);
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
                    var seconds = (now - recordingStarted).TotalSeconds;
                    var speed = seconds > 0.5 ? frame / (double)fps / seconds : 0;
                    progress.Report(new VideoRecordingProgress(
                        $"Recording… {TimeFormat.Format(TimeSpan.FromSeconds(frame / (double)fps))} of {TimeFormat.Format(TimeSpan.FromSeconds(frames / (double)fps))}",
                        (double)frame / frames, speed));
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
            return new VideoRecordingResult(options.OutputPath, TimeSpan.FromSeconds(frames / (double)fps), clock.Elapsed, note);
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
