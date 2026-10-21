using System.IO;
using LibVLCSharp.Shared;
using NAudio.Wave;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Decodes any sound libVLC reads (AC-3, DTS, WMA, Matroska and WebM, TTA, Speex, Opus or FLAC in MP4, and the rest)
/// into a float WAV in the temp folder, as fast as it decodes, for the editor, converting, finding beats and the
/// recorder. It's the last decoder <see cref="AudioReaderFactory"/> tries, so a file that plays also edits.
/// </summary>
/// <remarks>
/// <para>
/// libVLC converts with its "transcode" stream output, which goes as fast as the computer can. It has one flaw: for
/// sound with no packetizer of its own (PCM, ALAC, TTA, WMA and others), libVLC 3 throws the file's last packet away
/// at the end (its "copy" packetizer has no drain). That can be a whole second of a TTA file. So when what came out is
/// shorter than the file says it is, the end is decoded again by playing it into libVLC's file output (which does
/// drain, but only goes at playing speed) from a moment before, and joined on where the two match.
/// </para>
/// <para>The temp file is deleted when the stream returned is disposed.</para>
/// </remarks>
public static class LibVlcAudioDecoder
{
    /// <summary>How far before the end of the fast decode the end is decoded again, so the two overlap.</summary>
    private const double TailOverlapSeconds = 1.5;

    /// <summary>
    /// How much of the fast decode's end isn't trusted, in frames: the packet before the lost one can come out wrong
    /// too (WMA's does, missing the overlap the lost one would have added).
    /// </summary>
    private const int UntrustedFrames = 4096;

    /// <summary>How much of the fast decode, just before the untrusted end, is matched against the second decode.</summary>
    private const int MatchFrames = 2048;

    /// <summary>The longest a file can be for its end to be decoded again at playing speed (so it can't take long).</summary>
    private const double MaxReplaySeconds = 8;

