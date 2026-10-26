using NAudio.Wave;

namespace Lucent.Radio;

/// <summary>
/// A station playing: Lucent's own <see cref="RadioStream"/> for MP3 and AAC, or a video stream (libVLC) for video
/// stations and the formats Lucent's own player can't read (Opus, Ogg, FLAC, AAC in MP4).
/// </summary>
public interface ILiveStream : IDisposable
{
    /// <summary>The sound, for the player.</summary>
    WaveStream Sound { get; }

    /// <summary>The song that's on, as the station last said, or null when it doesn't say.</summary>
    string? NowPlaying { get; }

    /// <summary>True while it's waiting for the stream to catch up (playing silence).</summary>
    bool IsBuffering { get; }

    /// <summary>Kilobits per second, or 0 when the stream doesn't say.</summary>
    int Bitrate { get; }

    /// <summary>The sound's format ("MP3", "Opus"…), or null when it isn't known yet.</summary>
    string? Codec { get; }

    /// <summary>Raised (on a background thread) when the station says a new song is on.</summary>
    event Action<string>? TitleChanged;

    /// <summary>Raised (on a background thread) when the station has gone for good.</summary>
    event Action<Exception?>? Lost;
}
