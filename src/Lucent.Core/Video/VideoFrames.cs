using System.Runtime.InteropServices;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// The pictures of a video as libVLC decodes them, handed over one at a time to whatever shows them: 32-bit BGRA
/// (blue, green, red, then a byte that's always opaque), top row first. libVLC shows each at its moment, by the
/// same clock the video's sound is played to, so drawing the newest one keeps picture and sound together.
/// </summary>
/// <remarks>
/// There are two pictures: libVLC draws into one while the other is the newest finished one. Finishing a picture
/// swaps them. <see cref="CopyLatest"/> holds the newest still while it's read, so a picture is never torn.
/// </remarks>
public sealed class VideoFrames : IDisposable
{
    /// <summary>The biggest picture Lucent asks libVLC for; bigger videos are scaled down to fit.</summary>
    public const int MaxWidth = 3840;

    public const int MaxHeight = 2160;

    private readonly object _gate = new();
    private nint _drawing;
    private nint _finished;
    private int _bytes;
    private bool _disposed;

    /// <summary>The pictures' width in pixels, or 0 until the first one's size is known.</summary>
    public int Width { get; private set; }

    /// <summary>The pictures' height in pixels, or 0 until the first one's size is known.</summary>
    public int Height { get; private set; }

    /// <summary>Bytes from the start of one row to the next.</summary>
    public int Stride => Width * 4;

    /// <summary>Counts the finished pictures, so a viewer can tell when there's a new one.</summary>
    public long Count { get; private set; }

    /// <summary>
    /// Works out the size to ask libVLC for: the video's own size with its pixels made square (a widescreen DVD's
    /// are wider than tall), scaled down to fit <see cref="MaxWidth"/> × <see cref="MaxHeight"/>. Both even, which
    /// libVLC's scaler wants.
    /// </summary>
    public static (int Width, int Height) SizeFor(int width, int height, uint aspectNum, uint aspectDen)
    {
        if (width <= 0 || height <= 0)
        {
            return (0, 0);
        }

        double shown = width;
        if (aspectNum > 0 && aspectDen > 0 && aspectNum != aspectDen)
        {
            shown = width * (double)aspectNum / aspectDen;
        }

        var scale = Math.Min(1.0, Math.Min(MaxWidth / shown, MaxHeight / (double)height));
        var w = Math.Max(2, (int)Math.Round(shown * scale / 2) * 2);
        var h = Math.Max(2, (int)Math.Round(height * scale / 2) * 2);
        return (w, h);
    }

    /// <summary>Makes room for pictures of this size, when libVLC says what it'll send. Called from libVLC's thread.</summary>
    internal void SetSize(int width, int height)
    {
        lock (_gate)
        {
            Free();
            Width = width;
            Height = height;
            _bytes = width * height * 4;
            _drawing = Allocate(_bytes);
            _finished = Allocate(_bytes);
        }
    }

    /// <summary>Where libVLC draws the next picture. Called from libVLC's thread.</summary>
    internal nint BeginPicture()
    {
        lock (_gate)
        {
            return _drawing;
        }
    }

    /// <summary>The picture libVLC drew is done and its moment has come: it's the newest now. Called from libVLC's thread.</summary>
    internal void FinishPicture()
    {
        lock (_gate)
        {
            (_drawing, _finished) = (_finished, _drawing);
            Count++;
        }
    }

    /// <summary>
    /// Calls <paramref name="copy"/> with the newest picture (its pixels, width, height and stride) and returns true, or
    /// returns false when there isn't one yet. libVLC waits to finish its next picture until the copy returns.
    /// </summary>
    public bool CopyLatest(Action<nint, int, int, int> copy)
    {
        lock (_gate)
        {
            if (Count == 0 || _finished == 0)
            {
                return false;
            }

            copy(_finished, Width, Height, Stride);
            return true;
        }
    }

    /// <summary>The newest picture's pixels, copied out (for the self-test and screenshots), or null when there isn't one.</summary>
    public byte[]? Snapshot()
    {
        byte[]? pixels = null;
        CopyLatest((source, _, _, _) =>
        {
            pixels = new byte[_bytes];
            Marshal.Copy(source, pixels, 0, _bytes);
        });
        return pixels;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            Free();
        }
    }

    private nint Allocate(int bytes)
    {
        if (_disposed || bytes <= 0)
        {
            return 0;
        }

        // Black (and opaque) until libVLC draws in it. Rows are padded to 32 bytes past the end, as libVLC's
        // converters may write a little beyond the last pixel.
        var memory = Marshal.AllocHGlobal(bytes + 32);
        unsafe
        {
            var span = new Span<uint>((void*)memory, bytes / 4);
            span.Fill(0xFF000000);
        }

        return memory;
    }

    private void Free()
    {
        if (_drawing != 0)
        {
            Marshal.FreeHGlobal(_drawing);
        }

        if (_finished != 0)
        {
            Marshal.FreeHGlobal(_finished);
        }

        _drawing = 0;
        _finished = 0;
        Width = 0;
        Height = 0;
        _bytes = 0;
        Count = 0;
    }
}
