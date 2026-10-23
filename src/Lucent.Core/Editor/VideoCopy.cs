using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer.Editor;

/// <summary>What a copy of a video is saved as: an MP4 video, or its sound alone.</summary>
public enum VideoCopyFormat
{
    /// <summary>H.264 video and AAC sound in an MP4 file: plays everywhere.</summary>
    H264,

    /// <summary>HEVC (H.265) video and AAC sound in an MP4 file: about half the size, for newer devices.</summary>
    Hevc,

    Mp3,
    Flac,
    Wav,
}

/// <summary>How much a copy is squeezed: the bitrate of its video, and of its sound.</summary>
public enum VideoCopyQuality
{
    High,
    Medium,
    Small,
}

/// <summary>How to save a copy of a video in the editor, as chosen in its Save a copy box.</summary>
public sealed record VideoCopyChoice
{
    public VideoCopyFormat Format { get; init; } = VideoCopyFormat.H264;

    /// <summary>The picture's shorter side, in pixels (one of <see cref="VideoCopy.Sizes"/>; 0 for the video's own).</summary>
    public int Size { get; init; } = 720;

    /// <summary>Frames a second (one of <see cref="VideoCopy.FrameRates"/>; 0 for the video's own).</summary>
    public int FrameRate { get; init; } = 30;

    public VideoCopyQuality Quality { get; init; } = VideoCopyQuality.Medium;

    /// <summary>The file it's saved as.</summary>
    public string Path { get; init; } = string.Empty;

    public bool IsVideo => VideoCopy.IsVideo(Format);
}

/// <summary>
/// A video file being written by the system's own encoders (Media Foundation on Windows, AVFoundation on a Mac), with
/// the graphics card's or the Mac's media engine where there is one. Used on one thread.
/// </summary>
public interface IVideoCopyWriter : IDisposable
{
    /// <summary>Which encoder is doing it, for the user ("the graphics card (NVIDIA NVENC)"), when it's known.</summary>
    string EncoderNote { get; }

    /// <summary>Adds picture number <paramref name="frame"/> (from 0): BGRA, top row first, at the size the file was made for.</summary>
    void WriteVideo(nint pixels, int stride, long frame);

    /// <summary>Adds 16-bit stereo sound starting <paramref name="firstSample"/> samples in.</summary>
    void WriteAudio(byte[] samples, int length, long firstSample);

    void Finish();
}

/// <summary>
/// Saves a copy of a video open in the editor (the selection, or all of it) re-encoded smaller, or in another format:
/// H.264 or HEVC in an MP4, at a smaller size, frame rate or bitrate, or just its sound as MP3, FLAC or WAV. The
/// pictures are the original's, read by libVLC (<see cref="VideoExport"/>), and follow the edit; the sound is the
/// editor's.
/// </summary>
public static class VideoCopy
{
    /// <summary>Sizes on offer, by the picture's shorter side; 0 is the video's own.</summary>
    public static readonly IReadOnlyList<int> Sizes = new[] { 0, 1080, 720, 480 };

    /// <summary>Frame rates on offer; 0 is the video's own.</summary>
    public static readonly IReadOnlyList<int> FrameRates = new[] { 0, 30, 24 };

    // The sound in a video copy: AAC at these rates (thousands of bits a second), which both systems' encoders take.
    private static readonly int[] SoundBitrates = { 192_000, 160_000, 128_000 };

    /// <summary>
    /// Starts a video file: path, width, height, frames a second, video bitrate, sample rate (44100 or 48000), sound
    /// bitrate, HEVC (else H.264), and whether the graphics card's encoder may be used. Each app sets this. Throws
    /// <see cref="NotSupportedException"/> with a message for the user when the system has no encoder for it.
    /// </summary>
    public static Func<string, int, int, int, int, int, int, bool, bool, IVideoCopyWriter>? CreateWriter { get; set; }

    /// <summary>The choices made last time, offered again (this session).</summary>
    public static VideoCopyChoice LastChoice { get; set; } = new();

    public static bool IsVideo(VideoCopyFormat format) => format is VideoCopyFormat.H264 or VideoCopyFormat.Hevc;

    public static string Extension(VideoCopyFormat format) => format switch
    {
        VideoCopyFormat.Mp3 => ".mp3",
        VideoCopyFormat.Flac => ".flac",
        VideoCopyFormat.Wav => ".wav",
        _ => ".mp4",
    };

