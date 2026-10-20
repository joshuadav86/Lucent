using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Lucent.Mac.Library;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using Lucent.Mac.Audio;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Stems;
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
        ("Player", () => PreviewPlayer("Lucent")),
        ("Player-Lucent", () => PreviewPlayer("Lucent")),
        ("Player-Classic", () => PreviewPlayer("Classic")),
        ("Player-Glass", () => PreviewPlayer("Glass")),
        ("Player-Neon", () => PreviewPlayer("Neon")),
        ("Player-Studio", () => PreviewPlayer("Studio")),
        ("Player-Options", () => PreviewOptions("Graphics")),
        ("Player-Options-Visuals", () => PreviewOptions("Visuals")),
        ("Player-Options-Sound", () => PreviewOptions("Sound")),
        ("Player-Options-Look", () => PreviewOptions("Theme")),
        ("Player-Options-Updates", () => PreviewOptions("Updates")),
        ("Player-Sources", () => PreviewSources()),
        ("Prompt", () => PreviewPrompt()),
        ("Message", () => PreviewMessage()),
        ("Library", () => PreviewLibrary()),
        ("Queue", () => PreviewQueue()),
        ("TrackInfo", TrackInfoPreviews.TrackInfo),
        ("TrackInfoLyrics", TrackInfoPreviews.TrackInfoLyrics),
        ("TrackInfoFile", TrackInfoPreviews.TrackInfoFile),
        ("TrackInfoSeveral", TrackInfoPreviews.TrackInfoSeveral),
        ("Artwork", TrackInfoPreviews.Artwork),
        ("ArtworkEmpty", TrackInfoPreviews.ArtworkEmpty),
        ("Equalizer", () => PreviewEqualizer()),
        ("Analyzer", () => PreviewAnalyzer()),
        ("Radio", () => PreviewRadio(genre: false)),
        ("RadioGenre", () => PreviewRadio(genre: true)),
        ("Stores", () => PreviewStores(signedIn: false)),
        ("StoresSyncing", () => PreviewStores(signedIn: true, syncing: true)),
        ("StoresFirstSync", () => PreviewStores(signedIn: true, firstSync: true)),
        ("BandcampSignIn", () => new Lucent.Mac.Stores.BandcampSignInWindow()),
        ("Import", () => PreviewImport(done: false)),
        ("ImportDone", () => PreviewImport(done: true)),
        ("Visuals", () => PreviewVisuals()),
        ("VisualsPicker", () => PreviewVisualsPicker()),
        ("Editor", () => PreviewEditor(EditorView.Waveform)),
        ("EditorSpectrogram", () => PreviewEditor(EditorView.Spectrogram)),
        ("EditorEffects", () => PreviewEditorEffects()),
        ("EditorBeats", () => PreviewEditorBeats()),
        ("EditorLoopMaker", () => PreviewLoopMaker()),
        ("SaveAs", () => ExportWindow.PrepareSaveDialog(Path.Combine(SampleFolder(), "Night Drive.flac"),
            new ExportOptions { Format = ExportFormat.Flac }, null)),
        ("Convert", () => ExportWindow.PrepareConverter(
            new[] { "Night Drive.flac", "Harbour Lights.wav", "Glass Garden.mp3", "Slow Orbit.m4a" }
                .Select(name => Path.Combine(SampleFolder(), name)).ToList(), new AppSettings())),
        ("RecordVideo", () => PreviewRecordVideo()),
        ("Stems", () => PreviewStems()),
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
    private static Window PreviewSources()
    {
        var player = new MainWindow();
        player.PreviewSkin("Lucent");
        return new Window
        {
            Width = 352,
            SizeToContent = SizeToContent.Height,
            Background = Avalonia.Media.Brushes.Transparent,
            Content = player.TakeSourcesPanel(),
        };
    }

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

    /// <summary>A track with made-up tags, without a file to read them from.</summary>
    private static Track SampleTrack(string path, string title, string artist, string album, int seconds)
    {
        var constructor = typeof(Track).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
        return (Track)constructor.Invoke(new object?[]
        {
            path, title, artist, TimeSpan.FromSeconds(seconds), 320, 44100, 2, album,
        });
    }

    private static readonly (string Folder, string Title, string Artist, string Album, int Seconds, int DaysAgo)[] SampleSongs =
    {
        ("Music / Boards of Canada / Music Has the Right to Children", "Roygbiv", "Boards of Canada", "Music Has the Right to Children", 151, 2),
        ("Music / Boards of Canada / Music Has the Right to Children", "Aquarius", "Boards of Canada", "Music Has the Right to Children", 352, 2),
        ("Music / Boards of Canada / Music Has the Right to Children", "Turquoise Hexagon Sun", "Boards of Canada", "Music Has the Right to Children", 307, 2),
        ("Music / Aphex Twin / Selected Ambient Works 85-92", "Xtal", "Aphex Twin", "Selected Ambient Works 85-92", 291, 5),
        ("Music / Aphex Twin / Selected Ambient Works 85-92", "Pulsewidth", "Aphex Twin", "Selected Ambient Works 85-92", 227, 5),
        ("Music / Aphex Twin / Selected Ambient Works 85-92", "Ageispolis", "Aphex Twin", "Selected Ambient Works 85-92", 321, 5),
        ("Music / Mixes", "Late night set (live)", "", "", 3725, 9),
        ("Music / Mixes", "Sunday morning", "Four Tet", "", 2411, 12),
        ("Downloads", "Windowlicker", "Aphex Twin", "Windowlicker", 367, 20),
        ("Downloads", "Kid A", "Radiohead", "Kid A", 284, 21),
    };

    private static Window PreviewLibrary()
    {
        var root = Path.Combine(Path.GetTempPath(), "lucent-screenshots", "Music");
        var items = SampleSongs.Select(song =>
        {
            var directory = Path.Combine(root, song.Folder.Replace(" / ", "/"));
            var path = Path.Combine(directory, song.Title + ".flac");
            var item = new LibraryItem(path, song.Folder, directory, DateTime.Today.AddDays(-song.DaysAgo).AddHours(song.Seconds % 13));
            item.ApplyTags(SampleTrack(path, song.Title, song.Artist, song.Album, song.Seconds));
            return item;
        }).ToList();

        var settings = new AppSettings
        {
            LibraryFolders = { root },
            CollapsedLibraryFolders = { "Downloads" },
        };
        var library = new MusicLibrary();
        var window = new LibraryWindow();
        window.Attach(library, settings);
        library.ShowPreview(settings.LibraryFolders, items);
        window.LibraryView.SelectedItem = items[1];
        return window;
    }

    private static Window PreviewQueue()
    {
        var tracks = SampleSongs.Take(6).Select(song => SampleTrack(song.Title + ".flac", song.Title, song.Artist, song.Album, song.Seconds));
        var entries = new ObservableCollection<PlaylistEntry>(tracks.Select(track => new PlaylistEntry(track)));
        entries[1].IsCurrent = true;
        var queue = new QueueWindow();
        queue.PlaylistView.ItemsSource = entries;
        queue.EmptyPlaylistHint.IsVisible = false;
        queue.QueueCount.Text = $"{entries.Count} tracks";
        return queue;
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

    /// <summary>The stems window, with four made-up stems of different loudness and shape.</summary>
    private static Window PreviewStems()
    {
        const int rate = 44100;
        var frames = rate * 20;
        AudioBuffer Stem(Func<double, double> wave) => new(Enumerable.Range(0, 2).Select(_ =>
            Enumerable.Range(0, frames).Select(i => (float)wave(i / (double)rate)).ToArray()).ToArray(), rate);
        var stems = new StemSet(new[] { "drums", "bass", "other", "vocals" }, new[]
        {
            Stem(t => 0.9 * Math.Exp(-12 * (t % 0.5)) * Math.Sin(2 * Math.PI * 60 * t)),
            Stem(t => 0.4 * Math.Sin(2 * Math.PI * 55 * t) * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 0.25 * t))),
            Stem(t => 0.3 * Math.Sin(2 * Math.PI * 330 * t) * (0.5 + 0.5 * Math.Sin(2 * Math.PI * 0.1 * t))),
            Stem(t => t % 4 < 3 ? 0.6 * Math.Sin(2 * Math.PI * 440 * t) * Math.Sin(Math.PI * (t % 4) / 3) : 0),
        });
        var window = new StemsWindow(stems, "Night Drive", "Split on the graphics card.", _ => Task.CompletedTask, () => Task.CompletedTask);
        window.PreviewMuted(0);
        window.ShowSaved("Night Drive stems/Night Drive (Vocals).flac");
        return window;
    }

    private static Window PreviewRadio(bool genre)
    {
        var settings = new AppSettings { RadioSection = WinampInspiredVisualizer.Radio.RadioGuide.Featured };
        var featured = WinampInspiredVisualizer.Radio.FeaturedStations.Stations;
        WinampInspiredVisualizer.Radio.RadioGuide.SetFavorite(settings, featured[0], true);
        WinampInspiredVisualizer.Radio.RadioGuide.SetFavorite(settings, featured[2], true);
        WinampInspiredVisualizer.Radio.RadioGuide.Remember(settings, featured[0]);
        var window = new RadioWindow(new MainWindow(), settings);
        if (genre)
        {
            var stations = new (string Name, string Tags, string Country, int Kbps)[]
            {
                ("Deep House Lounge", "deep house,house,lounge", "Netherlands", 192),
                ("Ibiza Global Radio", "house,electronic,balearic", "Spain", 128),
                ("Frisky Radio", "progressive house,trance,electronic", "United States", 128),
                ("Chicago House Radio", "house,chicago house", "United States", 320),
                ("Soulful House FM", "house,soulful,soul", "United Kingdom", 256),
                ("Tech House Radio", "tech house,house,techno", "Germany", 128),
            }.Select((station, i) => new WinampInspiredVisualizer.Radio.RadioStation
            {
                Id = $"preview-{i}",
                Name = station.Name,
                StreamUrl = "https://example.com/stream",
                Tags = station.Tags.Split(',').ToList(),
                Country = station.Country,
                Bitrate = station.Kbps,
                Codec = "MP3",
                Votes = 900 - i * 120,
            }).ToList();
            window.PreviewDirectory("House", stations);
        }

        return window;
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

    private static Window PreviewImport(bool done)
    {
        var window = new ImportWindow(new MainWindow(), new AppSettings { PlexClientId = "preview" });
        if (done)
        {
            window.PreviewResult(new WinampInspiredVisualizer.Import.LibraryImportResult
            {
                Source = WinampInspiredVisualizer.Import.ImportSource.AppleMusic,
                Songs = 2140,
                Found = 2093,
                StatsUpdated = 1876,
                Playlists = new[] { "Road trip", "Late nights", "Gym" },
                Missing = Enumerable.Range(1, 47).Select(i => $"Artist – Song {i}").ToList(),
                FolderToLink = "/Users/sam/Music/iTunes/iTunes Media/Music",
                FolderToLinkSongs = 312,
            }, newFavorites: 58);
        }

        return window;
    }

    /// <summary>The Record video window for a song partway through, starting on Crystal Cavern.</summary>
    private static Window PreviewRecordVideo()
    {
        var presets = new Lucent.Mac.Visuals.MetalShaderFiles().LoadPresets();
        var start = presets.ToList().FindIndex(preset => preset.Name == "Crystal Cavern");
        var template = new VideoRecordingOptions
        {
            TrackPath = Path.Combine(SampleFolder(), "Night Drive.flac"),
            OutputPath = string.Empty,
            Presets = presets,
            PresetIndex = Math.Max(0, start),
        };
        return VideoExportWindow.Prepare(template, "Aurora Fields · Night Drive", TimeSpan.FromSeconds(245), TimeSpan.FromSeconds(72));
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
        var editor = OpenPreviewEditor();
        editor.ShowPreviewEdits(view);
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    /// <summary>The audio editor with Beats on: the bars drawn over the waveform, and a four-bar loop selected.</summary>
    private static Window PreviewEditorBeats()
    {
        var editor = OpenPreviewEditor();
        editor.ShowPreviewLoop();
        Dispatcher.UIThread.RunJobs();
        return editor;
    }

    /// <summary>The loop maker (normally a pop-up under its button), with the best four-bar loops found.</summary>
    private static Window PreviewLoopMaker()
    {
        var editor = OpenPreviewEditor();
        editor.ShowPreviewLoop();
        var card = editor.TakeLoopMakerCard();
        editor.Close();
        return new Window
        {
            Width = 440,
            SizeToContent = SizeToContent.Height,
            Background = Avalonia.Media.Brushes.Transparent,
            Content = card,
        };
    }

    private static EditorWindow OpenPreviewEditor()
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
        return editor;
    }

    /// <summary>The editor's effects menu, which opens above its Effects button, in a window of its own so it can be drawn.</summary>
    private static Window PreviewEditorEffects()
    {
        var editor = (EditorWindow)PreviewEditor(EditorView.Waveform);
        var card = editor.PreviewEffects(string.Empty);
        editor.Close();
        return new Window
        {
            Width = 632,
            SizeToContent = SizeToContent.Height,
            Background = Avalonia.Media.Brushes.Transparent,
            Content = card,
        };
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
