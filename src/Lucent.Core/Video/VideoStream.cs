using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Radio;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// A video, playing through libVLC: its sound comes out of this stream, so the player plays it like any song (the
/// equalizer, the visuals, the meter and exclusive output all work on it), and its pictures go to <see cref="Frames"/>
/// for the visuals window to show.
/// </summary>
/// <remarks>
/// <para>
/// libVLC keeps its own clock. It shows each picture at its moment by that clock, and stamps each piece of sound it
/// decodes with the moment it should be heard. <see cref="Read"/> hands the player the sound that's due when what it
/// reads now will reach the speakers (<see cref="HeardDelayUs"/> later), so sound and picture line up: it plays
/// silence while the sound isn't due yet, and skips any that came too late.
/// </para>
/// <para>
/// libVLC decodes on its own threads. Its callbacks and <see cref="Read"/> (on the audio thread) meet under one lock;
/// libVLC is only ever told what to do from the UI thread.
/// </para>
/// </remarks>
public sealed class VideoStream : WaveStream, IPlayerControlled, ILiveStream
{
    /// <summary>Sound is played in stereo: libVLC mixes surround sound down.</summary>
    private const int Channels = 2;

    /// <summary>The sample rate used when the video has no sound, or doesn't say what rate it has.</summary>
    private const int DefaultRate = 48000;

    /// <summary>How long after it's read sound reaches the speakers: as <see cref="AudioCapture"/> reckons it.</summary>
    internal const long HeardDelayUs = AudioCapture.OutputLatencyMs * 2 / 3 * 1000L;

    /// <summary>How far sound can drift from its moment, once lined up, before it's lined up again.</summary>
    private const long DriftUs = 60_000;

    /// <summary>How close sound has to be to its moment to start playing it.</summary>
    private const long LineUpUs = 15_000;

    /// <summary>How much decoded sound is held, at most. libVLC waits for room when it gets ahead.</summary>
    private const double HeldSeconds = 2;

    /// <summary>How much sound a station with no picture holds before it plays, and again after it ran dry.</summary>
    private const double StationPrebufferSeconds = 1;

    /// <summary>How long to ignore sound decoded before a seek, if libVLC doesn't say it has thrown the rest away.</summary>
    private const long SeekDiscardUs = 2_000_000;

    private readonly LibVLC _libVlc;
    private Media _media;
    private Media? _playlist;
    private readonly MediaPlayer _player;
    private readonly WaveFormat _format;
    private readonly int _rate;
    private readonly int _heldFrames;
    private (int Width, int Height) _pictureSize;
    private int _sizeChecked;
    private readonly bool _rotated;
    private readonly object _gate = new();
    private readonly Queue<Block> _blocks = new();
    private int _heldFrameCount;
    private bool _lined;
    private bool _refilling;
    private bool _closing;
    private bool _ended;
    private bool _discarding;
    private long _discardSince;
    private long _pausedAt = -1;
    private long _lengthMs;
    private long? _seekMs;
    private long _startMs;
    private bool _started;
    private bool _wantPlaying;
    private long _position;
    private long _anchorMs = -1;
    private long _anchorClock;
    private volatile bool _hasPicture;
    private string? _nowPlaying;

    /// <summary>
    /// Opens a video file, or a stream at an address (with <c>://</c> in it), ready to play. Throws when libVLC can't
    /// be loaded or can't read it.
    /// </summary>
    public VideoStream(string pathOrAddress)
        : this(pathOrAddress, live: false, userAgent: null)
    {
    }

