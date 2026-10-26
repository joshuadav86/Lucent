using System.Runtime.CompilerServices;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Multi Delay (Holden05's r_multidelay.cpp): six delay buffers shared by every Multi Delay in the
/// preset. Each Multi Delay either puts the picture into one of them or takes from it the picture of a set
/// number of frames (or one beat) ago; the buffers' delays are the ones the first Multi Delay saved.
/// </summary>
[AvsEffect("Holden05: Multi Delay")]
public sealed class MultiDelay : AvsEffect
{
    private const int BufferCount = 6;

    // Up to 512 MB of old pictures for each buffer: AVS's windows were small, Lucent's pictures aren't.
    private const long BudgetBytes = 512L << 20;

    // AVS kept the buffers in globals, shared by every Multi Delay; here each running preset has its own.
    private static readonly ConditionalWeakTable<AvsShared, SharedState> States = new();

    private readonly SharedState _state;
    private int _mode;
    private int _activeBuffer;

    public MultiDelay(AvsShared shared)
        : base(shared)
    {
        _state = States.GetValue(shared, _ => new SharedState());
        _state.Instances++;
    }

    public override void Load(AvsConfigReader config)
    {
        var state = _state;
        _mode = config.ReadInt(_mode);
        _activeBuffer = config.ReadInt(_activeBuffer);
        if (!config.HasInt)
        {
            return;
        }

        // Only the first Multi Delay saves the buffers' settings; any that do set them for all.
        for (var i = 0; i < BufferCount; i++)
        {
            if (config.HasInt)
            {
                state.UseBeats[i] = config.ReadInt() == 1;
            }

            if (config.HasInt)
            {
                state.Delay[i] = Math.Max(0, config.ReadInt());
                state.FrameDelay[i] = (state.UseBeats[i] ? state.FramesPerBeat : state.Delay[i]) + 1;
            }
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var state = _state;
        var frameSize = context.Width * context.Height;

        // The first Multi Delay to draw each frame looks after the buffers; the last moves them on.
        if (state.RenderId >= state.Instances)
        {
            state.RenderId = 0;
        }

        state.RenderId++;
        if (state.RenderId == 1)
        {
            state.Update(this, context.IsBeat, frameSize);
        }

        var active = Math.Clamp(_activeBuffer, 0, BufferCount - 1);
        if (_mode != 0 && state.FrameDelay[active] > 1)
        {
            if (_mode == 2)
            {
                state.Frames[active].CopyOut(state.OutPosition[active], framebuffer, frameSize);
            }
            else
            {
                state.Frames[active].CopyIn(state.InPosition[active], framebuffer, frameSize);
            }
        }

        if (state.RenderId == state.Instances)
        {
            for (var i = 0; i < BufferCount; i++)
            {
                if (++state.InPosition[i] >= state.VirtualSize[i])
                {
                    state.InPosition[i] = 0;
                }

                if (++state.OutPosition[i] >= state.VirtualSize[i])
                {
                    state.OutPosition[i] = 0;
                }
            }
        }

        return 0;
    }

    /// <summary>What AVS kept in globals for all the Multi Delays. Sizes are in frames where AVS counted bytes.</summary>
    private sealed class SharedState
    {
        public int Instances;
        public int RenderId;
        public int FramesSinceBeat;
        public int FramesPerBeat;
        public int OldFrameSize;
        public readonly bool[] UseBeats = new bool[BufferCount];
        public readonly int[] Delay = new int[BufferCount];
        public readonly int[] FrameDelay = new int[BufferCount];
        public readonly int[] VirtualSize = new int[BufferCount];
        public readonly int[] OldVirtualSize = new int[BufferCount];
        public readonly int[] InPosition = new int[BufferCount];
        public readonly int[] OutPosition = new int[BufferCount];
        public readonly MiscDelayFrames[] Frames = Enumerable.Range(0, BufferCount).Select(_ => new MiscDelayFrames()).ToArray();

        public void Update(MultiDelay effect, bool isBeat, int frameSize)
        {
            if (isBeat)
            {
                FramesPerBeat = FramesSinceBeat;
                for (var i = 0; i < BufferCount; i++)
                {
                    if (UseBeats[i])
                    {
                        FrameDelay[i] = FramesPerBeat + 1;
                    }
                }

                FramesSinceBeat = 0;
            }

            FramesSinceBeat++;
            var maxFrames = MiscDelayFrames.MaxFrames(frameSize, BudgetBytes);
            for (var i = 0; i < BufferCount; i++)
            {
                if (FrameDelay[i] <= 1)
                {
                    continue;
                }

                if (FrameDelay[i] > maxFrames)
                {
                    effect.Warn($"Multi Delay's delays were shortened to {maxFrames} frames to keep the memory they use reasonable.");
                }

                var size = Math.Min(FrameDelay[i], maxFrames);
                VirtualSize[i] = size;
                var frames = Frames[i];
                if (frameSize == OldFrameSize)
                {
                    var oldSize = OldVirtualSize[i];
                    if (size != oldSize)
                    {
                        if (size > oldSize)
                        {
                            if (size > frames.Count)
                            {
                                frames.Reset(UseBeats[i] ? 2 * size : size);
                                OutPosition[i] = 0;
                                InPosition[i] = size - 1;
                            }
                            else
                            {
                                // Spread the waiting frames out to the new length, repeating the oldest.
                                var waiting = oldSize - OutPosition[i];
                                var to = size - waiting;
                                frames.Move(OutPosition[i], to, waiting);
                                frames.Fill(OutPosition[i], to, to);
                            }
                        }
                        else
                        {
                            var before = OutPosition[i];
                            if (before > size)
                            {
                                frames.Move(before - size, 0, size);
                                InPosition[i] = size - 1;
                                OutPosition[i] = 0;
                            }
                            else if (before < size)
                            {
                                frames.Move(oldSize + before - size, OutPosition[i], size - before);
                            }
                        }

                        OldVirtualSize[i] = size;
                    }
                }
                else
                {
                    frames.Reset(UseBeats[i] ? 2 * size : size);
                    OutPosition[i] = 0;
                    InPosition[i] = size - 1;
                    OldVirtualSize[i] = size;
                }

                OldFrameSize = frameSize;
            }
        }
    }
}