    /// <summary>The size a copy comes out at, for a size from <see cref="Sizes"/>: never bigger than the video, even numbers.</summary>
    public static (int Width, int Height) SizeFor(int videoWidth, int videoHeight, int size)
    {
        if (videoWidth <= 0 || videoHeight <= 0)
        {
            return (0, 0);
        }

        var shorter = Math.Min(videoWidth, videoHeight);
        var scale = size <= 0 || size >= shorter ? 1.0 : size / (double)shorter;
        return (Math.Max(2, (int)Math.Round(videoWidth * scale / 2) * 2), Math.Max(2, (int)Math.Round(videoHeight * scale / 2) * 2));
    }

    /// <summary>The video's own frame rate, to the nearest whole frame (at least 1, at most 120).</summary>
    public static int OwnFrameRate(VideoEdit video)
    {
        var times = video.Movie.PictureTimes;
        if (times.Count < 2 || times[^1] <= times[0])
        {
            return 30;
        }

        return Math.Clamp((int)Math.Round((times.Count - 1) / (times[^1] - times[0])), 1, 120);
    }

    /// <summary>The frame rate a copy comes out at, for a rate from <see cref="FrameRates"/>: never more than the video's own.</summary>
    public static int FrameRateFor(VideoEdit video, int rate)
    {
        var own = OwnFrameRate(video);
        return rate <= 0 ? own : Math.Min(rate, own);
    }

    /// <summary>The original's video bitrate, in bits a second.</summary>
    public static long OwnBitrate(VideoEdit video)
    {
        var track = video.Movie.Tracks.FirstOrDefault(track => track.IsVideo && track.Samples.Length > 0);
        if (track is null || video.Movie.Duration <= 0)
        {
            return 0;
        }

        return (long)(track.Samples.Sum(sample => sample.Size) * 8 / video.Movie.Duration);
    }

    /// <summary>
    /// The video bitrate for a copy: so many bits for each pixel of each frame (HEVC needs about 60% of what H.264 does
    /// for the same look), never more than the original's.
    /// </summary>
    public static int VideoBitrate(VideoEdit video, VideoCopyFormat format, VideoCopyQuality quality, int width, int height, int frameRate)
    {
        var bitsPerPixel = quality switch
        {
            VideoCopyQuality.High => 0.12,
            VideoCopyQuality.Medium => 0.07,
            _ => 0.04,
        };
        if (format == VideoCopyFormat.Hevc)
        {
            bitsPerPixel *= 0.6;
        }

        var bitrate = (long)(width * (long)height * frameRate * bitsPerPixel);
        var own = OwnBitrate(video);
        if (own > 0)
        {
            bitrate = Math.Min(bitrate, own);
        }

        return (int)Math.Clamp(bitrate, 100_000, 200_000_000);
    }

    /// <summary>The sound's bitrate in a video copy.</summary>
    public static int SoundBitrate(VideoCopyQuality quality) => SoundBitrates[(int)quality];

    /// <summary>About how big a copy comes out, in bytes. For FLAC this is a rough guide: it depends on the sound.</summary>
    public static long EstimateBytes(VideoEdit video, AudioBuffer audio, VideoCopyChoice choice, int videoWidth, int videoHeight, double seconds)
    {
        if (choice.IsVideo)
        {
            var (width, height) = SizeFor(videoWidth, videoHeight, choice.Size);
            var rate = FrameRateFor(video, choice.FrameRate);
            var bits = (VideoBitrate(video, choice.Format, choice.Quality, width, height, rate) + (double)SoundBitrate(choice.Quality)) * seconds;
            return (long)(bits / 8 * 1.01);
        }

        var channels = Math.Min(2, audio.ChannelCount);
        return choice.Format switch
        {
            VideoCopyFormat.Mp3 => (long)(Mp3Bitrate(choice.Quality) / 8.0 * seconds),
            VideoCopyFormat.Flac => (long)(audio.SampleRate * channels * Depth(choice.Quality) / 8.0 * seconds * 0.5),
            _ => (long)(audio.SampleRate * channels * Depth(choice.Quality) / 8.0 * seconds),
        };
    }

