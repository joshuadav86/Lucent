using Lucent.Mac.Interop;
using SharpMetal.Metal;

namespace Lucent.Mac.Export;

/// <summary>
/// Brings finished frames back from the graphics card for the video encoder, the Mac's match for the
/// Windows app's VideoFrameConverter (VideoToolbox turns BGRA into the encoder's YUV itself, so there's
/// nothing to convert). Each frame is copied into a buffer the processor can read, at the end of the
/// frame's own command buffer, and a few frames are kept in flight, so the graphics card is never waited
/// on: a frame is read back only once the next couple have been handed in, by which time it's long done.
/// </summary>
public sealed unsafe class VideoFrameReader : IDisposable
{
    /// <summary>How many frames can be in flight before the oldest has to be read back.</summary>
    public const int Depth = 3;

    private readonly int _width;
    private readonly int _height;
    private readonly int _bytesPerRow;
    private readonly MTLBuffer[] _copies = new MTLBuffer[Depth];
    private readonly IntPtr[] _commands = new IntPtr[Depth];
    private int _oldest;
    private int _pending;

    /// <summary>BGRA frames of <paramref name="width"/> x <paramref name="height"/> drawn with <paramref name="device"/>.</summary>
    public VideoFrameReader(MTLDevice device, int width, int height)
    {
        _width = width;
        _height = height;
        _bytesPerRow = width * 4;
        for (var i = 0; i < Depth; i++)
        {
            // Shared memory works on every Mac's graphics, built in or separate.
            _copies[i] = device.NewBuffer((ulong)_bytesPerRow * (ulong)height, MTLResourceOptions.ResourceStorageModeShared);
            if (_copies[i].NativePtr == IntPtr.Zero)
            {
                throw new OutOfMemoryException("There isn't enough memory to record a video this size.");
            }
        }
    }

    /// <summary>How many frames have been handed in and not read back yet.</summary>
    public int Pending => _pending;

    /// <summary>
    /// Adds a copy of <paramref name="frame"/> to the end of <paramref name="commands"/> (the frame's own,
    /// not yet committed) and commits them. Read the oldest back first if <see cref="Depth"/> are pending.
    /// </summary>
    public void Submit(MTLCommandBuffer commands, MTLTexture frame)
    {
        if (_pending == Depth)
        {
            throw new InvalidOperationException("Read the oldest frame back first.");
        }

        var slot = (_oldest + _pending) % Depth;
        var blit = commands.BlitCommandEncoder();
        blit.CopyFromTexture(frame, 0, 0, new MTLOrigin(),
            new MTLSize { width = (ulong)_width, height = (ulong)_height, depth = 1 },
            _copies[slot], 0, (ulong)_bytesPerRow, (ulong)_bytesPerRow * (ulong)_height);
        blit.EndEncoding();
        _commands[slot] = ObjC.Send(commands.NativePtr, "retain");
        commands.Commit();
        _pending++;
    }

    /// <summary>
    /// Waits for the oldest frame handed in (normally done long ago) and copies it, top row first, to
    /// <paramref name="destination"/>, whose rows are <paramref name="destinationBytesPerRow"/> bytes apart.
    /// </summary>
    public void ReadOldest(IntPtr destination, int destinationBytesPerRow)
    {
        if (_pending == 0)
        {
            throw new InvalidOperationException("There's no frame to read back.");
        }

        var slot = _oldest;
        var commands = new MTLCommandBuffer(_commands[slot]);
        try
        {
            commands.WaitUntilCompleted();
            if (commands.Status == MTLCommandBufferStatus.Error)
            {
                throw new InvalidOperationException(
                    $"The graphics card couldn't draw a frame. {StringHelper.Read(commands.Error.LocalizedDescription)}");
            }
        }
        finally
        {
            ObjC.Release(_commands[slot]);
            _commands[slot] = IntPtr.Zero;
            _oldest = (slot + 1) % Depth;
            _pending--;
        }

        var source = (byte*)_copies[slot].Contents;
        var target = (byte*)destination;
        if (destinationBytesPerRow == _bytesPerRow)
        {
            Buffer.MemoryCopy(source, target, (long)_bytesPerRow * _height, (long)_bytesPerRow * _height);
            return;
        }

        // The pixel buffer's rows are padded: copy them one at a time.
        var length = Math.Min(_bytesPerRow, destinationBytesPerRow);
        for (var y = 0; y < _height; y++)
        {
            Buffer.MemoryCopy(source + (long)y * _bytesPerRow, target + (long)y * destinationBytesPerRow, length, length);
        }
    }

    public void Dispose()
    {
        for (var i = 0; i < Depth; i++)
        {
            if (_commands[i] != IntPtr.Zero)
            {
                // Still on the graphics card: let it finish with the buffer first.
                new MTLCommandBuffer(_commands[i]).WaitUntilCompleted();
                ObjC.Release(_commands[i]);
                _commands[i] = IntPtr.Zero;
            }

            if (_copies[i].NativePtr != IntPtr.Zero)
            {
                _copies[i].Dispose();
            }
        }

        _pending = 0;
    }
}
