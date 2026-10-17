using System.Reflection;
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
        ("Player", () => PreviewPlayer("Lucent")),
        ("Player-Lucent", () => PreviewPlayer("Lucent")),
        ("Player-Classic", () => PreviewPlayer("Classic")),
        ("Player-Glass", () => PreviewPlayer("Glass")),
        ("Player-Neon", () => PreviewPlayer("Neon")),
        ("Player-Studio", () => PreviewPlayer("Studio")),
        ("Player-Options", () => PreviewOptions("Graphics")),
        ("Player-Options-Visuals", () => PreviewOptions("Visuals")),
        ("Player-Options-Look", () => PreviewOptions("Theme")),
        ("Prompt", () => PreviewPrompt()),
        ("Message", () => PreviewMessage()),
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

    /// <summary>The player in a skin, part way through a song in a queue of three.</summary>
    private static Window PreviewPlayer(string skin)
    {
        var player = new MainWindow();
        player.PreviewSkin(skin);
        player.ShowPreview(new[]
        {
            SampleTrack("Boards of Canada", "Roygbiv", TimeSpan.FromSeconds(151), 320, 44100, 2),
            SampleTrack("Aphex Twin", "Xtal", TimeSpan.FromSeconds(294), 1411, 44100, 2),
            SampleTrack("Burial", "Archangel", TimeSpan.FromSeconds(238), 256, 48000, 2),
        }, TimeSpan.FromSeconds(83));
        return player;
    }

    /// <summary>The player's options panel (normally a pop-up under its button), on one of its tabs.</summary>
    private static Window PreviewOptions(string tab)
    {
        var player = new MainWindow();
        player.PreviewSkin("Lucent");
        var panel = player.TakeOptionsPanel(tab);
        return new Window
        {
            Width = 372,
            SizeToContent = SizeToContent.Height,
            Background = Avalonia.Media.Brushes.Transparent,
            Content = panel,
        };
    }

    /// <summary>A track as if read from a tagged file (none is needed: the screenshots play nothing).</summary>
    private static WinampInspiredVisualizer.Playlist.Track SampleTrack(string artist, string title, TimeSpan duration, int bitrate, int sampleRate, int channels)
    {
        var constructor = typeof(WinampInspiredVisualizer.Playlist.Track).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)[0];
        return (WinampInspiredVisualizer.Playlist.Track)constructor.Invoke(new object[]
        {
            $"/Users/you/Music/{artist}/{title}.flac", title, artist, duration, bitrate, sampleRate, channels, string.Empty,
        });
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
}
