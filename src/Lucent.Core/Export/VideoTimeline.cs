using Lucent.Playlist;

namespace Lucent.Export;

/// <summary>
/// When each frame of a recording happens, the same on Windows and the Mac: which part of the song
/// the video covers, how much sound goes with each frame, and the frames drawn first (and not kept)
/// so a video that starts partway through a song finds the visual already in full flow. Frames are
/// numbered from 0 at the start of the video; the warm-up frames come before, numbered below 0.
/// </summary>
public sealed class VideoTimeline
{
    // Drawn but not kept before a video that starts partway through a song, so the visual is already in full flow.
    private const double WarmUpSeconds = 2.0;

    /// <summary>
    /// The frames for <paramref name="options"/>' part of a song <paramref name="songLength"/> long,
    /// with its sound at <paramref name="sampleRate"/> (44100 or 48000). Throws if that's too short to record.
    /// </summary>
    public VideoTimeline(VideoRecordingOptions options, int sampleRate, TimeSpan songLength)
    {
        FrameRate = options.FrameRate;
        SampleRate = sampleRate;
        // 44.1 and 48 kHz both divide by 30 and 60, so every frame has a whole number of samples.
        SamplesPerFrame = sampleRate / FrameRate;
        var songSeconds = songLength.TotalSeconds;
        var from = Math.Clamp(options.From.TotalSeconds, 0, songSeconds);
        var to = Math.Clamp(options.To?.TotalSeconds ?? songSeconds, from, songSeconds);
        Frames = (long)Math.Ceiling((to - from) * FrameRate - 1e-6);
        if (Frames < FrameRate / 2)
        {
            throw new InvalidOperationException("That's too short to record: choose at least half a second.");
        }

        // Start on a frame boundary, and warm up from a couple of seconds before if there's song there.
        FirstFrame = (long)Math.Round(from * FrameRate);
        WarmUpFrames = (int)Math.Min((long)(WarmUpSeconds * FrameRate), FirstFrame);
    }

    /// <summary>30 or 60.</summary>
    public int FrameRate { get; }

    public int SampleRate { get; }

    /// <summary>How many samples (per channel) of sound play while each frame is shown.</summary>
    public int SamplesPerFrame { get; }

    /// <summary>How many frames the video has.</summary>
    public long Frames { get; }

    /// <summary>The video's first frame, counted in frames from the start of the song.</summary>
    public long FirstFrame { get; }

    /// <summary>How many frames are drawn and thrown away before the first one.</summary>
    public int WarmUpFrames { get; }

    /// <summary>The time from one frame to the next, in seconds.</summary>
    public float DeltaTime => 1f / FrameRate;

    /// <summary>The samples (per channel) of the song before the first warm-up frame, which are skipped.</summary>
    public long SkippedSamples => (FirstFrame - WarmUpFrames) * SamplesPerFrame;

    /// <summary>How long the video is.</summary>
    public TimeSpan Length => TimeSpan.FromSeconds(Frames / (double)FrameRate);

    /// <summary>The visuals' clock at a frame: it starts at 0 with the first warm-up frame and only ever moves on.</summary>
    public double Clock(long frame) => (double)(frame + WarmUpFrames) / FrameRate;

    /// <summary>How far into the song a frame is, in seconds.</summary>
    public double SongPosition(long frame) => (double)(FirstFrame + frame) / FrameRate;

    /// <summary>
    /// How the recording is getting on at <paramref name="frame"/>, <paramref name="seconds"/> after
    /// the drawing started: how far through, and how fast against real time.
    /// </summary>
    public VideoRecordingProgress Progress(long frame, double seconds)
    {
        var speed = seconds > 0.5 ? frame / (double)FrameRate / seconds : 0;
        return new VideoRecordingProgress(
            $"Recording… {TimeFormat.Format(TimeSpan.FromSeconds(frame / (double)FrameRate))} of {TimeFormat.Format(Length)}",
            (double)frame / Frames, speed);
    }
}
