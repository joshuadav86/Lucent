using System.Diagnostics;
using System.Runtime.InteropServices;
using Lucent.Mac.Interop;
using Lucent.Mac.Visuals;
using NAudio.Wave;
using SharpMetal.Metal;
using SkiaSharp;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// Checks the Mac app works without a window, for CI on a Mac: decodes the given music files with
/// Core Audio, then compiles every visual's Metal shaders and draws a few frames of each to music
/// made up on the spot, and can save a contact sheet of what they look like.
/// </summary>
/// <remarks>Usage: Lucent --self-test [--sheet picture.png] [music files...]</remarks>
public static class SelfTest
{
    private const int Width = 320;
    private const int Height = 180;
    private const int FramesPerVisual = 45;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int SampleRate = 44100;

    public static int Run(string[] args)
    {
        string? sheetPath = null;
        var music = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--sheet" && i + 1 < args.Length)
            {
                sheetPath = args[++i];
            }
            else
            {
                music.Add(args[i]);
            }
        }

        var failures = 0;
        foreach (var path in music)
        {
            failures += TestDecoding(path) ? 0 : 1;
        }

        failures += TestVisuals(sheetPath);
        Console.WriteLine(failures == 0 ? "Self-test passed." : $"Self-test failed: {failures} problem(s).");
        return failures == 0 ? 0 : 1;
    }

    private static bool TestDecoding(string path)
    {
        try
        {
            using var reader = AudioReaderFactory.Open(path);
            var samples = reader.ToSampleProvider();
            var buffer = new float[samples.WaveFormat.SampleRate * samples.WaveFormat.Channels];
            long total = 0;
            var peak = 0f;
            int read;
            while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
            {
                total += read;
                for (var i = 0; i < read; i++)
                {
                    peak = Math.Max(peak, Math.Abs(buffer[i]));
                }
            }

            var seconds = (double)total / samples.WaveFormat.Channels / samples.WaveFormat.SampleRate;
            Console.WriteLine($"Decoded {Path.GetFileName(path)}: {samples.WaveFormat.SampleRate} Hz, {samples.WaveFormat.Channels} channel(s), {seconds:0.00} s, peak {peak:0.00}.");
            if (total == 0 || peak < 0.01f)
            {
                Console.WriteLine($"::error::{Path.GetFileName(path)} decoded to silence.");
                return false;
            }

            // Seeking back to the start has to work too: the player's position slider uses it.
            reader.Position = 0;
            if (samples.Read(buffer, 0, buffer.Length) <= 0)
            {
                Console.WriteLine($"::error::{Path.GetFileName(path)} couldn't be read again after seeking back to the start.");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error::Couldn't decode {Path.GetFileName(path)}: {ex.Message}");
            return false;
        }
    }

    private static int TestVisuals(string? sheetPath)
    {
        var device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == IntPtr.Zero)
        {
            Console.WriteLine("::warning::This machine has no Metal device, so the visuals weren't drawn.");
            return 0;
        }

        Console.WriteLine($"Metal device: {StringHelper.Read(device.Name)}");
        var capture = new AudioCapture();
        capture.Reset(SampleRate);
        var files = new MetalShaderFiles();
        using var engine = new VisualEngine(device, new AudioAnalyzer(capture), files, MTLPixelFormat.BGRA8Unorm)
        {
            AutoCycle = false,
            TransitionSeconds = 0.25f,
        };
        var failed = new List<string>();
        engine.PresetFailed += message =>
        {
            lock (failed)
            {
                failed.Add(message);
            }
        };

        if (engine.Presets.Count == 0)
        {
            Console.WriteLine("::error::No visuals were found next to the app.");
            return 1;
        }

        var outputDescription = MTLTextureDescriptor.Texture2DDescriptor(MTLPixelFormat.BGRA8Unorm, Width, Height, false);
        outputDescription.Usage = MTLTextureUsage.RenderTarget | MTLTextureUsage.ShaderRead;
        var canRead = device.HasUnifiedMemory;
        outputDescription.StorageMode = canRead ? MTLStorageMode.Shared : MTLStorageMode.Private;
        var output = device.NewTexture(outputDescription);

        var columns = (int)Math.Ceiling(Math.Sqrt(engine.Presets.Count * 16.0 / 9.0));
        var rows = (engine.Presets.Count + columns - 1) / columns;
        using var sheet = canRead && sheetPath is not null ? new SKBitmap(columns * Width, rows * Height) : null;
        var pixels = new byte[Width * Height * 4];
        var dark = new List<string>();
        var drawingErrors = 0;
        var now = 0.0;
        var music = new MadeUpMusic();
        var stopwatch = Stopwatch.StartNew();

        engine.StartCompiling();
        for (var index = 0; index < engine.Presets.Count; index++)
        {
            var name = engine.Presets[index].Name;
            engine.PresetIndex = index;

            // Draw until this visual has compiled and been on screen for a while (or it failed).
            var drawn = 0;
            var waitedFor = Stopwatch.StartNew();
            MTLCommandBuffer? last = null;
            while (drawn < FramesPerVisual && !engine.HasFailed(index) && waitedFor.Elapsed < TimeSpan.FromSeconds(120))
            {
                using var pool = AutoreleasePool.Begin();
                capture.Write(music.Next((int)(SampleRate * FrameSeconds)));
                now += FrameSeconds;
                var commands = engine.EncodeFrame(output, now, (float)FrameSeconds);
                commands.Commit();
                if (engine.ActivePreset == index)
                {
                    drawn++;
                    if (drawn == FramesPerVisual)
                    {
                        commands.WaitUntilCompleted();
                        last = commands;
                        if (commands.Status == MTLCommandBufferStatus.Error)
                        {
                            drawingErrors++;
                            Console.WriteLine($"::error::{name}: the GPU reported an error drawing it: {StringHelper.Read(commands.Error.LocalizedDescription)}");
                        }
                    }
                }
                else if (!engine.IsReady(index))
                {
                    // Still compiling: don't spin flat out while waiting.
                    commands.WaitUntilCompleted();
                    Thread.Sleep(5);
                }
            }

            if (engine.HasFailed(index))
            {
                continue;
            }

            if (last is null)
            {
                drawingErrors++;
                Console.WriteLine($"::error::{name} never came on screen.");
                continue;
            }

            if (!canRead)
            {
                continue;
            }

            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                output.GetBytes(handle.AddrOfPinnedObject(), Width * 4,
                    new MTLRegion { origin = new MTLOrigin(), size = new MTLSize { width = Width, height = Height, depth = 1 } }, 0);
            }
            finally
            {
                handle.Free();
            }

            long sum = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
            }

            var brightness = sum / (Width * Height * 3.0 * 255.0);
            if (brightness < 0.004)
            {
                dark.Add(name);
            }

            if (sheet is not null)
            {
                CopyToSheet(sheet, pixels, index % columns * Width, index / columns * Height);
            }
        }

        output.Dispose();
        var compiled = engine.Presets.Count - failed.Count;
        Console.WriteLine($"Drew {compiled} of {engine.Presets.Count} visuals in {stopwatch.Elapsed.TotalSeconds:0} s.");
        foreach (var message in failed)
        {
            Console.WriteLine($"::error::{message}");
        }

        if (dark.Count > 0)
        {
            Console.WriteLine($"::warning::Black after {FramesPerVisual} frames (may be fine for slow starters): {string.Join(", ", dark)}");
        }

        if (sheet is not null)
        {
            using var file = File.Create(sheetPath!);
            sheet.Encode(file, SKEncodedImageFormat.Png, 100);
            Console.WriteLine($"Saved a contact sheet of every visual to {sheetPath}.");
        }

        return failed.Count + drawingErrors;
    }

    /// <summary>Copies one visual's BGRA picture into its place on the contact sheet.</summary>
    private static void CopyToSheet(SKBitmap sheet, byte[] bgra, int left, int top)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 4;
                sheet.SetPixel(left + x, top + y, new SKColor(bgra[i + 2], bgra[i + 1], bgra[i], 255));
            }
        }
    }

    /// <summary>A kick drum on every beat at 120 bpm, a bass line and a shimmer of treble, so every visual has something to move to.</summary>
    private sealed class MadeUpMusic
    {
        private long _sample;

        public float[] Next(int count)
        {
            var samples = new float[count];
            for (var i = 0; i < count; i++, _sample++)
            {
                var t = (double)_sample / SampleRate;
                var sinceBeat = t % 0.5;
                var kick = Math.Sin(2 * Math.PI * (50 + 80 * Math.Exp(-sinceBeat * 30)) * sinceBeat) * Math.Exp(-sinceBeat * 8);
                var bass = 0.3 * Math.Sin(2 * Math.PI * (t % 2 < 1 ? 55 : 73.4) * t);
                var treble = 0.08 * Math.Sin(2 * Math.PI * 5000 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t));
                samples[i] = (float)(0.6 * kick + bass + treble);
            }

            return samples;
        }
    }
}
