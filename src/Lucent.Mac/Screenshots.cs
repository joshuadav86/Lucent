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
        ("Stores", () => PreviewStores(signedIn: false)),
        ("StoresSyncing", () => PreviewStores(signedIn: true, syncing: true)),
        ("StoresFirstSync", () => PreviewStores(signedIn: true, firstSync: true)),
        ("BandcampSignIn", () => new Lucent.Mac.Stores.BandcampSignInWindow()),
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

    private static Window PreviewStores(bool signedIn, bool syncing = false, bool firstSync = false)
    {
        var settings = new AppSettings
        {
            BandcampImport = true,
            BeatportImport = !signedIn,
            DownloadsWatchFolder = "/Users/sam/Downloads",
            StoreMusicFolder = "/Users/sam/Music",
            BandcampUsername = signedIn ? "nightdrive" : null,
            BandcampSync = syncing,
            BandcampFormat = "flac",
        };
        Lucent.Mac.Stores.BandcampSignIn.UseForPreview(signedIn ? "identity=preview" : null);

        var service = new Lucent.Mac.Stores.StoreService(settings, _ => { });
        var window = new StoreWindow(service, settings);
        if (syncing)
        {
            service.PreviewSync(new WinampInspiredVisualizer.Stores.BandcampSyncProgress(3, 12, "Kiasmos - Blurred EP", 0.4));
        }
        else if (firstSync)
        {
            service.PreviewStatus("12 purchases aren't in your library yet.");
            window.PreviewFirstSync(Enumerable.Range(1, 12)
                .Select(i => new WinampInspiredVisualizer.Stores.BandcampPurchase($"{i}", "Artist", $"Release {i}", "https://bandcamp.com/download", false))
                .ToList());
        }
        else
        {
            service.PreviewStatus("Added Burial - Antidawn to your library.");
        }

        return window;
    }
}
