using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NAudio.Wave;
using Vortice.Direct3D11;
using Vortice.DXGI;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace WinampInspiredVisualizer;

/// <summary>
/// Checks the Windows app works without a window, for CI: decodes the given music files with the
/// readers the player uses, then compiles every visual (the built-in ones and the MilkDrop and AVS presets)
/// and draws a few frames of each to music made up on the spot, off screen as recording a video does,
/// and can save a contact sheet of what they look like. On a machine with no graphics card, as CI's
/// Windows machines are, Direct3D draws in software: slowly, but the same pictures.
/// </summary>
/// <remarks>
/// Usage: Lucent.exe --self-test [--sheet picture.png] [music files...]. Lucent.exe is a windowed program,
/// so nothing waits for it or shows what it writes unless its output is piped somewhere: in PowerShell,
/// <c>Lucent.exe --self-test | Out-Host</c> waits, shows it, and sets $LASTEXITCODE (1 if anything failed).
/// </remarks>
public static class SelfTest
{
    private const int Width = 320;
    private const int Height = 180;
    private const int FramesPerVisual = 45;

    // A visual still black after FramesPerVisual frames keeps drawing this much longer before it
    // counts as black: some take a while to build up, or have quiet moments (Sand Yantra sweeps its
    // mandala away and pours it out again).
    private const int SlowStarterFrames = 12 * 60;
    private const double DarkBrightness = 0.004;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int SampleRate = 44100;
    private const int StdOutputHandle = -11;
    private const int AttachParentProcess = -1;

    // How long a visual gets to show itself. Drawing in software is far slower than on a graphics card,
    // so a heavy visual may not reach FramesPerVisual in this time; that's a warning, not a failure.
    private static readonly TimeSpan TimePerVisual = TimeSpan.FromSeconds(60);

