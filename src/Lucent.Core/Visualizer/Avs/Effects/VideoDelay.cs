namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Video Delay (Holden04's r_videodelay.cpp): shows the picture as it was a number of frames ago, or a number of beats' worth of frames ago.</summary>
[AvsEffect("Holden04: Video Delay")]
public sealed class VideoDelay : AvsEffect
{
    // Up to 1 GB of old pictures (AVS allowed 400 frames, which at 1080p would be over 3 GB).
    private const long BudgetBytes = 1L << 30;

    private readonly MiscDelayFrames _frames = new();
    private bool _enabled = true;
    private bool _useBeats;
    private int _delay = 10;
    private int _frameDelay = 10;
    private int _framesSinceBeat;
    private int _virtualSize;
    private int _oldVirtualSize;
    private int _oldFrameSize;
    private int _position;

    public VideoDelay(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        if (config.HasInt)
        {
            _enabled = config.ReadInt() == 1;
        }

        if (config.HasInt)
        {
            _useBeats = config.ReadInt() == 1;
        }

        if (config.HasInt)
        {
            _delay = Math.Min(config.ReadInt(), _useBeats ? 16 : 200);
        }

        // As in AVS, the frame count only took the setting when it was changed in the editor: a loaded
        // preset starts at 10 frames (and in beats mode, waits for a beat to measure).
    }

    public override void Restart()
    {
        _frames.Reset(0);
        _frameDelay = 10;
        _framesSinceBeat = 0;
        _virtualSize = 0;
        _oldVirtualSize = 0;
        _oldFrameSize = 0;
        _position = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var frameSize = context.Width * context.Height;
        if (_useBeats)
        {
            if (context.IsBeat)
            {
                _frameDelay = Math.Min(_framesSinceBeat * _delay, 400);
                _framesSinceBeat = 0;
            }

            _framesSinceBeat++;
        }

        if (!_enabled || _frameDelay <= 0)
        {
            return 0;
        }

        var maxFrames = MiscDelayFrames.MaxFrames(frameSize, BudgetBytes);
        if (_frameDelay > maxFrames)
        {
            Warn($"Video Delay's delay was shortened to {maxFrames} frames to keep the memory it uses reasonable.");
        }

        // Sizes are in frames where AVS counted bytes.
        _virtualSize = Math.Min(_frameDelay, maxFrames);
        if (frameSize == _oldFrameSize)
        {
            if (_virtualSize != _oldVirtualSize)
            {
                if (_virtualSize > _oldVirtualSize)
                {
                    if (_virtualSize > _frames.Count)
                    {
                        _frames.Reset(_useBeats ? Math.Min(2 * _virtualSize, 400) : _virtualSize);
                        _position = 0;
                    }
                    else
                    {
                        // Spread the waiting frames out to the new length, repeating the oldest.
                        var size = _oldVirtualSize - _position;
                        var to = _virtualSize - size;
                        _frames.Move(_position, to, size);
                        _frames.Fill(_position, to, to);
                    }
                }
                else
                {
                    var before = _position + 1;
                    if (before > _virtualSize)
                    {
                        _frames.Move(before - _virtualSize, 0, _virtualSize);
                        _position = _virtualSize - 1;
                    }
                    else if (before < _virtualSize)
                    {
                        _frames.Move(_oldVirtualSize + before - _virtualSize, _position + 1, _virtualSize - before);
                    }
                }

                _oldVirtualSize = _virtualSize;
            }
        }
        else
        {
            _frames.Reset(_useBeats ? 2 * _virtualSize : _virtualSize);
            _position = 0;
            _oldVirtualSize = _virtualSize;
        }

        _oldFrameSize = frameSize;
        _frames.CopyOut(_position, fbout, frameSize);
        _frames.CopyIn(_position, framebuffer, frameSize);
        _position++;
        if (_position >= _virtualSize)
        {
            _position = 0;
        }

        return Swapped;
    }
}
