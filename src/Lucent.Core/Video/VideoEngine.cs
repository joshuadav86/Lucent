using LibVLCSharp.Shared;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// libVLC, the engine inside VLC, which Lucent plays videos with. It's loaded the first time a video is opened, and
/// one copy serves the whole app. Each app ships it beside itself (the VideoLAN.LibVLC packages): in
/// <c>libvlc/win-x64</c> on Windows and <c>libvlc/osx-arm64/lib</c> or <c>libvlc/osx-x64/lib</c> on a Mac.
/// </summary>
/// <remarks>
/// libVLC never plays a sound or opens a window of its own here: <see cref="VideoStream"/> takes its decoded sound
/// into Lucent's player and its pictures into <see cref="VideoFrames"/>, so the equalizer, the visuals and the outputs
/// treat a video like any song. libVLC is LGPL 2.1; its licence ships with it (assets/licenses/LICENSE-libVLC.txt).
/// </remarks>
public static class VideoEngine
{
    private static readonly object Gate = new();
    private static LibVLC? _libVlc;
    private static Exception? _failure;

    /// <summary>True once libVLC has loaded, or when it can still be tried. False after it failed to load.</summary>
    public static bool MayWork
    {
        get
        {
            lock (Gate)
            {
                return _failure is null;
            }
        }
    }

    /// <summary>libVLC's version, such as "3.0.24 Vetinari", once it has loaded; otherwise null.</summary>
    public static string? Version { get; private set; }

    /// <summary>Loads libVLC if it isn't yet. Throws <see cref="NotSupportedException"/> when it can't be loaded.</summary>
    public static LibVLC Get()
    {
        lock (Gate)
        {
            if (_libVlc is not null)
            {
                return _libVlc;
            }

            if (_failure is not null)
            {
                throw new NotSupportedException("Lucent's video player (libVLC) couldn't be loaded.", _failure);
            }

            try
            {
                Core.Initialize();
                _libVlc = new LibVLC(
                    // No file name written over the picture as it starts, and nothing said in the terminal.
                    "--no-video-title-show",
                    "--quiet",
                    // Decoded by the processor (FFmpeg's decoders), not the graphics card: every picture is copied
                    // back for Lucent to draw anyway, and the card's decoders take long enough to start that a
                    // video's first second showed up to half a second behind its sound (Windows and Mac alike).
                    "--avcodec-hw=none",
                    "--codec=avcodec,any",
                    // Never Xlib, on the Linux machines this is tested on: frames go to Lucent, not a window.
                    "--no-xlib");
                Version = _libVlc.Version;
                return _libVlc;
            }
            catch (Exception ex)
            {
                _failure = ex;
                throw new NotSupportedException("Lucent's video player (libVLC) couldn't be loaded.", ex);
            }
        }
    }
}
