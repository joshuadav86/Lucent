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
        ("Visuals", () => PreviewVisuals()),
        ("VisualsPicker", () => PreviewVisualsPicker()),
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

    private static VisualizerWindow PreviewVisuals(string lastVisual = "Lava Lamp")
    {
        var settings = new AppSettings
        {
            LastVisual = lastVisual,
            FavoriteVisuals = { "Lava Lamp", "Aurora", "Neural Bloom" },
            VisualLists = { ["Late night"] = new List<string> { "Lava Lamp", "Neural Bloom" } },
            ShowFrameRate = true,
        };
        var host = new Lucent.Mac.Visualizer.VisualizerHost(new WinampInspiredVisualizer.Audio.AudioCapture())
        {
            StartPresetName = settings.LastVisual,
        };
        var window = new VisualizerWindow();
        window.Attach(host, settings);
        window.ShowFrameRate(settings.ShowFrameRate);
        Dispatcher.UIThread.RunJobs();
        window.FrameRateText.Text = "60 fps · 1638 × 1012";
        return window;
    }

    /// <summary>The visual picker, which opens above the visuals' bar, in a window of its own so it can be drawn.</summary>
    private static Window PreviewVisualsPicker()
    {
        // The visuals load once their window is shown; then the picker moves to a window of its own.
        var visuals = PreviewVisuals("Crystal Cavern");
        visuals.Show();
        Dispatcher.UIThread.RunJobs();
        var picker = visuals.PresetPopup.Child!;
        visuals.PresetPopup.Child = null;
        visuals.Close();
        return new Window
        {
            Width = 532,
            SizeToContent = SizeToContent.Height,
            Background = Avalonia.Media.Brushes.Transparent,
            Content = picker,
        };
    }

    private static Window PreviewMessage()
    {
        var box = new MessageWindow();
        box.Heading.Text = "Lucent";
        box.Body.Text = "Lucent couldn't play this file. It may be damaged, or in a format Lucent can't read.";
        box.Buttons.Children.Add(new Button { Content = "OK", Theme = (Avalonia.Styling.ControlTheme?)box.FindResource("PickerButton") });
        return box;
    }
}
