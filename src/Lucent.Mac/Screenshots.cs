using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// Draws each of the app's windows to a PNG without a screen, so the Mac windows can be checked by eye on
/// any machine, Linux included: <c>Lucent --screenshots out-folder [window names...]</c>. It uses Avalonia's
/// headless renderer, so everything shows except the Metal visuals (drawn by the graphics chip).
/// </summary>
public static class Screenshots
{
    /// <summary>
    /// Every window, by name, with what it needs to show something worth looking at. Each window's
    /// port adds itself here.
    /// </summary>
    private static readonly (string Name, Func<Window> Create)[] Catalog =
    {
        ("Player", () => new MainWindow()),
        ("Prompt", () => PreviewPrompt()),
        ("Message", () => PreviewMessage()),
        ("Editor", () => PreviewEditor(EditorView.Waveform)),
        ("EditorSpectrogram", () => PreviewEditor(EditorView.Spectrogram)),
        ("SaveAs", () => ExportWindow.PrepareSaveDialog(Path.Combine(SampleFolder(), "Night Drive.flac"),
            new ExportOptions { Format = ExportFormat.Flac }, null)),
        ("Convert", () => ExportWindow.PrepareConverter(
            new[] { "Night Drive.flac", "Harbour Lights.wav", "Glass Garden.mp3", "Slow Orbit.m4a" }
                .Select(name => Path.Combine(SampleFolder(), name)).ToList(), new AppSettings())),
    };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Lucent --screenshots <folder> [window names...]");
            return 2;
        }

        var folder = args[0];
        var only = args.Skip(1).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(folder);

        // Settings and the library go to a throwaway folder, so screenshots never touch real ones.
        Environment.SetEnvironmentVariable("LUCENT_DATA_FOLDER", Path.Combine(Path.GetTempPath(), "lucent-screenshots"));

        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();

        var failures = 0;
        foreach (var (name, create) in Catalog.Where(entry => only.Count == 0 || only.Contains(entry.Name)))
        {
            try
            {
                var window = create();
                window.Show();
                Dispatcher.UIThread.RunJobs();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                var frame = window.CaptureRenderedFrame();
                var path = Path.Combine(folder, $"{name}.png");
                frame?.Save(path);
                Console.WriteLine($"{name}: {(frame is null ? "nothing drawn" : path)}");
                window.Close();
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"::error::{name} couldn't be drawn: {ex}");
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static Window PreviewPrompt()
    {
        var prompt = new PromptWindow();
        prompt.Heading.Text = "New playlist";
        prompt.Hint.Text = "Give the playlist a name.";
        prompt.OkButton.Content = "Create";
        prompt.Answer.Text = "Late night";
        return prompt;
    }

    private static Window PreviewMessage()
    {
        var box = new MessageWindow();
        box.Heading.Text = "Lucent";
        box.Body.Text = "Lucent couldn't play this file. It may be damaged, or in a format Lucent can't read.";
        box.Buttons.Children.Add(new Button { Content = "OK", Theme = (Avalonia.Styling.ControlTheme?)box.FindResource("PickerButton") });
        return box;
    }

    /// <summary>A folder for the sample files the editor and converter show.</summary>
    private static string SampleFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lucent-screenshots", "Music");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>
    /// The audio editor with a generated song loaded, a few markers, a split and a selection.
    /// </summary>
    private static Window PreviewEditor(EditorView view)
    {
        var path = Path.Combine(SampleFolder(), "Night Drive.wav");
        if (!File.Exists(path))
        {
            WriteSampleSong(path);
        }

        // Core Audio isn't there off the Mac; NAudio reads the WAV just as well.
        var decoder = AudioReaderFactory.OpenWithSystemDecoder;
        AudioReaderFactory.OpenWithSystemDecoder = file =>
            file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? new WaveFileReader(file) : decoder!(file);

        var editor = new EditorWindow(path, new AudioPlayer());
        editor.Show();
        var waited = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        while ((!editor.Loading.IsCompleted || editor.IsBusy) && waited.Elapsed < TimeSpan.FromSeconds(30));

        AudioReaderFactory.OpenWithSystemDecoder = decoder;
        editor.ShowPreviewEdits(view);
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    /// <summary>
    /// Writes 40 seconds of something song-like: a chord progression with a melody, a kick and hi-hats,
    /// a quiet intro and a louder chorus, so the waveform and spectrogram have shape.
    /// </summary>
    private static void WriteSampleSong(string path)
    {
        const int rate = 44100;
        const double seconds = 40;
        var frames = (int)(rate * seconds);
        var random = new Random(7);
        var chords = new[] { new[] { 220.0, 261.6, 329.6 }, new[] { 174.6, 220.0, 261.6 }, new[] { 196.0, 246.9, 293.7 }, new[] { 164.8, 207.7, 246.9 } };
        var melody = new[] { 659.3, 587.3, 523.3, 587.3, 659.3, 784.0, 659.3, 523.3 };
        const double beat = 0.5;
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)rate;
            var section = t < 3 ? t / 3 * 0.4 : t < 18 ? 0.55 : t < 25 ? 0.7 : 1.0;
            var fadeOut = t > seconds - 3 ? (seconds - t) / 3 : 1;
            var chord = chords[(int)(t / 2) % chords.Length];
            double pad = 0;
            foreach (var f in chord)
            {
                pad += Math.Sin(2 * Math.PI * f * t) + 0.3 * Math.Sin(4 * Math.PI * f * t);
            }

            var noteTime = t % beat;
            var note = melody[(int)(t / beat) % melody.Length];
            var lead = t > 8 ? Math.Sin(2 * Math.PI * note * t) * Math.Exp(-noteTime * 5) : 0;
            var kickTime = t % (beat * 2);
            var kick = t > 3 ? Math.Sin(2 * Math.PI * (50 + 90 * Math.Exp(-kickTime * 30)) * kickTime) * Math.Exp(-kickTime * 9) : 0;
            var hatTime = (t + beat / 2) % beat;
            var hat = t > 12 ? (random.NextDouble() * 2 - 1) * Math.Exp(-hatTime * 60) * 0.5 : 0;
            var mix = (pad * 0.07 + lead * 0.22 + kick * 0.55 + hat * 0.25) * section * fadeOut;
            samples[i * 2] = (float)(mix + lead * 0.05 * section);
            samples[i * 2 + 1] = (float)(mix - lead * 0.05 * section + hat * 0.04);
        }

        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
        writer.WriteSamples(samples, 0, samples.Length);
    }
}
