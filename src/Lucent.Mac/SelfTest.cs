using System.Diagnostics;
using System.Runtime.InteropServices;
using Lucent.Mac.Export;
using Lucent.Mac.Interop;
using Lucent.Mac.Visuals;
using NAudio.Wave;
using SharpMetal.Foundation;
using SharpMetal.Metal;
using SkiaSharp;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// Checks the Mac app works without a window, for CI on a Mac: decodes the given music files with
/// Core Audio, then compiles every visual's Metal shaders and draws a few frames of each to music
/// made up on the spot, and can save a contact sheet of what they look like. It records a short video
/// as Record video does and checks the MP4 that comes out. It also translates visuals from HLSL as the
/// app does for the user's own, and checks the result matches the build's.
/// </summary>
/// <remarks>Usage: Lucent --self-test [--sheet picture.png] [music files...]</remarks>
public static class SelfTest
{
    private const int Width = 320;
    private const int Height = 180;
    private const int FramesPerVisual = 45;

    // A visual still black after FramesPerVisual frames keeps drawing this much longer before it
    // counts as black: some take a while to build up, or have quiet moments (Sand Yantra sweeps its
    // mandala away and pours it out again), and the brightness is checked every CheckEvery frames.
    private const int SlowStarterFrames = 12 * 60;
    private const int CheckEvery = 30;
    private const double DarkBrightness = 0.004;
    private const double FrameSeconds = 1.0 / 60.0;
    private const int SampleRate = 44100;

    // The test recording: small, and a part from partway into the song, so the warm-up before it is drawn too.
    private const int RecordingWidth = 320;
    private const int RecordingHeight = 180;
    private const int RecordingFrameRate = 30;
    private static readonly TimeSpan RecordingFrom = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan RecordingTo = TimeSpan.FromSeconds(2.5);

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
        failures += TestRecording();
        failures += TestRuntimeTranslation();
        failures += WinampInspiredVisualizer.Editor.Stems.StemSelfTest.Run(Console.WriteLine);
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
        // A cache of its own, so the self-test never tidies away the user's translations.
        var files = new MetalShaderFiles(cache: new ShaderCache(Path.Combine(Path.GetTempPath(), "lucent-self-test-cache")));
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
        var slow = new List<string>();
        var heavy = new List<string>();
        var drawingErrors = 0;
        var now = 0.0;
        var music = new MadeUpMusic();
        var stopwatch = Stopwatch.StartNew();