    private VideoStream(string pathOrAddress, bool live, string? userAgent)
    {
        Source = pathOrAddress;
        IsLive = live;
        _libVlc = VideoEngine.Get();
        _media = pathOrAddress.Contains("://")
            ? new Media(_libVlc, new Uri(pathOrAddress))
            : new Media(_libVlc, Path.GetFullPath(pathOrAddress), FromType.FromPath);
        if (userAgent is not null)
        {
            _media.AddOption(":http-user-agent=" + userAgent);
        }

        if (live)
        {
            // A station's playlist (.pls, .m3u) lists its stream, which is what's played.
            if (IsPlaylist(pathOrAddress))
            {
                Task.Run(() => _media.Parse(MediaParseOptions.ParseNetwork, 8_000)).GetAwaiter().GetResult();
                if (_media.SubItems.Count > 0 && _media.SubItems[0] is { } listed)
                {
                    // The playlist is kept until the end: the stream it lists belongs to it.
                    _playlist = _media;
                    _media = listed;
                    if (userAgent is not null)
                    {
                        _media.AddOption(":http-user-agent=" + userAgent);
                    }
                }
            }
        }
        else
        {
            // Off the UI thread: Parse finishes on libVLC's thread, and awaiting it here could wait on the UI thread forever.
            var parsed = Task.Run(() => _media.Parse(pathOrAddress.Contains("://") ? MediaParseOptions.ParseNetwork : MediaParseOptions.ParseLocal, 10_000))
                .GetAwaiter().GetResult();
            if (parsed == MediaParsedStatus.Failed)
            {
                _media.Dispose();
                throw new InvalidDataException("This file can't be played: it may be damaged, or not a video.");
            }
        }

        // A station's tracks aren't known until it plays: it's taken to have sound, and its picture shows when one comes.
        var tracks = live ? Array.Empty<MediaTrack>() : _media.Tracks;
        var audio = tracks.Where(track => track.TrackType == TrackType.Audio).Select(track => track.Data.Audio).FirstOrDefault();
        var video = tracks.Where(track => track.TrackType == TrackType.Video).Select(track => track.Data.Video).FirstOrDefault();
        HasSound = live || tracks.Any(track => track.TrackType == TrackType.Audio);
        _hasPicture = tracks.Any(track => track.TrackType == TrackType.Video);
        _rate = audio.Rate is >= 8000 and <= 384000 ? (int)audio.Rate : DefaultRate;
        _format = WaveFormat.CreateIeeeFloatWaveFormat(_rate, Channels);
        _heldFrames = (int)(_rate * HeldSeconds);
        _lengthMs = Math.Max(0, _media.Duration);
        _pictureSize = VideoFrames.SizeFor((int)video.Width, (int)video.Height, video.SarNum, video.SarDen);
        // Pictures taken with the phone on its side are stood up by libVLC, which swaps their width and height.
        _rotated = (int)video.Orientation >= 4;
        Title = _media.Meta(MetadataType.Title);

        _player = new MediaPlayer(_media);
        _player.SetAudioCallbacks(OnSound, OnSoundPaused, OnSoundResumed, OnSoundFlushed, OnSoundDrained);
        // libVLC 3 sends its sound to Lucent only as 16-bit stereo: a fixed format set beforehand (SetAudioFormat) is
        // ignored, and asked for any other way, it gives up. It's made float for the player as it comes (OnSound).
        _player.SetAudioFormatCallback(OnSoundFormat, OnSoundCleanup);
        _player.SetVideoFormatCallbacks(OnPictureFormat, OnPictureCleanup);
        _player.SetVideoCallbacks(OnPictureLock, null, OnPictureShown);
        _player.EndReached += (_, _) => End();
        _player.EncounteredError += (_, _) =>
        {
            Problem = live ? "The station couldn't be played, or stopped sending." : "The video stopped because libVLC couldn't go on reading it.";
            End();
        };
        _media.MetaChanged += (_, e) =>
        {
            // A station's "now playing" (from its ICY or HLS titles).
            if (e.MetadataType == MetadataType.NowPlaying && _media.Meta(MetadataType.NowPlaying) is { Length: > 0 } title && title != _nowPlaying)
            {
                _nowPlaying = title;
                TitleChanged?.Invoke(title);
            }
        };
        _player.LengthChanged += (_, e) =>
        {
            if (e.Length > 0)
            {
                Interlocked.Exchange(ref _lengthMs, e.Length);
            }
        };
        _player.TimeChanged += (_, e) =>
        {
            // A seek has landed once libVLC reports a time near where it was sent.
            if (_seekMs is { } target && Math.Abs(e.Time - target) < 2000)
            {
                _seekMs = null;
            }
        };
    }

    /// <summary>The file or address this was opened from.</summary>
    public string Source { get; }

    /// <summary>True for a station (<see cref="OpenStation"/>): it can't be moved through and has no end.</summary>
    public bool IsLive { get; }

    /// <summary>Raised (on libVLC's thread) when a station's picture first comes, so it can be shown.</summary>
    public event Action? PictureStarted;

    /// <summary>Raised (on libVLC's thread) when a station says a new song is on.</summary>
    public event Action<string>? TitleChanged;

    /// <summary>Raised (on libVLC's thread) when a station stopped sending, or couldn't be played.</summary>
    public event Action<Exception?>? Lost;

    WaveStream ILiveStream.Sound => this;

