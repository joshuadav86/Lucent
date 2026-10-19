using System.Diagnostics;
using Lucent.Mac.Interop;
using Lucent.Mac.Visuals;
using SharpMetal.Metal;
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac;

/// <summary>
/// How long the GPU takes to draw each visual at the size a visuals window has on a Retina screen,
/// so a visual far heavier than the rest shows up before someone notices it stutter. CI's GPUs are
/// slower than a real Mac's, so the times are compared with each other: the slowest are listed, and
/// any many times slower than the typical visual is flagged as a warning.
/// </summary>
public static partial class SelfTest
{
    // About a visuals window of 1000 x 640 points on a Retina screen.
    private const int TimingWidth = 2000;
    private const int TimingHeight = 1280;
    private const int TimingFrames = 5;
    private const int SlowestListed = 15;
    private const double HeavyTimesTypical = 10.0;

    // A visual this slow on CI's GPU gets fewer timed frames, and the whole pass stops after this long.
    private static readonly TimeSpan TimingPerVisual = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan TimingBudget = TimeSpan.FromMinutes(10);

    private static void TimeVisuals(MTLDevice device, VisualEngine engine, AudioCapture capture, MadeUpMusic music, ref double now)
    {
        var description = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, TimingWidth, TimingHeight, false);
        description.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
        description.StorageMode = MTLStorageMode.Private;
        var output = device.NewTexture(description);

        var times = new List<(string Name, double Milliseconds)>();
        var budget = Stopwatch.StartNew();
        for (var index = 0; index < engine.Presets.Count; index++)
        {
            if (!engine.IsReady(index))
            {
                continue;
            }

            if (budget.Elapsed > TimingBudget)
            {
                Console.WriteLine($"::warning::Timed {times.Count} of the visuals; the rest ran out of time on this GPU.");
                break;
            }

            engine.PresetIndex = index;
            // One frame starts the switch, a jump past the transition finishes it, and a few more
            // settle the visual's own feedback and particles before the timed frames.
            Draw(engine, output, capture, music, ref now).WaitUntilCompleted();
            now += 1.0;
            for (var i = 0; i < 3; i++)
            {
                Draw(engine, output, capture, music, ref now).WaitUntilCompleted();
            }

            var frames = new List<double>();
            var spent = Stopwatch.StartNew();
            while (frames.Count < TimingFrames && (frames.Count == 0 || spent.Elapsed < TimingPerVisual))
            {
                var commands = Draw(engine, output, capture, music, ref now);
                commands.WaitUntilCompleted();
                if (commands.Status == MTLCommandBufferStatus.Error)
                {
                    break;
                }

                var seconds = ObjC.SendDouble(commands.NativePtr, "GPUEndTime") - ObjC.SendDouble(commands.NativePtr, "GPUStartTime");
                if (seconds > 0)
                {
                    frames.Add(seconds * 1000.0);
                }
            }

            if (frames.Count > 0)
            {
                frames.Sort();
                times.Add((engine.Presets[index].Name, frames[frames.Count / 2]));
            }
        }

        output.Dispose();
        if (times.Count == 0)
        {
            Console.WriteLine("::warning::This GPU didn't report how long the visuals took to draw.");
            return;
        }

        var sorted = times.Select(time => time.Milliseconds).OrderBy(ms => ms).ToList();
        var typical = sorted[sorted.Count / 2];
        Console.WriteLine($"GPU time per frame at {TimingWidth} x {TimingHeight}: typical visual {typical:0.0} ms. Slowest:");
        foreach (var (name, ms) in times.OrderByDescending(time => time.Milliseconds).Take(SlowestListed))
        {
            Console.WriteLine($"  {ms,8:0.0} ms  {ms / typical,5:0.0}x  {name}");
        }

        var heavy = times.Where(time => time.Milliseconds > typical * HeavyTimesTypical).OrderByDescending(time => time.Milliseconds).ToList();
        if (heavy.Count > 0)
        {
            Console.WriteLine($"::warning::More than {HeavyTimesTypical:0} times slower to draw than the typical visual, so likely to stutter on a Retina screen: "
                + string.Join(", ", heavy.Select(time => $"{time.Name} ({time.Milliseconds / typical:0}x)")));
        }
    }

    private static MTLCommandBuffer Draw(VisualEngine engine, MTLTexture output, AudioCapture capture, MadeUpMusic music, ref double now)
    {
        using var pool = AutoreleasePool.Begin();
        capture.Write(music.Next((int)(SampleRate * FrameSeconds)));
        now += FrameSeconds;
        var commands = engine.EncodeFrame(output, now, (float)FrameSeconds);
        commands.Commit();
        return commands;
    }
}
