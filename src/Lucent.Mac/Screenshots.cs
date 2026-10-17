using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
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
        ("Equalizer", () => PreviewEqualizer()),
        ("Analyzer", () => PreviewAnalyzer()),
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

    private static Window PreviewEqualizer()
    {
        var player = new Lucent.Mac.Audio.AudioPlayer();
        var settings = new AppSettings();
        settings.Equalizer.BandCount = 16;
        settings.Equalizer.PresetName = "Rock";
        settings.Equalizer.Gains = WinampInspiredVisualizer.Audio.EqualizerPresets.Find("Rock")!.Curve
            .Sample(WinampInspiredVisualizer.Audio.EqualizerBands.ForCount(16)).ToList();
        settings.Equalizer.PreampDb = -2;
        player.Equalizer.Apply(settings.Equalizer);
        var window = EqualizerWindow.Create(player, settings);
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            window.UpdateLayout();
            AnalyzerSampleMusic.Play(player.Capture, 1.5, seconds => window.Spectrum.Advance((float)seconds));
        });
        return window;
    }

    private static Window PreviewAnalyzer()
    {
        var player = new Lucent.Mac.Audio.AudioPlayer();
        var window = AnalyzerWindow.Create(player, new AppSettings());
        window.Opened += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            window.UpdateLayout();
            AnalyzerSampleMusic.Play(player.Capture, 11, seconds => window.Picture.Advance(seconds));
        });
        return window;
    }
}