    // The formats NAudio reads itself. Everything else, MP3 included, goes through Media Foundation.
    private static readonly HashSet<string> ReadWithoutMediaFoundation = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".aif", ".aiff", ".ogg", ".oga", ".opus",
    };

    public static int Run(string[] args)
    {
        ShowOutput();
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
        failures += TestOtherApps();
        failures += Editor.Stems.StemSelfTest.Run(Console.WriteLine);
        Console.WriteLine(failures == 0 ? "Self-test passed." : $"Self-test failed: {failures} problem(s).");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Sends what the self-test writes somewhere it can be seen. A windowed program starts with nowhere
    /// to write to, unless its output was piped (as CI does); run from a command prompt instead, it
    /// writes to that prompt's window.
    /// </summary>
    private static void ShowOutput()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1))
        {
            AttachConsole(AttachParentProcess);
        }

        Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
    }

    // Listening to other apps: the capture takes their sound and leaves the player's out until it's done,
    // then listening starts and stops for real (a warning, not a failure, where the machine has no sound to hear).
    private static int TestOtherApps()
    {
        var problems = 0;
        var capture = new AudioCapture();
        capture.Reset(44100);
        capture.BeginExternal(48000);
        capture.Reset(96000);
        capture.Write(new float[] { 1f, 1f, 1f, 1f });
        capture.WriteExternal(new float[] { 0.5f, 0.5f, 0.5f, 0.5f }, new float[] { 0.5f, 0.5f, 0.5f, 0.5f }, new float[] { 0.5f, 0.5f, 0.5f, 0.5f });
        // Reads glide through the newest block at the music's speed, so wait for it to have "played".
        Thread.Sleep(50);
        var heard = new float[4];
        capture.ReadLatest(heard, capture.DelaySamples);
        if (capture.SampleRate != 48000 || capture.DelaySamples != 0 || heard.Any(sample => sample != 0.5f && sample != 0f) || !heard.Contains(0.5f))
        {
            problems++;
            Console.WriteLine($"::error::Listening to other apps mixed in the player's sound ({string.Join(", ", heard)} at {capture.SampleRate} Hz).");
        }

        capture.EndExternal();
        if (capture.SampleRate != 96000 || capture.IsExternal)
        {
            problems++;
            Console.WriteLine($"::error::After listening to other apps, the capture went back to {capture.SampleRate} Hz instead of the player's 96000 Hz.");
        }

        try
        {
            using (SystemAudio.Start(capture))
            {
                Thread.Sleep(300);
            }

            Console.WriteLine($"Listened to other apps' sound on {SystemAudio.DefaultOutputName() ?? "the default output"}.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::warning::Couldn't listen to other apps here: {ex.Message}");
        }

        if (capture.IsExternal)
        {
            problems++;
            Console.WriteLine("::error::The capture was still listening to other apps after they were let go.");
        }

        return problems;
    }

    private static bool TestDecoding(string path)
    {
        // Windows Server can leave Media Foundation out, and it's what reads everything but WAV, AIFF and Ogg.
        if (!ReadWithoutMediaFoundation.Contains(Path.GetExtension(path)) && !NativeLibrary.TryLoad("mfplat.dll", out _))
        {
            Console.WriteLine($"::warning::This machine has no Media Foundation, so {Path.GetFileName(path)} wasn't decoded.");
            return true;
        }

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
        var presets = PresetLibrary.LoadPresets();
        if (presets.Count == 0)
        {
            Console.WriteLine("::error::No visuals were found next to the app.");
            return 1;
        }

        var analyzer = new AudioAnalyzer();
        using var renderer = Renderer.CreateOffscreen(analyzer, presets);
        renderer.AutoCycle = false;
        renderer.TransitionSeconds = 0.25f;
        var failed = new List<string>();
        renderer.PresetFailed += message =>
        {
            lock (failed)
            {
                failed.Add(message);
            }
        };

        // Compiles every visual the way a video recording does, falling back to drawing in software
        // when there's no graphics card.
        var stopwatch = Stopwatch.StartNew();
        try
        {
            renderer.OpenOffscreen(Width, Height, Enumerable.Range(0, presets.Count).ToList(), null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            foreach (var message in failed)
            {
                Console.WriteLine($"::error::{message}");
            }

            Console.WriteLine($"::error::The visuals couldn't be drawn: {ex.Message}");
            return Math.Max(1, failed.Count);
        }

        Console.WriteLine($"Graphics device: {AdapterName(renderer.Device)}");
        Console.WriteLine($"Compiled {presets.Count - failed.Count} of {presets.Count} visuals in {stopwatch.Elapsed.TotalSeconds:0} s.");

        // Each frame is copied back from the graphics card to see how bright it is.
        using var copy = renderer.Device.CreateTexture2D(
            Format.B8G8R8A8_UNorm, Width, Height, 1, 1, null,
            BindFlags.None, ResourceOptionFlags.None, ResourceUsage.Staging, CpuAccessFlags.Read);
        var columns = (int)Math.Ceiling(Math.Sqrt(presets.Count * 16.0 / 9.0));
        var rows = (presets.Count + columns - 1) / columns;
        var sheet = sheetPath is not null ? new byte[columns * Width * rows * Height * 4] : null;
        var pixels = new byte[Width * Height * 4];
        var dark = new List<string>();
        var slow = new List<string>();
        var heavy = new List<string>();
        var drawingErrors = 0;
        var drew = 0;
        var now = 0.0;
        var music = new MadeUpMusic();
        stopwatch.Restart();

        for (var index = 0; index < presets.Count; index++)
        {
            if (!renderer.IsReady(index))
            {
                // It didn't compile: PresetFailed has said why.
                continue;
            }

            var name = presets[index].Name;
            renderer.PresetIndex = index;

            // Draw until this visual has been on screen for a while (the change to it takes a quarter
            // of a second). One that's still black then keeps going a while longer, in case it's just a
            // slow starter. Waiting for each frame keeps the time limit honest.
            var drawn = 0;
            var brightness = 0.0;
            var waitedFor = Stopwatch.StartNew();
            try
            {
                while (!renderer.HasFailed(index))
                {
                    analyzer.Update((float)FrameSeconds, music.Next((int)(SampleRate * FrameSeconds)), SampleRate);
                    now += FrameSeconds;
                    var frame = renderer.DrawOffscreen(now, (float)FrameSeconds, now);
                    brightness = Brightness(renderer.Context, frame, copy, pixels);
                    drawn++;
                    if ((drawn >= FramesPerVisual && brightness >= DarkBrightness)
                        || drawn >= FramesPerVisual + SlowStarterFrames || waitedFor.Elapsed > TimePerVisual)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                drawingErrors++;
                Console.WriteLine($"::error::{name}: drawing it failed: {ex.Message}");
            }

            var removed = renderer.Device.DeviceRemovedReason;
            if (removed.Failure)
            {
                // Nothing more can be drawn on a device that's gone.
                drawingErrors++;
                Console.WriteLine($"::error::The graphics device stopped while drawing {name} ({removed}), so the visuals after it weren't drawn.");
                break;
            }

            if (renderer.HasFailed(index) || drawn == 0)
            {
                // A MilkDrop or AVS preset that went wrong while drawing: PresetFailed has said why.
                continue;
            }

            drew++;
            if (drawn < FramesPerVisual)
            {
                heavy.Add($"{name} ({drawn} frames in {waitedFor.Elapsed.TotalSeconds:0} s)");
            }

            if (brightness < DarkBrightness)
            {
                dark.Add(name);
            }
            else if (drawn > FramesPerVisual)
            {
                slow.Add($"{name} ({drawn / 60.0:0.0} s)");
            }

            if (sheet is not null)
            {
                CopyToSheet(sheet, columns * Width, pixels, index % columns * Width, index / columns * Height);
            }
        }

        Console.WriteLine($"Drew {drew} of {presets.Count} visuals in {stopwatch.Elapsed.TotalSeconds:0} s.");
        foreach (var message in failed)
        {
            Console.WriteLine($"::error::{message}");
        }

        if (slow.Count > 0)
        {
            Console.WriteLine($"Slow starters, black after {FramesPerVisual} frames but drawing later: {string.Join(", ", slow)}");
        }

        if (heavy.Count > 0)
        {
            Console.WriteLine($"::warning::Drew too slowly on this machine to reach {FramesPerVisual} frames: {string.Join(", ", heavy)}");
        }

        if (dark.Count > 0)
        {
            Console.WriteLine($"::warning::Still black after {(FramesPerVisual + SlowStarterFrames) / 60} seconds (or the time limit): {string.Join(", ", dark)}");
        }

        if (sheet is not null)
        {
            SaveSheet(sheetPath!, sheet, columns * Width, rows * Height);
            Console.WriteLine($"Saved a contact sheet of every visual to {sheetPath}.");
        }

        return failed.Count + drawingErrors;
    }

    /// <summary>The name of the graphics card the device draws with ("Microsoft Basic Render Driver" in software).</summary>
    private static string AdapterName(ID3D11Device device)
    {
        try
        {
            using var dxgiDevice = device.QueryInterface<IDXGIDevice>();
            using var adapter = dxgiDevice.GetAdapter();
            return adapter.Description.Description;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }

    /// <summary>
    /// The frame's average brightness, 0 to 1, leaving its BGRA pixels in <paramref name="pixels"/>.
    /// Reading it back waits for the graphics card to finish drawing it.
    /// </summary>
    private static double Brightness(ID3D11DeviceContext context, ID3D11Texture2D frame, ID3D11Texture2D copy, byte[] pixels)
    {
        context.CopyResource(copy, frame);
        var mapped = context.Map<byte>(copy, 0, 0, MapMode.Read);
        try
        {
            // The graphics card pads each row; the pixels are kept packed.
            var pitch = mapped.Length / Height;
            for (var row = 0; row < Height; row++)
            {
                mapped.Slice(row * pitch, Width * 4).CopyTo(pixels.AsSpan(row * Width * 4, Width * 4));
            }
        }
        finally
        {
            context.Unmap(copy, 0);
        }

        long sum = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
        }

        return sum / (Width * Height * 3.0 * 255.0);
    }

    /// <summary>Copies one visual's BGRA picture into its place on the contact sheet, <paramref name="sheetWidth"/> pixels across.</summary>
    private static void CopyToSheet(byte[] sheet, int sheetWidth, byte[] bgra, int left, int top)
    {
        for (var y = 0; y < Height; y++)
        {
            Array.Copy(bgra, y * Width * 4, sheet, ((top + y) * sheetWidth + left) * 4, Width * 4);
        }
    }

    private static void SaveSheet(string path, byte[] sheet, int width, int height)
    {
        // Bgr32 leaves out alpha, which the visuals don't set to anything meaningful.
        var picture = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, sheet, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(picture));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    /// <summary>A kick drum on every beat at 120 bpm, a bass line and a shimmer of treble, so every visual has something to move to.</summary>
    private sealed class MadeUpMusic
    {
        private readonly float[] _latest = new float[AudioAnalyzer.WindowSamples];
        private long _sample;

        /// <summary>Moves on by <paramref name="count"/> samples and returns the newest <see cref="AudioAnalyzer.WindowSamples"/>.</summary>
        public float[] Next(int count)
        {
            Array.Copy(_latest, count, _latest, 0, _latest.Length - count);
            for (var i = _latest.Length - count; i < _latest.Length; i++, _sample++)
            {
                var t = (double)_sample / SampleRate;
                var sinceBeat = t % 0.5;
                var kick = Math.Sin(2 * Math.PI * (50 + 80 * Math.Exp(-sinceBeat * 30)) * sinceBeat) * Math.Exp(-sinceBeat * 8);
                var bass = 0.3 * Math.Sin(2 * Math.PI * (t % 2 < 1 ? 55 : 73.4) * t);
                var treble = 0.08 * Math.Sin(2 * Math.PI * 5000 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 4 * t));
                _latest[i] = (float)(0.6 * kick + bass + treble);
            }

            return _latest;
        }
    }
}