    /// <summary>The song a station says is on, or null.</summary>
    public string? NowPlaying => _nowPlaying;

    /// <summary>True while a station is on its way (or waiting for more): silence is playing.</summary>
    public bool IsBuffering
    {
        get
        {
            lock (_gate)
            {
                return IsLive && !_ended && (_blocks.Count == 0 || _refilling);
            }
        }
    }

    /// <summary>Not known: libVLC doesn't say.</summary>
    public int Bitrate => 0;

    /// <summary>The sound's format once libVLC has it ("Opus", "AAC"…), otherwise null.</summary>
    public string? Codec
    {
        get
        {
            var codec = _player.Media?.Tracks.FirstOrDefault(track => track.TrackType == TrackType.Audio).Codec ?? 0;
            return codec == 0 ? null : CodecName(codec);
        }
    }

    /// <summary>
    /// Tunes in to a station through libVLC: a video station, or one in a format Lucent's own radio player doesn't read
    /// (Opus, Ogg, FLAC, AAC in MP4). Its picture, if it has one, shows once it comes (<see cref="PictureStarted"/>).
    /// </summary>
    public static VideoStream OpenStation(string address, string userAgent) => new(address, live: true, userAgent);

    private static bool IsPlaylist(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && Path.GetExtension(uri.AbsolutePath).ToLowerInvariant() is ".pls" or ".m3u" or ".asx" or ".xspf";

    // libVLC's four-letter codes, as people know them.
    private static string CodecName(uint fourcc)
    {
        var code = new string(new[] { (char)(fourcc & 0xFF), (char)((fourcc >> 8) & 0xFF), (char)((fourcc >> 16) & 0xFF), (char)(fourcc >> 24) }).Trim();
        return code.ToLowerInvariant() switch
        {
            "mpga" or "mp3" => "MP3",
            "mp4a" => "AAC",
            "opus" => "Opus",
            "vorb" => "Vorbis",
            "flac" => "FLAC",
            "a52" => "AC-3",
            _ => code.ToUpperInvariant(),
        };
    }

    /// <summary>The title tagged in the file, or null.</summary>
    public string? Title { get; }

    /// <summary>True when the video has a sound track. Without one, the player plays silence for as long as it lasts.</summary>
    public bool HasSound { get; }

    /// <summary>
    /// True when there's a picture to show (an audio-only file opened this way has none). A station's turns true when
    /// its first picture comes.
    /// </summary>
    public bool HasPicture => _hasPicture;

    /// <summary>The pictures, as libVLC shows them.</summary>
    public VideoFrames Frames { get; } = new();

    /// <summary>Why the video stopped early, when it did; otherwise null.</summary>
    public string? Problem { get; private set; }

    public override WaveFormat WaveFormat => _format;

    public override bool CanSeek => !IsLive;

    public override long Length => Interlocked.Read(ref _lengthMs) * _format.AverageBytesPerSecond / 1000 / _format.BlockAlign * _format.BlockAlign;

    public override long Position
    {
        get => (long)(CurrentTime.TotalSeconds * _format.AverageBytesPerSecond) / _format.BlockAlign * _format.BlockAlign;
        set => CurrentTime = TimeSpan.FromSeconds((double)value / _format.AverageBytesPerSecond);
    }

    public override TimeSpan CurrentTime
    {
        get
        {
            // A station's time is how long it's been heard since tuning in. libVLC's own time for a live stream is
            // where it is in the station's window of the broadcast, which jumps about as the window moves.
            if (IsLive)
            {
                return TimeSpan.FromSeconds((double)Interlocked.Read(ref _position) / _format.AverageBytesPerSecond);
            }

            if (_seekMs is { } target)
            {
                return TimeSpan.FromMilliseconds(target);
            }

            if (!_started)
            {
                return TimeSpan.FromMilliseconds(_startMs);
            }

            lock (_gate)
            {
                if (_ended && _blocks.Count == 0)
                {
                    return TotalTime;
                }
            }

            return TimeSpan.FromMilliseconds(SmoothTime());
        }
        set => Seek((long)Math.Max(0, value.TotalMilliseconds));
    }

    // libVLC's own time only moves on every so often (each time it reads a little more of the file), so the time
    // shown runs on from where it last said by the clock, and follows it again when the two part.
    private long SmoothTime()
    {
        var reported = Math.Max(0, _player.Time);
        var clock = _libVlc.Clock;
        if (!_wantPlaying || _anchorMs < 0)
        {
            _anchorMs = reported;
            _anchorClock = clock;
            return reported;
        }

        var running = _anchorMs + (clock - _anchorClock) / 1000;
        if (Math.Abs(running - reported) > 400)
        {
            _anchorMs = reported;
            _anchorClock = clock;
            return reported;
        }

        var length = Interlocked.Read(ref _lengthMs);
        return length > 0 ? Math.Min(running, length) : running;
    }

    /// <summary>How many times the sound was lined up again with the picture after drifting, for the self-test.</summary>
    public int Relined { get; private set; }

    /// <summary>How many pictures libVLC has dropped for coming too late to show, since it last started, for the self-test.</summary>
    public int LostPictures => _media.Statistics.LostPictures;

    /// <summary>libVLC's clock now, in microseconds: the clock the pictures are shown by.</summary>
    public long Clock => _libVlc.Clock;

    // ---- What the player says ----

    public void Playing()
    {
        _wantPlaying = true;
        if (!_started)
        {
            _started = true;
            if (_startMs > 0)
            {
                _media.AddOption($":start-time={_startMs / 1000.0:0.###}".Replace(',', '.'));
            }

            _player.Play();
            return;
        }

        lock (_gate)
        {
            if (_ended)
            {
                // Played through, then played again: from the start.
                Restart(0);
                return;
            }
        }

        _player.SetPause(false);
    }

    public void Paused()
    {
        _wantPlaying = false;
        if (_started)
        {
            _player.SetPause(true);
        }
    }

    public void Stopped()
    {
        Paused();
        Seek(0);
    }

    // ---- The sound ----

    public override int Read(byte[] buffer, int offset, int count)
    {
        var frames = count / _format.BlockAlign;
        var output = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, frames * _format.BlockAlign));
        var written = 0;
        lock (_gate)
        {
            // libVLC has paused (or hasn't yet said it's carried on): what it decoded isn't due until it does.
            if (_pausedAt >= 0)
            {
                output.Clear();
                written = frames;
                frames = 0;
            }

            if (IsLive && !_hasPicture && written < frames)
            {
                written = ReadInOrder(output, written, frames);
                frames = 0;
            }

            var heard = _libVlc.Clock + HeardDelayUs;
            while (written < frames)
            {
                if (_blocks.Count == 0)
                {
                    if (_ended)
                    {
                        break;
                    }

                    // Nothing decoded yet (starting, seeking, or a slow disk): silence, and line up again after.
                    output[(written * Channels)..].Clear();
                    written = frames;
                    _lined = false;
                    break;
                }

                var block = _blocks.Peek();
                var due = block.Pts + block.Used * 1_000_000L / _rate;
                var now = heard + written * 1_000_000L / _rate;
                var early = due - now;
                var allowed = _lined ? DriftUs : LineUpUs;
                if (early > allowed)
                {
                    var gap = (int)Math.Min(frames - written, early * _rate / 1_000_000L);
                    output.Slice(written * Channels, gap * Channels).Clear();
                    written += gap;
                    Reline();
                    continue;
                }

                if (early < -allowed)
                {
                    SkipFrames((int)Math.Min(int.MaxValue, -early * _rate / 1_000_000L));
                    Reline();
                    continue;
                }

                _lined = true;
                var take = Math.Min(frames - written, block.Frames - block.Used);
                block.Samples.AsSpan(block.Used * Channels, take * Channels).CopyTo(output[(written * Channels)..]);
                block.Used += take;
                written += take;
                _heldFrameCount -= take;
                if (block.Used == block.Frames)
                {
                    _blocks.Dequeue();
                }
            }

            Monitor.PulseAll(_gate);
        }