    /// <summary>
    /// Saves the stretch from <paramref name="from"/> to <paramref name="to"/> (seconds of the edit) of a video whose
    /// pictures are <paramref name="size"/>, with the editor's sound <paramref name="audio"/>, as chosen. Writes a
    /// temporary file next to the chosen one, then puts it in place. Returns a note on how it was encoded, for the user.
    /// Throws <see cref="InvalidDataException"/> or <see cref="NotSupportedException"/> with a message for the user.
    /// </summary>
    public static string Save(VideoEdit video, AudioBuffer audio, double from, double to, VideoCopyChoice choice, (int Width, int Height) size,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var temporary = SafeFile.TemporaryPathFor(choice.Path);
        var committed = false;
        try
        {
            string note;
            if (choice.IsVideo)
            {
                try
                {
                    note = SaveVideo(video, audio, from, to, choice, size, temporary, allowHardware: true, progress, cancellation);
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not NotSupportedException and not InvalidDataException)
                {
                    // Some graphics card encoders give up partway: Windows' or macOS's own one takes over.
                    SafeFile.Discard(temporary);
                    note = SaveVideo(video, audio, from, to, choice, size, temporary, allowHardware: false, progress, cancellation);
                }
            }
            else
            {
                note = SaveSound(audio, from, to, choice, temporary, progress, cancellation);
            }

            SafeFile.Hide(temporary);
            SafeFile.Commit(temporary, choice.Path, replace: true);
            committed = true;
            return note;
        }
        finally
        {
            if (!committed)
            {
                SafeFile.Discard(temporary);
            }
        }
    }

    private static string SaveVideo(VideoEdit video, AudioBuffer audio, double from, double to, VideoCopyChoice choice, (int Width, int Height) size,
        string path, bool allowHardware, IProgress<double>? progress, CancellationToken cancellation)
    {
        var create = CreateWriter ?? throw new NotSupportedException("Saving a video isn't available here.");
        var (width, height) = SizeFor(size.Width, size.Height, choice.Size);
        var rate = FrameRateFor(video, choice.FrameRate);
        var pictures = VideoExport.Pictures(video, from, to, rate);
        if (width == 0 || pictures.Count == 0)
        {
            throw new InvalidDataException("This video's pictures couldn't be read.");
        }

        // The sound, as the encoders take it: 16-bit stereo at 44.1 or 48 kHz.
        var sound = Sound(audio, from, to, out var soundFrames);
        var sampleRate = audio.SampleRate is 44100 or 48000 ? audio.SampleRate : 48000;
        if (sampleRate != audio.SampleRate)
        {
            sound = new ResamplingSampleProvider(sound, sampleRate);
            soundFrames = (long)(soundFrames * (double)sampleRate / audio.SampleRate);
        }

        using var writer = create(path, width, height, rate, VideoBitrate(video, choice.Format, choice.Quality, width, height, rate),
            sampleRate, SoundBitrate(choice.Quality), choice.Format == VideoCopyFormat.Hevc, allowHardware);

        // libVLC hands the pictures over on its own threads; the writer takes them on this one, a few behind at most.
        var bytes = width * 4 * height;
        using var queue = new BlockingCollection<byte[]>(4);
        var spare = new ConcurrentBag<byte[]>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var reading = Task.Run(() =>
        {
            try
            {
                using var libVlc = VideoEngine.CreateForFrames();
                VideoExport.Read(libVlc, video, pictures, width, height, (_, pixels, stride) =>
                {
                    var copy = spare.TryTake(out var buffer) ? buffer : new byte[bytes];
                    for (var row = 0; row < height; row++)
                    {
                        Marshal.Copy(pixels + row * stride, copy, row * width * 4, width * 4);
                    }

                    queue.Add(copy, stop.Token);
                }, stop.Token);
            }
            finally
            {
                queue.CompleteAdding();
            }
        }, stop.Token);

        var pcm = new byte[4 * 4096];
        var floats = new float[2 * 4096];
        long soundWritten = 0;
        void WriteSoundUntil(long until)
        {
            while (soundWritten < until)
            {
                var wanted = (int)Math.Min(4096, until - soundWritten);
                var read = sound.Read(floats, 0, wanted * 2) / 2;
                if (read <= 0)
                {
                    soundWritten = long.MaxValue;
                    return;
                }

                for (var i = 0; i < read * 2; i++)
                {
                    var sample = (int)Math.Round(Math.Clamp(floats[i], -1f, 1f) * 32767);
                    pcm[i * 2] = (byte)sample;
                    pcm[i * 2 + 1] = (byte)(sample >> 8);
                }

                writer.WriteAudio(pcm, read * 4, soundWritten);
                soundWritten += read;
            }
        }

        try
        {
            long frame = 0;
            foreach (var picture in queue.GetConsumingEnumerable(stop.Token))
            {
                // The sound up to the end of this picture goes in with it, so the two stay interleaved.
                WriteSoundUntil((long)((frame + 1) * (double)sampleRate / rate));
                unsafe
                {
                    fixed (byte* pixels = picture)
                    {
                        writer.WriteVideo((nint)pixels, width * 4, frame++);
                    }
                }

                spare.Add(picture);
                progress?.Report(0.98 * frame / pictures.Count);
            }

            reading.GetAwaiter().GetResult();
            WriteSoundUntil(soundFrames);
            writer.Finish();
        }
        catch (Exception)
        {
            stop.Cancel();
            try
            {
                reading.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The writer's failure is the one to tell.
            }

            throw;
        }

        progress?.Report(1);
        return writer.EncoderNote;
    }

