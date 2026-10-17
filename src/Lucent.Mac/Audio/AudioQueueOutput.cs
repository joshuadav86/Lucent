using System.Runtime.InteropServices;
using NAudio.Wave;
using static Lucent.Mac.Interop.CoreAudio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Plays audio through macOS's Audio Queue Services: the Mac's stand-in for NAudio's WaveOutEvent.
/// A few buffers take turns: while one plays, Core Audio hands the others back to be refilled.
/// </summary>
public sealed unsafe class AudioQueueOutput : IWavePlayer
{
    private const int BufferCount = 3;

    private readonly object _lock = new();
    private readonly int _latencyMs;
    private IWaveProvider? _source;
    private byte[] _scratch = Array.Empty<byte>();
    private GCHandle _self;
    private IntPtr _queue;
    private int _buffersQueued;
    private bool _ended;
    private float _volume = 1f;

    /// <param name="latencyMs">How much audio is queued ahead of the speakers, split between the buffers.</param>
    public AudioQueueOutput(int latencyMs) => _latencyMs = latencyMs;

    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;

    public WaveFormat OutputWaveFormat => _source?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    /// <summary>Not used: the player turns the music down with a VolumeSampleProvider, as on Windows.</summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public void Init(IWaveProvider waveProvider)
    {
        if (waveProvider.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat || waveProvider.WaveFormat.BitsPerSample != 32)
        {
            throw new ArgumentException("The Mac output plays 32-bit float audio.", nameof(waveProvider));
        }

        (IntPtr, GCHandle) old;
        lock (_lock)
        {
            old = DetachQueue();
            _source = waveProvider;
        }

        DisposeQueue(old);
    }

    public void Play()
    {
        lock (_lock)
        {
            if (_source is null || PlaybackState == PlaybackState.Playing)
            {
                return;
            }

            if (_queue == IntPtr.Zero)
            {
                OpenQueue();
            }

            Check(AudioQueueStart(_queue, IntPtr.Zero), "start playing");
            PlaybackState = PlaybackState.Playing;
        }
    }

    public void Pause()
    {
        lock (_lock)
        {
            if (_queue != IntPtr.Zero && PlaybackState == PlaybackState.Playing)
            {
                AudioQueuePause(_queue);
                PlaybackState = PlaybackState.Paused;
            }
        }
    }

    public void Stop()
    {
        bool wasPlaying;
        (IntPtr, GCHandle) old;
        lock (_lock)
        {
            wasPlaying = PlaybackState != PlaybackState.Stopped;
            old = DetachQueue();
        }

        DisposeQueue(old);
        if (wasPlaying)
        {
            PlaybackStopped?.Invoke(this, new StoppedEventArgs());
        }
    }

    public void Dispose()
    {
        (IntPtr, GCHandle) old;
        lock (_lock)
        {
            old = DetachQueue();
            _source = null;
        }

        DisposeQueue(old);
    }

    private void OpenQueue()
    {
        var format = _source!.WaveFormat;
        var description = AudioStreamBasicDescription.Float(format.SampleRate, format.Channels);
        _self = GCHandle.Alloc(this);
        Check(AudioQueueNewOutput(&description, &OnBufferFree, GCHandle.ToIntPtr(_self), IntPtr.Zero, IntPtr.Zero, 0, out _queue),
            "open the sound output");

        var bytesPerBuffer = format.AverageBytesPerSecond * _latencyMs / 1000 / BufferCount;
        bytesPerBuffer -= bytesPerBuffer % format.BlockAlign;
        _scratch = new byte[bytesPerBuffer];
        _ended = false;
        _buffersQueued = 0;
        for (var i = 0; i < BufferCount; i++)
        {
            Check(AudioQueueAllocateBuffer(_queue, (uint)bytesPerBuffer, out var buffer), "make a sound buffer");
            Fill(buffer);
        }
    }

    /// <summary>
    /// Takes the queue out of use. It's stopped and freed after the lock is let go (<see cref="DisposeQueue"/>):
    /// stopping waits for a refill that may be running, and that refill needs the lock.
    /// </summary>
    private (IntPtr Queue, GCHandle Self) DetachQueue()
    {
        var old = (_queue, _self);
        _queue = IntPtr.Zero;
        _self = default;
        PlaybackState = PlaybackState.Stopped;
        return old;
    }

    private static void DisposeQueue((IntPtr Queue, GCHandle Self) old)
    {
        if (old.Queue != IntPtr.Zero)
        {
            AudioQueueStop(old.Queue, 1);
            AudioQueueDispose(old.Queue, 1);
        }

        if (old.Self.IsAllocated)
        {
            old.Self.Free();
        }
    }

    /// <summary>Refills a buffer from the music and queues it, or leaves it out once the music has ended.</summary>
    private void Fill(AudioQueueBuffer* buffer)
    {
        var read = _ended ? 0 : _source!.Read(_scratch, 0, (int)Math.Min(buffer->AudioDataBytesCapacity, (uint)_scratch.Length));
        if (read <= 0)
        {
            _ended = true;
            return;
        }

        Marshal.Copy(_scratch, 0, (IntPtr)buffer->AudioData, read);
        buffer->AudioDataByteSize = (uint)read;
        if (AudioQueueEnqueueBuffer(_queue, buffer, 0, IntPtr.Zero) == 0)
        {
            _buffersQueued++;
        }
    }

    [UnmanagedCallersOnly]
    private static void OnBufferFree(IntPtr userData, IntPtr queue, AudioQueueBuffer* buffer)
    {
        if (GCHandle.FromIntPtr(userData).Target is not AudioQueueOutput output)
        {
            return;
        }

        var finished = false;
        lock (output._lock)
        {
            if (output._queue != queue)
            {
                return;
            }

            output._buffersQueued--;
            try
            {
                output.Fill(buffer);
            }
            catch (Exception)
            {
                // A decoding error part way through a file: end the song there, as Windows does.
                output._ended = true;
            }

            finished = output._ended && output._buffersQueued == 0;
        }

        if (finished)
        {
            // The last buffer has played. Stopping from Core Audio's own thread isn't allowed to wait,
            // so it's done on another one.
            Task.Run(output.Stop);
        }
    }
}