        Interlocked.Add(ref _position, written * _format.BlockAlign);
        return written * _format.BlockAlign;
    }

    // Called with the lock held, for a station with no picture: there's nothing to keep the sound in step with, so it
    // plays in the order it came, as Lucent's own radio player plays a station. (Lined up with libVLC's clock, it was
    // cut short and padded with silence each time a live stream's clock jumped, which stuttered.) When it runs dry it
    // plays silence until a moment's worth has come again, so it doesn't stutter in and out.
    private int ReadInOrder(Span<float> output, int written, int frames)
    {
        if (_refilling && _heldFrameCount < _rate * StationPrebufferSeconds && !_ended)
        {
            output[(written * Channels)..].Clear();
            return frames;
        }

        _refilling = false;
        while (written < frames && _blocks.Count > 0)
        {
            var block = _blocks.Peek();
            var take = Math.Min(frames - written, block.Frames - block.Used);
            block.Samples.AsSpan(block.Used * Channels, take * Channels).CopyTo(output[(written * Channels)..]);
            block.Used += take;
            written += take;
            _heldFrameCount -= take;
            if (block.Used == block.Frames)
            {
                _blocks.Dequeue();
            }
        }

        if (written < frames && !_ended)
        {
            output[(written * Channels)..].Clear();
            written = frames;
            _refilling = true;
        }

        return written;
    }

    // Called with the lock held: sound and picture had drifted apart, and are lined up again.
    private void Reline()
    {
        if (_lined)
        {
            Relined++;
        }

        _lined = false;
    }

    // Called with the lock held: throws away the next frames of sound.
    private void SkipFrames(int frames)
    {
        while (frames > 0 && _blocks.Count > 0)
        {
            var block = _blocks.Peek();
            var take = Math.Min(frames, block.Frames - block.Used);
            block.Used += take;
            frames -= take;
            _heldFrameCount -= take;
            if (block.Used == block.Frames)
            {
                _blocks.Dequeue();
            }
        }
    }

    // On libVLC's audio thread: sound decoded, stamped with when it should be heard.
    private void OnSound(nint data, nint samples, uint count, long pts)
    {
        var frames = (int)count;
        var copy = new float[frames * Channels];
        unsafe
        {
            var sixteen = new ReadOnlySpan<short>((void*)samples, copy.Length);
            for (var i = 0; i < copy.Length; i++)
            {
                copy[i] = sixteen[i] / 32768f;
            }
        }
        lock (_gate)
        {
            if (_discarding)
            {
                if (_libVlc.Clock - _discardSince < SeekDiscardUs)
                {
                    return;
                }

                _discarding = false;
            }

            // Wait for room when libVLC is ahead, which keeps it from decoding the whole file at once.
            while (!_closing && !_discarding && _heldFrameCount >= _heldFrames)
            {
                Monitor.Wait(_gate, 200);
            }

            if (_closing || _discarding)
            {
                return;
            }

            _blocks.Enqueue(new Block(copy, frames, pts));
            _heldFrameCount += frames;
        }
    }

    // On libVLC's thread, as the sound starts: asks for 16-bit stereo at the rate the player was told.
    private unsafe int OnSoundFormat(ref nint opaque, ref nint format, ref uint rate, ref uint channels)
    {
        // format is libVLC's four-character buffer itself (LibVLCSharp passes it as a reference to a pointer).
        fixed (nint* buffer = &format)
        {
            "S16N"u8.CopyTo(new Span<byte>(buffer, 4));
        }

        rate = (uint)_rate;
        channels = Channels;
        return 0;
    }

    private void OnSoundCleanup(nint opaque)
    {
    }

    // libVLC paused at this moment by its clock.
    private void OnSoundPaused(nint data, long pts)
    {
        lock (_gate)
        {
            _pausedAt = pts;
        }
    }

    // libVLC carried on at this moment: what it decoded before the pause is now due that much later.
    private void OnSoundResumed(nint data, long pts)
    {
        lock (_gate)
        {
            if (_pausedAt >= 0)
            {
                var paused = pts - _pausedAt;
                foreach (var block in _blocks)
                {
                    block.Pts += paused;
                }
            }

            _pausedAt = -1;
            _lined = false;
        }
    }

    // libVLC threw away what it had decoded (after a seek, say).
    private void OnSoundFlushed(nint data, long pts)
    {
        lock (_gate)
        {
            _blocks.Clear();
            _heldFrameCount = 0;
            _discarding = false;
            _lined = false;
            Monitor.PulseAll(_gate);
        }
    }

    // The video's sound has all been decoded: libVLC waits here until it's been played.
    private void OnSoundDrained(nint data)
    {
        lock (_gate)
        {
            var deadline = Environment.TickCount64 + 30_000;
            while (!_closing && !_discarding && _blocks.Count > 0 && Environment.TickCount64 < deadline)
            {
                Monitor.Wait(_gate, 200);
            }
        }
    }

    private void End()
    {
        lock (_gate)
        {
            _ended = true;
            Monitor.PulseAll(_gate);
        }

        if (IsLive)
        {
            Lost?.Invoke(Problem is { } problem ? new RadioProblem(problem) : null);
        }
    }

    // ---- The pictures ----

    // On libVLC's thread, when it knows the picture's size: asks for BGRA at the video's own shape.
    private uint OnPictureFormat(ref nint opaque, nint chroma, ref uint width, ref uint height, ref uint pitches, ref uint lines)
    {
        var (w, h) = _pictureSize;
        if (w == 0 || h == 0)
        {
            (w, h) = VideoFrames.SizeFor((int)width, (int)height, 1, 1);
        }
        else if (_rotated)
        {
            (w, h) = (h, w);
        }

        Marshal.Copy("RV32"u8.ToArray(), 0, chroma, 4);
        width = (uint)w;
        height = (uint)h;
        pitches = (uint)(w * 4);
        lines = (uint)h;
        Frames.SetSize(w, h);
        if (!_hasPicture)
        {
            _hasPicture = true;
            PictureStarted?.Invoke();
        }

        return 1;
    }

    private void OnPictureCleanup(ref nint opaque)
    {
    }

    private nint OnPictureLock(nint opaque, nint planes)
    {
        Marshal.WriteIntPtr(planes, Frames.BeginPicture());
        return 0;
    }

    private void OnPictureShown(nint opaque, nint picture)
    {
        Frames.FinishPicture();
        if (IsLive && Interlocked.Exchange(ref _sizeChecked, 1) == 0)
        {
            Task.Run(CheckLiveSize);
        }
    }

    // A station's picture size isn't known until it plays, so the first was asked for at the size libVLC offered,
    // which its decoder pads (320 × 180 comes as 320 × 194) and so is a little stretched. Once the true size is
    // known, the picture is asked for again at it, by turning the picture off and on.
    private void CheckLiveSize()
    {
        uint width = 0, height = 0;
        if (_closing || !_player.Size(0, ref width, ref height) || width == 0 || height == 0)
        {
            return;
        }

        var size = VideoFrames.SizeFor((int)width, (int)height, 1, 1);
        if (size == (Frames.Width, Frames.Height))
        {
            return;
        }

        _pictureSize = size;
        var track = _player.VideoTrack;
        if (track >= 0)
        {
            _player.SetVideoTrack(-1);
            _player.SetVideoTrack(track);
        }
    }

    // ---- Seeking and closing ----

    private void Seek(long ms)
    {
        if (IsLive)
        {
            // A station can't be moved through; the player's rewind on Stop lands here and does nothing.
            return;
        }

        var length = Interlocked.Read(ref _lengthMs);
        if (length > 0)
        {
            ms = Math.Min(ms, length);
        }

        if (!_started)
        {
            _startMs = ms;
            return;
        }

        bool ended;
        lock (_gate)
        {
            ended = _ended;
            DiscardHeld();
        }

        if (ended)
        {
            Restart(ms);
            if (!_wantPlaying)
            {
                _player.SetPause(true);
            }

            return;
        }

        _seekMs = ms;
        _anchorMs = -1;
        _player.Time = ms;
    }

    // Called with the lock held: drops the sound held, and anything already on its way from before a seek.
    private void DiscardHeld()
    {
        _blocks.Clear();
        _heldFrameCount = 0;
        _discarding = true;
        _discardSince = _libVlc.Clock;
        _lined = false;
        Monitor.PulseAll(_gate);
    }

    // Plays again from ms, after libVLC reached the end (and stopped).
    private void Restart(long ms)
    {
        lock (_gate)
        {
            // Let any callback that's waiting go, so Stop doesn't wait on it.
            _closing = true;
            Monitor.PulseAll(_gate);
        }

        _player.Stop();
        lock (_gate)
        {
            _closing = false;
            _ended = false;
            _blocks.Clear();
            _heldFrameCount = 0;
            _discarding = false;
            _lined = false;
        }

        _media.AddOption($":start-time={ms / 1000.0:0.###}".Replace(',', '.'));
        _seekMs = ms;
        _player.Play();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _closing = true;
                Monitor.PulseAll(_gate);
            }

            _player.Stop();
            _player.Dispose();
            _media.Dispose();
            _playlist?.Dispose();
            Frames.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <summary>A piece of decoded sound: stereo float samples, and when (by libVLC's clock) the first should be heard.</summary>
    private sealed class Block
    {
        public Block(float[] samples, int frames, long pts)
        {
            Samples = samples;
            Frames = frames;
            Pts = pts;
        }

        public float[] Samples { get; }

        public int Frames { get; }

        public long Pts { get; set; }

        public int Used { get; set; }
    }
}