    private static string SaveSound(AudioBuffer audio, double from, double to, VideoCopyChoice choice, string path,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var sound = Sound(audio, from, to, out var frames);
        var options = new ExportOptions
        {
            Format = choice.Format switch
            {
                VideoCopyFormat.Mp3 => ExportFormat.Mp3,
                VideoCopyFormat.Flac => ExportFormat.Flac,
                _ => ExportFormat.Wav,
            },
            Mp3Quality = choice.Quality switch
            {
                VideoCopyQuality.High => Mp3Quality.Cbr320,
                VideoCopyQuality.Medium => Mp3Quality.Cbr192,
                _ => Mp3Quality.Cbr128,
            },
            FlacBitDepth = Depth(choice.Quality),
            WavDepth = Depth(choice.Quality) == 24 ? PcmDepth.Int24 : PcmDepth.Int16,
        };
        var note = AudioEncoder.Encode(sound, path, options, frames, done => progress?.Report(done), cancellation);
        return note ?? string.Empty;
    }

    // The editor's sound from one moment to another, in stereo (a mono video's one channel in both).
    private static ISampleProvider Sound(AudioBuffer audio, double from, double to, out long frames)
    {
        var start = (int)Math.Clamp(Math.Round(from * audio.SampleRate), 0, audio.Frames);
        var end = (int)Math.Clamp(Math.Round(to * audio.SampleRate), start, audio.Frames);
        frames = end - start;
        var slice = audio.Slice(start, end - start);
        var channels = slice.ChannelCount >= 2 ? new[] { slice.Channels[0], slice.Channels[1] } : new[] { slice.Channels[0], slice.Channels[0] };
        return new AudioBufferSampleProvider(new AudioBuffer(channels, audio.SampleRate));
    }

    private static int Mp3Bitrate(VideoCopyQuality quality) => quality switch
    {
        VideoCopyQuality.High => 320_000,
        VideoCopyQuality.Medium => 192_000,
        _ => 128_000,
    };

    // FLAC and WAV: 24-bit at High, 16-bit otherwise.
    private static int Depth(VideoCopyQuality quality) => quality == VideoCopyQuality.High ? 24 : 16;

    /// <summary>
    /// Turns a BGRA picture into NV12 (a plane of brightness, then one of colour at half the size each way), with
    /// BT.709's colours in TV range, as the Windows encoders take pictures.
    /// </summary>
    public static unsafe void BgraToNv12(nint pixels, int stride, int width, int height, byte[] nv12)
    {
        var source = (byte*)pixels;
        fixed (byte* target = nv12)
        {
            var chroma = target + width * height;
            for (var y = 0; y < height; y++)
            {
                var row = source + y * stride;
                var luma = target + y * width;
                for (var x = 0; x < width; x++)
                {
                    int b = row[x * 4], g = row[x * 4 + 1], r = row[x * 4 + 2];
                    luma[x] = (byte)((47 * r + 157 * g + 16 * b + 128 >> 8) + 16);
                }
            }

            for (var y = 0; y < height / 2; y++)
            {
                var top = source + y * 2 * stride;
                var bottom = top + stride;
                var row = chroma + y * width;
                for (var x = 0; x < width / 2; x++)
                {
                    var at = x * 8;
                    int b = top[at] + top[at + 4] + bottom[at] + bottom[at + 4];
                    int g = top[at + 1] + top[at + 5] + bottom[at + 1] + bottom[at + 5];
                    int r = top[at + 2] + top[at + 6] + bottom[at + 2] + bottom[at + 6];
                    row[x * 2] = (byte)Math.Clamp((-26 * r - 87 * g + 112 * b + 512 >> 10) + 128, 16, 240);
                    row[x * 2 + 1] = (byte)Math.Clamp((112 * r - 102 * g - 10 * b + 512 >> 10) + 128, 16, 240);
                }
            }
        }
    }
}