    /// <summary>
    /// Decodes <paramref name="path"/>. Throws <see cref="NotSupportedException"/> when libVLC can't be loaded or finds
    /// no sound in the file.
    /// </summary>
    public static WaveStream Open(string path, CancellationToken cancellation = default)
    {
        var libVlc = VideoEngine.Get();
        var temp = TempPath();
        try
        {
            var expectedMs = Transcode(libVlc, Path.GetFullPath(path), temp, cancellation);
            var decoded = PcmFileReader.TryOpen(temp);
            var frames = decoded is null ? 0 : decoded.Length / decoded.WaveFormat.BlockAlign;
            var rate = decoded?.WaveFormat.SampleRate ?? 48000;
            var expectedFrames = expectedMs * rate / 1000;
            var missing = expectedFrames - frames;

            // Within 10 ms is close enough: the length libVLC says is only to the millisecond, and lossy formats pad.
            // An unknown length (libVLC doesn't say for CAF) is checked as if something were missing.
            var checkEnd = expectedMs == 0 || missing >= rate / 100;
            if (decoded is null || frames < UntrustedFrames + MatchFrames)
            {
                // Too short to join anything on (a TTA file under a second can come out with nothing at all): a short
                // file is decoded again whole, at playing speed.
                decoded?.Dispose();
                if (expectedMs > MaxReplaySeconds * 1000 || !checkEnd)
                {
                    throw new NotSupportedException($"libVLC found no sound it could decode in {Path.GetFileName(path)}.");
                }

                DeleteQuietly(temp);
                Replay(path, 0, temp, cancellation);
                var whole = PcmFileReader.TryOpen(temp);
                if (whole is null || whole.Length == 0)
                {
                    whole?.Dispose();
                    throw new NotSupportedException($"libVLC found no sound it could decode in {Path.GetFileName(path)}.");
                }

                return new DecodedFile(whole, whole.Length / whole.WaveFormat.BlockAlign, Array.Empty<float>(), temp);
            }

            // More than a few seconds short isn't one lost packet but a file libVLC can't read to the end, and
            // decoding it again at playing speed would take as long.
            if (checkEnd && missing <= rate * (long)MaxReplaySeconds && FindEnd(path, decoded, cancellation) is { } end)
            {
                return new DecodedFile(decoded, end.Keep, end.Tail, temp);
            }

            return new DecodedFile(decoded, frames, Array.Empty<float>(), temp);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"Lucent-decode-{Guid.NewGuid():N}.wav");

    /// <summary>The fast decode, into <paramref name="destination"/>. Returns how long libVLC says the file is (ms), or 0.</summary>
    private static long Transcode(LibVLC libVlc, string path, string destination, CancellationToken cancellation)
    {
        using var media = new Media(libVlc, path, FromType.FromPath);
        // fl32: libVLC's float samples, kept as they are. The file name is quoted for the sout chain's own parser.
        var quoted = destination.Replace("\\", "\\\\").Replace("'", "\\'");
        media.AddOption($":sout=#transcode{{acodec=fl32}}:std{{access=file,mux=wav,dst='{quoted}'}}");
        media.AddOption(":no-sout-video");
        media.AddOption(":no-sout-spu");
        media.AddOption(":no-sout-all");
        Run(media, null, cancellation);
        return media.Duration > 0 ? media.Duration : 0;
    }

    /// <summary>
    /// Plays <paramref name="path"/> from <paramref name="startSeconds"/> to its end into libVLC's file output, which
    /// writes a float WAV at <paramref name="destination"/>. It goes at playing speed, so it's given a few seconds more
    /// than <see cref="MaxReplaySeconds"/> at most.
    /// </summary>
    private static void Replay(string path, double startSeconds, string destination, CancellationToken cancellation)
    {
        var options = new[]
        {
            "--quiet", "--no-video", "--no-xlib", "--aout=afile", "--audiofile-format=float32", "--audiofile-wav",
            "--no-audio-time-stretch", "--audio-replay-gain-mode=none", "--audiofile-file=" + destination,
        };
        using var libVlc = new LibVLC(options);
        using var media = new Media(libVlc, Path.GetFullPath(path), FromType.FromPath);
        if (startSeconds > 0)
        {
            media.AddOption($":start-time={startSeconds.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)}");
        }

        Run(media, TimeSpan.FromSeconds(MaxReplaySeconds + TailOverlapSeconds + 5), cancellation);
    }

    /// <summary>
    /// The end the fast decode lost (see the remarks): how many of its frames to keep, then the sound after them from
    /// the second decode. Null when it can't be found, and the fast decode is kept as it is.
    /// </summary>
    private static (long Keep, float[] Tail)? FindEnd(string path, PcmFileReader decoded, CancellationToken cancellation)
    {
        var format = decoded.WaveFormat;
        var channels = format.Channels;
        var frames = decoded.Length / format.BlockAlign;
        var keep = frames - UntrustedFrames;
        var start = Math.Max(0, (double)(keep - MatchFrames) / format.SampleRate - TailOverlapSeconds);
        var temp = TempPath();
        try
        {
            Replay(path, start, temp, cancellation);
            using var again = PcmFileReader.TryOpen(temp);
            if (again is null || again.WaveFormat.Channels != channels || again.WaveFormat.SampleRate != format.SampleRate)
            {
                return null;
            }

            var end = ReadAll(again);
            var last = new float[MatchFrames * channels];
            decoded.Position = (keep - MatchFrames) * format.BlockAlign;
            decoded.ToSampleProvider().Read(last, 0, last.Length);
            decoded.Position = 0;

            // The second decode starts about at the moment asked for: the match is looked for outward from there,
            // since the same few frames can come round again (a held note, or silence).
            var expectedAt = (int)Math.Max(0, keep - MatchFrames - (long)Math.Round(start * format.SampleRate));
            var at = Match(last, end, channels, expectedAt);
            if (at < 0)
            {
                return null;
            }

            var from = (at + MatchFrames) * channels;
            return (keep, from < end.Length ? end[from..] : Array.Empty<float>());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The end stays as libVLC left it; the rest is all there.
            return null;
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>
    /// Where (in frames) <paramref name="needle"/> sits in <paramref name="haystack"/>: the nearest place to
    /// <paramref name="expected"/> where it's the same sound, or close to it. -1 when it isn't there.
    /// </summary>
    internal static int Match(float[] needle, float[] haystack, int channels, int expected)
    {
        var places = haystack.Length / channels - needle.Length / channels;
        if (places < 0)
        {
            return -1;
        }

        var energy = 0.0;
        foreach (var sample in needle)
        {
            energy += sample * sample;
        }

        // The same decoder makes the same samples, give or take rounding.
        var tolerance = Math.Max(1e-6, energy * 1e-4);
        expected = Math.Clamp(expected, 0, places);
        for (var step = 0; step <= places; step++)
        {
            foreach (var p in step == 0 ? new[] { expected } : new[] { expected - step, expected + step })
            {
                if (p < 0 || p > places)
                {
                    continue;
                }

                var error = 0.0;
                var offset = p * channels;
                for (var i = 0; i < needle.Length && error <= tolerance; i++)
                {
                    var d = haystack[offset + i] - needle[i];
                    error += d * d;
                }

                if (error <= tolerance)
                {
                    return p;
                }
            }
        }

        return -1;
    }

    private static float[] ReadAll(WaveStream stream)
    {
        var samples = stream.ToSampleProvider();
        var all = new List<float>();
        var chunk = new float[16384];
        int read;
        while ((read = samples.Read(chunk, 0, chunk.Length)) > 0)
        {
            all.AddRange(chunk.AsSpan(0, read));
        }

        return all.ToArray();
    }

    /// <summary>Plays <paramref name="media"/> to its end (into whatever its options send it to), or throws.</summary>
    private static void Run(Media media, TimeSpan? limit, CancellationToken cancellation)
    {
        using var player = new MediaPlayer(media);
        using var ended = new ManualResetEventSlim();
        var failed = false;
        player.EndReached += (_, _) => ended.Set();
        player.EncounteredError += (_, _) =>
        {
            failed = true;
            ended.Set();
        };

        if (!player.Play())
        {
            throw new NotSupportedException("libVLC couldn't start decoding the file.");
        }

        // A stall (no progress for half a minute) is given up on rather than waited for forever.
        var lastTime = -1L;
        var still = System.Diagnostics.Stopwatch.StartNew();
        var running = System.Diagnostics.Stopwatch.StartNew();
        while (!ended.Wait(250))
        {
            if (running.Elapsed > limit)
            {
                player.Stop();
                throw new NotSupportedException("libVLC took too long decoding the file.");
            }

            if (cancellation.IsCancellationRequested)
            {
                player.Stop();
                cancellation.ThrowIfCancellationRequested();
            }

            var time = player.Time;
            if (time != lastTime)
            {
                lastTime = time;
                still.Restart();
            }
            else if (still.Elapsed > TimeSpan.FromSeconds(30))
            {
                player.Stop();
                throw new NotSupportedException("libVLC stopped making progress decoding the file.");
            }
        }

        // Stopping closes the output, which finishes the WAV's header.
        player.Stop();
        if (failed)
        {
            throw new NotSupportedException("libVLC couldn't decode the file.");
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Left in the temp folder, which the system clears.
        }
    }

    /// <summary>The decoded file, with the end decoded again (if any) after it, deleted once closed.</summary>
    private sealed class DecodedFile : WaveStream
    {
        private readonly PcmFileReader _file;
        private readonly byte[] _tail;
        private readonly string _path;
        private readonly long _keep;
        private long _position;

        public DecodedFile(PcmFileReader file, long keepFrames, float[] tail, string path)
        {
            _file = file;
            _keep = keepFrames * file.WaveFormat.BlockAlign;
            _tail = new byte[tail.Length * 4];
            Buffer.BlockCopy(tail, 0, _tail, 0, _tail.Length);
            _path = path;
        }

        public override WaveFormat WaveFormat => _file.WaveFormat;

        public override long Length => _keep + _tail.Length;

        public override long Position
        {
            get => _position;
            set
            {
                _position = Math.Clamp(value / WaveFormat.BlockAlign * WaveFormat.BlockAlign, 0, Length);
                if (_position <= _keep)
                {
                    _file.Position = _position;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count -= count % WaveFormat.BlockAlign;
            var done = 0;
            if (_position < _keep)
            {
                done = _file.Read(buffer, offset, (int)Math.Min(count, _keep - _position));
                _position += done;
                if (done == 0)
                {
                    _position = _keep;
                }
            }

            if (done < count && _position >= _keep)
            {
                var at = (int)(_position - _keep);
                var more = Math.Min(count - done, _tail.Length - at);
                if (more > 0)
                {
                    Buffer.BlockCopy(_tail, at, buffer, offset + done, more);
                    done += more;
                    _position += more;
                }
            }

            return done;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _file.Dispose();
                DeleteQuietly(_path);
            }

            base.Dispose(disposing);
        }
    }
}