        engine.StartCompiling();
        for (var index = 0; index < engine.Presets.Count; index++)
        {
            var name = engine.Presets[index].Name;
            engine.PresetIndex = index;

            // Draw until this visual has compiled and been on screen for a while (or it failed). One
            // that's still black then keeps going a while longer, in case it's just a slow starter.
            var drawn = 0;
            var waitedFor = Stopwatch.StartNew();
            MTLCommandBuffer? last = null;
            MTLCommandBuffer? latest = null;
            var brightness = 0.0;
            while (!engine.HasFailed(index) && waitedFor.Elapsed < TimeSpan.FromSeconds(120))
            {
                using var pool = AutoreleasePool.Begin();
                capture.Write(music.Next((int)(SampleRate * FrameSeconds)));
                now += FrameSeconds;
                var commands = engine.EncodeFrame(output, now, (float)FrameSeconds);
                commands.Commit();
                if (engine.ActivePreset == index)
                {
                    drawn++;
                    latest = commands;
                    if (drawn >= FramesPerVisual && (drawn - FramesPerVisual) % CheckEvery == 0)
                    {
                        commands.WaitUntilCompleted();
                        last = commands;
                        if (commands.Status == MTLCommandBufferStatus.Error)
                        {
                            drawingErrors++;
                            Console.WriteLine($"::error::{name}: the GPU reported an error drawing it: {StringHelper.Read(commands.Error.LocalizedDescription)}");
                            break;
                        }

                        if (!canRead)
                        {
                            break;
                        }

                        brightness = Brightness(output, pixels);
                        if (brightness >= DarkBrightness || drawn >= FramesPerVisual + SlowStarterFrames)
                        {
                            break;
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

            if (last is null && latest is { } newest)
            {
                // On screen but drawing too slowly to reach FramesPerVisual in time, as a heavy visual can
                // on a slow or virtual GPU: check the newest frame it drew instead.
                newest.WaitUntilCompleted();
                if (newest.Status == MTLCommandBufferStatus.Error)
                {
                    drawingErrors++;
                    Console.WriteLine($"::error::{name}: the GPU reported an error drawing it: {StringHelper.Read(newest.Error.LocalizedDescription)}");
                    continue;
                }

                last = newest;
                heavy.Add($"{name} ({drawn} frames in {waitedFor.Elapsed.TotalSeconds:0} s)");
                if (canRead)
                {
                    brightness = Brightness(output, pixels);
                }
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

        if (slow.Count > 0)
        {
            Console.WriteLine($"Slow starters, black after {FramesPerVisual} frames but drawing later: {string.Join(", ", slow)}");
        }

        if (heavy.Count > 0)
        {
            Console.WriteLine($"::warning::Drew too slowly on this GPU to reach {FramesPerVisual} frames: {string.Join(", ", heavy)}");
        }

        if (dark.Count > 0)
        {
            Console.WriteLine($"::warning::Still black after {(FramesPerVisual + SlowStarterFrames) / 60} seconds: {string.Join(", ", dark)}");
        }

        if (sheet is not null)
        {
            using var file = File.Create(sheetPath!);
            sheet.Encode(file, SKEncodedImageFormat.Png, 100);
            Console.WriteLine($"Saved a contact sheet of every visual to {sheetPath}.");
        }

        return failed.Count + drawingErrors;
    }

    /// <summary>
    /// Records two seconds of made-up music drawn with one visual, small, as Record video does (off screen
    /// with Metal, encoded by AVFoundation), starting partway into the song so the warm-up before it is
    /// drawn too. Then it opens the MP4 with AVFoundation and checks it has one video track and one sound
    /// track, the right size, frame rate and length, and a picture in it that isn't black.
    /// </summary>
    private static int TestRecording()
    {
        if (MetalSupport.TryCreateDevice() is null)
        {
            Console.WriteLine("::warning::This machine has no Metal device, so recording a video wasn't tried.");
            return 0;
        }

        var folder = Directory.CreateTempSubdirectory("lucent-recording");
        var problems = 0;
        try
        {
            var song = Path.Combine(folder.FullName, "Made-up music.wav");
            WriteMadeUpSong(song, seconds: 3);
            var files = new MetalShaderFiles(cache: new ShaderCache(Path.Combine(Path.GetTempPath(), "lucent-self-test-cache")));
            var presets = files.LoadPresets().ToList();
            if (presets.Count == 0)
            {
                Console.WriteLine("::error::No visuals were found to record a video with.");
                return 1;
            }

            // Bauhaus is quick to draw, on off-white paper, so a dark picture means the recording lost it.
            var index = presets.FindIndex(preset => preset.Name == "Bauhaus");
            var bright = index >= 0;
            if (!bright)
            {
                index = Math.Max(0, presets.FindIndex(preset => !preset.IsParticles));
            }

            var output = Path.Combine(folder.FullName, "Self-test video.mp4");
            var options = new VideoRecordingOptions
            {
                TrackPath = song,
                OutputPath = output,
                Width = RecordingWidth,
                Height = RecordingHeight,
                FrameRate = RecordingFrameRate,
                Quality = VideoQuality.Good,
                From = RecordingFrom,
                To = RecordingTo,
                Presets = presets,
                PresetIndex = index,
            };
            var stages = new StageLog();
            var stopwatch = Stopwatch.StartNew();
            var result = VideoRecorder.RecordAsync(options, stages, CancellationToken.None, files).GetAwaiter().GetResult();
            Console.WriteLine($"Recorded {presets[index].Name} at {RecordingWidth} x {RecordingHeight}, {RecordingFrameRate} fps: "
                + $"{result.Length.TotalSeconds:0.00} s of video in {stopwatch.Elapsed.TotalSeconds:0.0} s. {result.Note}");
            foreach (var stage in stages.Seen.Where(stage => stage.Contains("starting again", StringComparison.Ordinal)))
            {
                Console.WriteLine($"::warning::{stage}");
            }

            problems += CheckVideo(output, (RecordingTo - RecordingFrom).TotalSeconds, bright);

            // Everything is written to a hidden temporary file first, which mustn't be left behind.
            var leftovers = Directory.GetFiles(folder.FullName, ".*").Select(Path.GetFileName).ToList();
            if (leftovers.Count > 0)
            {
                problems++;
                Console.WriteLine($"::error::Recording left a temporary file behind: {string.Join(", ", leftovers)}");
            }
        }
        catch (Exception ex)
        {
            problems++;
            Console.WriteLine($"::error::Recording a video failed: {ex.Message}");
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Only the temporary folder.
            }
        }

        if (problems == 0)
        {
            Console.WriteLine("Recording a video works.");
        }

        return problems;
    }

    /// <summary>Opens a recorded video with AVFoundation and checks what's in it, as a player would find it.</summary>
    private static int CheckVideo(string path, double seconds, bool bright)
    {
        using var pool = AutoreleasePool.Begin();
        var bytes = File.Exists(path) ? new FileInfo(path).Length : 0;
        if (bytes < 1024)
        {
            Console.WriteLine($"::error::The recorded video is {(bytes == 0 ? "missing or empty" : $"only {bytes} bytes")}.");
            return 1;
        }

        var problems = 0;
        var asset = AVFoundation.OpenAsset(path);
        var (videoTracks, video) = AVFoundation.Tracks(asset, "AVMediaTypeVideo");
        var (soundTracks, sound) = AVFoundation.Tracks(asset, "AVMediaTypeAudio");
        var duration = AVFoundation.AssetDuration(asset);
        Console.WriteLine($"The video file: {bytes / 1024} KB, {duration:0.00} s long, with {videoTracks} video track(s) and {soundTracks} sound track(s).");
        if (videoTracks != 1 || soundTracks != 1)
        {
            problems++;
            Console.WriteLine("::error::The recorded video should have one video track and one sound track.");
        }

        if (!(Math.Abs(duration - seconds) < 0.1))
        {
            problems++;
            Console.WriteLine($"::error::The recorded video is {duration:0.00} s long, not {seconds:0.00} s.");
        }

        if (sound != IntPtr.Zero && !(Math.Abs(AVFoundation.TrackDuration(sound) - seconds) < 0.15))
        {
            problems++;
            Console.WriteLine($"::error::The recorded video's sound is {AVFoundation.TrackDuration(sound):0.00} s long, not {seconds:0.00} s.");
        }

        if (video == IntPtr.Zero)
        {
            return problems;
        }

        var size = ObjC.ReadValue<ObjC.CGSize>(video, "naturalSize");
        var frameRate = AVFoundation.Number(video, "nominalFrameRate");
        Console.WriteLine($"Its pictures: {size.Width:0} x {size.Height:0} at {frameRate:0.##} fps, {AVFoundation.TrackDuration(video):0.00} s.");
        if (Math.Abs(size.Width - RecordingWidth) > 0.5 || Math.Abs(size.Height - RecordingHeight) > 0.5)
        {
            problems++;
            Console.WriteLine($"::error::The recorded video is {size.Width:0} x {size.Height:0}, not {RecordingWidth} x {RecordingHeight}.");
        }

        if (!(Math.Abs(frameRate - RecordingFrameRate) < 1))
        {
            problems++;
            Console.WriteLine($"::error::The recorded video plays at {frameRate:0.##} fps, not {RecordingFrameRate}.");
        }

        // A picture from the middle: the readback from the graphics card and the encoder's colours both show here.
        var brightness = PictureBrightness(asset, CoreMedia.CMTime.Make((long)(seconds * 300), 600));
        if (brightness is null)
        {
            problems++;
        }
        else if (brightness < (bright ? 0.1 : DarkBrightness))
        {
            if (bright)
            {
                problems++;
                Console.WriteLine($"::error::The recorded video's picture is dark (brightness {brightness:0.000}), so the visual didn't reach it.");
            }
            else
            {
                Console.WriteLine($"::warning::The recorded video's picture is black (brightness {brightness:0.000}).");
            }
        }
        else
        {
            Console.WriteLine($"A picture from the middle of it has brightness {brightness:0.000}.");
        }

        return problems;
    }

    /// <summary>The average brightness (0 to 1) of a video's picture at <paramref name="time"/>, or null (saying why) if it can't be read.</summary>
    private static unsafe double? PictureBrightness(IntPtr asset, CoreMedia.CMTime time)
    {
        var image = AVFoundation.CopyPicture(asset, time, out var problem);
        if (image == IntPtr.Zero)
        {
            Console.WriteLine($"::error::A picture couldn't be read from the recorded video: {problem}");
            return null;
        }

        try
        {
            var width = (int)CoreGraphics.CGImageGetWidth(image);
            var height = (int)CoreGraphics.CGImageGetHeight(image);
            var pixels = new byte[width * height * 4];
            fixed (byte* data = pixels)
            {
                // Drawn into memory in a known layout (red, green, blue, unused), whatever the decoder made.
                var context = CoreGraphics.CGBitmapContextCreate((IntPtr)data, (nuint)width, (nuint)height, 8, (nuint)(width * 4),
                    CoreGraphics.Srgb, CoreGraphics.RgbxBitmap);
                if (context == IntPtr.Zero)
                {
                    Console.WriteLine("::error::The recorded video's picture couldn't be looked at.");
                    return null;
                }

                CoreGraphics.CGContextDrawImage(context, new ObjC.CGRect { Width = width, Height = height }, image);
                CoreGraphics.CGContextRelease(context);
            }

            long sum = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                sum += pixels[i] + pixels[i + 1] + pixels[i + 2];
            }

            return sum / (width * height * 3.0 * 255.0);
        }
        finally
        {
            CoreGraphics.CGImageRelease(image);
        }
    }

    /// <summary>A WAV file of <see cref="MadeUpMusic"/>, 16-bit stereo, for recording a video of.</summary>
    private static void WriteMadeUpSong(string path, int seconds)
    {
        using var writer = new WaveFileWriter(path, new WaveFormat(SampleRate, 16, 2));
        foreach (var sample in new MadeUpMusic().Next(SampleRate * seconds))
        {
            writer.WriteSample(sample);
            writer.WriteSample(sample);
        }
    }

    /// <summary>Notes each stage a recording reports, as it happens (on the recording's own thread).</summary>
    private sealed class StageLog : IProgress<VideoRecordingProgress>
    {
        private readonly List<string> _seen = new();

        public IReadOnlyList<string> Seen
        {
            get
            {
                lock (_seen)
                {
                    return _seen.ToList();
                }
            }
        }

        public void Report(VideoRecordingProgress value)
        {
            lock (_seen)
            {
                if (!_seen.Contains(value.Stage))
                {
                    _seen.Add(value.Stage);
                }
            }
        }
    }

    /// <summary>The output's average brightness, 0 to 1, leaving its BGRA pixels in <paramref name="pixels"/>.</summary>
    private static double Brightness(MTLTexture output, byte[] pixels)
    {
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

        return sum / (Width * Height * 3.0 * 255.0);
    }

    /// <summary>
    /// Translates some visuals from HLSL as the app does for the user's own (DXC's library, then SPIRV-Cross),
    /// checks each comes out exactly as the build translated it, and has Metal compile a visual of the user's own.
    /// </summary>
    private static int TestRuntimeTranslation()
    {
        if (!DxcCompiler.IsAvailable)
        {
            Console.WriteLine($"::warning::{DxcCompiler.LibraryName} isn't beside the app, so the user's own visuals can't be translated.");
            return 0;
        }

        var problems = 0;
        var userFolder = Directory.CreateTempSubdirectory("lucent-own-visuals");
        var cacheFolder = Directory.CreateTempSubdirectory("lucent-translations");
        try
        {
            File.WriteAllText(Path.Combine(userFolder.FullName, "SelfTestOwn.hlsl"),
                "// Name: Self-test own visual\n// Family: Fractal\nfloat3 Render(float2 uv, float2 p)\n{\n    return float3(pow(p.x, 2.0), uv.y, 0.5 + 0.5 * sin(Time + Bass));\n}\n");
            var files = new MetalShaderFiles(userPresetsFolder: userFolder.FullName, cache: new ShaderCache(cacheFolder.FullName));
            var presets = files.LoadPresets();
            var own = presets.FirstOrDefault(preset => !files.IsBuiltIn(preset));
            if (own is null)
            {
                Console.WriteLine("::error::A visual in the user's own folder wasn't found.");
                return 1;
            }

            // The user's visual, translated now, must compile for Metal.
            var device = MTLDevice.CreateSystemDefaultDevice();
            var stopwatch = Stopwatch.StartNew();
            var metal = files.Preset(own, "PSMain");
            Console.WriteLine($"Translated {own.Name} from HLSL in {stopwatch.Elapsed.TotalMilliseconds:0} ms.");
            if (device.NativePtr != IntPtr.Zero)
            {
                var options = new MTLCompileOptions { LanguageVersion = MTLLanguageVersion.Version21, FastMathEnabled = false };
                var error = new NSError(IntPtr.Zero);
                var library = device.NewLibrary(StringHelper.Create(metal), options, ref error);
                if (library.NativePtr == IntPtr.Zero)
                {
                    problems++;
                    Console.WriteLine($"::error::{own.Name}, translated as the app runs, didn't compile for Metal: {(error.NativePtr != IntPtr.Zero ? StringHelper.Read(error.LocalizedDescription) : "no reason given")}");
                }
                else
                {
                    library.Dispose();
                }
            }

            // Built-in visuals translated now must match what the build made, a particle visual among them.
            var samples = presets.Where(files.IsBuiltIn).Where(preset => preset.IsParticles).Take(1)
                .Concat(presets.Where(files.IsBuiltIn).Where(preset => !preset.IsParticles).Take(2));
            var common = File.ReadAllText(Path.Combine(files.ShadersFolder, "Common.hlsli"));
            var particles = File.ReadAllText(Path.Combine(files.ShadersFolder, "Particles.hlsli"));
            var presetMain = File.ReadAllText(Path.Combine(files.ShadersFolder, "PresetMain.hlsli"));
            var particleMain = File.ReadAllText(Path.Combine(files.ShadersFolder, "ParticleMain.hlsli"));
            foreach (var preset in samples)
            {
                foreach (var entry in preset.IsParticles ? new[] { "PSMain", "CSMain", "VSParticle" } : new[] { "PSMain" })
                {
                    var shipped = Path.Combine(files.MetalFolder, "Presets", $"{Path.GetFileName(preset.FilePath)}.{entry}.metal");
                    var hlsl = MetalTranslation.PresetSource(common, particles, presetMain, particleMain, preset.Code, preset.IsParticles, entry);
                    var now = new ShaderCache(cacheFolder.FullName).GetOrTranslate(hlsl, entry, Path.GetFileName(preset.FilePath));
                    if (!File.Exists(shipped) || File.ReadAllText(shipped).ReplaceLineEndings("\n") != now.ReplaceLineEndings("\n"))
                    {
                        problems++;
                        Console.WriteLine($"::error::{preset.Name} ({entry}) translated as the app runs doesn't match the build's translation.");
                    }
                }
            }

            if (problems == 0)
            {
                Console.WriteLine("Translating visuals as the app runs works, and matches the build.");
            }
        }
        catch (Exception ex)
        {
            problems++;
            Console.WriteLine($"::error::Translating a visual as the app runs failed: {ex.Message}");
        }
        finally
        {
            userFolder.Delete(recursive: true);
            cacheFolder.Delete(recursive: true);
        }

        return problems;
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
