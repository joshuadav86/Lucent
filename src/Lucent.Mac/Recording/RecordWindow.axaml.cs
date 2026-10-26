using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor.Plugins;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Recording;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// Records from an audio interface, DJ mixer or microphone into a WAV file: a DJ set played live, say, with
/// the visuals reacting to it. The input, its channels, sample rate and bit depth; live VST3 plugins the
/// sound goes through on its way to the file; how the file is named and where it's saved; and track marks,
/// made by hand or by Lucent hearing the song change, so the set can be skipped through song by song, and
/// named after their songs by ACRCloud with the DJ's own key.
/// </summary>
/// <remarks>
/// The input is opened as soon as the window is, so the level can be set before recording, and the visuals
/// follow it (instead of the player or other apps) until the window closes. The sound's path is in
/// <see cref="LiveRecorder"/>; this window only makes the choices and shows what's happening.
/// The Mac version of the Windows app's RecordWindow.
/// </remarks>
public partial class RecordWindow : Window
{
    private static readonly IBrush ClipBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private const string AcrCloudConsole = "https://console.acrcloud.com/";

    private readonly AudioPlayer _player;
    private readonly AppSettings _appSettings;
    private readonly RecordingSettings _settings = RecordingSettings.Load();
    private readonly PluginChain _chain = new();
    private readonly DispatcherTimer _timer;
    private IReadOnlyList<RecordingInput> _inputs = Array.Empty<RecordingInput>();
    private RecordingInput? _input;
    private InputDetails? _details;
    private LiveRecorder? _recorder;
    private LiveInput? _live;
    private AudioQueueOutput? _speakers;
    private PluginCatalog? _catalog;
    private FinishedRecording? _finished;
    private int _pluginFormat;          // the rate × channels the open plugins were started at
    private int _shownTracks = -1;
    // Stereo with sound on one side only (a mono mic in input 1, say): which side is silent, and since when.
    private int? _silentSide;
    private DateTime _lopsidedSince;
    // Read from the keychain as the input opens (not for a screenshot, where it could ask).
    private AcrCloudKey? _key;
    private double _levelLeft;
    private double _levelRight;
    private (double Level, DateTime At) _holdLeft;
    private (double Level, DateTime At) _holdRight;
    private bool _clipLit;
    private bool _busyWithPlugins;
    private bool _closeAnyway;
    private bool _stopping;
    private bool _preview;

    /// <summary>For the XAML loader only.</summary>
    public RecordWindow()
        : this(new AudioPlayer(), new AppSettings())
    {
    }

    public RecordWindow(AudioPlayer player, AppSettings appSettings)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _player = player;
        _appSettings = appSettings;
        NameInput.Text = _settings.Name;
        TemplateInput.Text = _settings.Template;
        ListenSwitch.IsChecked = _settings.Listen;
        DrySwitch.IsChecked = _settings.KeepDryCopy;
        DetectSwitch.IsChecked = _settings.DetectSongChanges;
        CueSwitch.IsChecked = _settings.WriteCueSheet;
        LibrarySwitch.IsChecked = _settings.AddToLibrary;
        IdentifySwitch.IsChecked = _settings.IdentifySongs;
        ShowSongId();
        foreach (var (token, meaning) in RecordingNames.Tokens)
        {
            var button = new Button
            {
                Theme = FindTheme("PillButton"),
                Content = token,
                Margin = new Thickness(0, 0, 6, 6),
            };
            ToolTip.SetTip(button, $"Adds {meaning}");
            button.Click += (_, _) => InsertToken(token);
            TokenPanel.Children.Add(button);
        }

        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(40) };
        _timer.Tick += (_, _) => ShowProgress();
        Opened += async (_, _) => await StartAsync();
        Closing += OnClosing;
        Closed += (_, _) => TearDown();
        KeyDown += OnKey;
    }

    /// <summary>True while a file is being written (or finished as the window closes).</summary>
    public bool IsBusy => _recorder?.IsRecording == true || _stopping;

    /// <summary>Closes without asking, finishing the file being recorded first (Lucent is quitting).</summary>
    public void StopAndClose()
    {
        _closeAnyway = true;
        Close();
    }

    // ---- The input ----

    /// <summary>
    /// The window part way through recording a set, with nothing opened, for <c>Lucent --screenshots</c> to draw.
    /// </summary>
    internal static RecordWindow PreparePreview()
    {
        var window = new RecordWindow(new AudioPlayer(), new AppSettings()) { _preview = true };
        window._input = new RecordingInput("preview", "DJM-A9");
        window._details = new InputDetails(4, RecordingSettings.SampleRates.Take(4).ToList(), null);
        window.InputText.Text = "DJM-A9";
        window.NameInput.Text = "Friday at the Loft";
        window.ShowFormat();
        window.ShowPlugins();
        window.ShowRecording(true);
        window.TimeText.Text = "1:12:40";
        window.StatusText.Text = "Recording Friday at the Loft 2026-10-20 21-30.wav  ·  1.2 GB  ·  Track 4";
        window._key = new AcrCloudKey("identify-eu-west-1.acrcloud.com", "preview-key-7f3a", "secret");
        window.ShowSongId();
        window.ShowTracks(new[]
        {
            new TrackMarker(0, "Lumen Avenue - Slow Tide"),
            new TrackMarker(48000L * 291, "Mara Velde - Afterhours (Club Mix)"),
            new TrackMarker(48000L * 1110, "Track 3", Automatic: true),
            new TrackMarker(48000L * 3874, "Track 4"),
        });
        window._levelLeft = 0.82;
        window._levelRight = 0.78;
        window._holdLeft = (0.9, DateTime.UtcNow.AddHours(1));
        window._holdRight = (0.88, DateTime.UtcNow.AddHours(1));
        return window;
    }

    private async Task StartAsync()
    {
        if (_preview)
        {
            ShowMeters(0, 0, false);
            return;
        }

        InputText.Text = "Looking for inputs…";
        _key = await Task.Run(SongIdAccount.Load);
        ShowSongId();
        _inputs = await Task.Run(LiveInputs.List);
        _input = LiveInputs.Find(_settings.InputId, _inputs);
        await OpenInputAsync();
        _timer.Start();
        if (_settings.Plugins is { Plugins.Count: > 0 } saved)
        {
            await LoadChainAsync(saved);
        }
    }

    /// <summary>Opens the chosen input in the chosen format, closing what was open. Not while recording.</summary>
    private async Task OpenInputAsync()
    {
        CloseInput();
        ProblemText.IsVisible = false;
        if (_input is null)
        {
            InputText.Text = "No inputs found";
            ShowFormat();
            ShowProblem("This Mac has no inputs. Plug in an audio interface or mixer, then choose it here.");
            return;
        }

        if (!LiveInputs.IsAllowed())
        {
            ShowFormat();
            ShowProblem("Lucent isn't allowed to record. Turn Lucent on in System Settings, Privacy & Security, Microphone, then open this window again.");
            return;
        }

        InputText.Text = _input.Name;
        try
        {
            var input = _input;
            _details = await Task.Run(() => LiveInputs.Describe(input));
        }
        catch (Exception ex)
        {
            _details = null;
            ShowFormat();
            ShowProblem($"{_input.Name} couldn't be opened: {ex.Message}");
            return;
        }

        FitFormat();
        ShowFormat();
        try
        {
            var recorder = new LiveRecorder(_player.Capture, _settings.SampleRate, _settings.Channels)
            {
                BitDepth = _settings.BitDepth,
                DetectSongChanges = _settings.DetectSongChanges,
                Identifier = MakeIdentifier(),
                SumPair = _settings.SumToMono,
            };
            recorder.Failed += (_, ex) => Dispatcher.UIThread.Post(() => OnRecordingFailed(ex));
            _recorder = recorder;
            _live = LiveInput.Start(_input, recorder, _settings.FirstChannel);
            ShowFormat();
        }
        catch (Exception ex)
        {
            ShowProblem($"{_input.Name} couldn't be opened for recording: {ex.Message}");
            CloseInput();
            return;
        }

        await FitPluginsAsync();
        UpdateListening();
    }

    private void CloseInput()
    {
        _speakers?.Dispose();
        _speakers = null;
        _live?.Dispose();
        _live = null;
        _recorder?.Dispose();
        _recorder = null;
    }

    // Keeps the chosen channels and rate to ones this input has.
    private void FitFormat()
    {
        if (_details is not { } details)
        {
            return;
        }

        if (_settings.FirstChannel + InputsTaken(_settings.Channels, _settings.SumToMono) > details.Channels)
        {
            _settings.FirstChannel = 0;
            _settings.Channels = Math.Min(2, details.Channels);
            _settings.SumToMono = false;
        }

    }

    private void OnChooseInput(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var input in _inputs)
        {
            var item = new MenuItem { Header = input.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = input == _input };
            item.Click += async (_, _) =>
            {
                _input = input;
                _settings.InputId = input.Id;
                _settings.FirstChannel = 0;
                _settings.Channels = 2;
                _settings.SumToMono = false;
                _settings.Save();
                await OpenInputAsync();
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var refresh = new MenuItem { Header = "Look for inputs again" };
        refresh.Click += async (_, _) =>
        {
            _inputs = await Task.Run(LiveInputs.List);
            _input = LiveInputs.Find(_input?.Id ?? _settings.InputId, _inputs);
            await OpenInputAsync();
        };
        menu.Items.Add(refresh);
        var midi = new MenuItem { Header = "Audio MIDI Setup…" };
        midi.Click += (_, _) => OpenUrl("/System/Applications/Utilities/Audio MIDI Setup.app");
        menu.Items.Add(midi);
        menu.Open(InputButton);
    }

    private void OnChooseChannels(object? sender, RoutedEventArgs e)
    {
        var count = _details?.Channels ?? 2;
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var (first, channels, summed) in ChannelChoices(count))
        {
            var item = new MenuItem
            {
                Header = ChannelName(first, channels, summed),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = first == _settings.FirstChannel && channels == _settings.Channels && summed == _settings.SumToMono,
            };
            item.Click += async (_, _) =>
            {
                await UseChannelsAsync(first, channels, summed);
            };
            menu.Items.Add(item);
        }

        menu.Open(ChannelsButton);
    }

    /// <summary>Stereo pairs first (1 + 2, 3 + 4…), then each channel on its own, then each pair mixed to mono.</summary>
    private static IEnumerable<(int First, int Channels, bool Summed)> ChannelChoices(int count)
    {
        for (var first = 0; first + 1 < count; first += 2)
        {
            yield return (first, 2, false);
        }

        for (var channel = 0; channel < count; channel++)
        {
            yield return (channel, 1, false);
        }

        // A mono mic or mixer out on both sides of a pair, or a stereo source folded down to one channel.
        for (var first = 0; first + 1 < count; first += 2)
        {
            yield return (first, 1, true);
        }
    }

    private static string ChannelName(int first, int channels, bool summed) => (channels, summed) switch
    {
        (2, _) => $"Inputs {first + 1} + {first + 2} (stereo)",
        (_, true) => $"Inputs {first + 1} + {first + 2} mixed to mono",
        _ => $"Input {first + 1} (mono)",
    };

    // How many of the input's channels a choice takes.
    private static int InputsTaken(int channels, bool summed) => summed ? 2 : channels;

    private async Task UseChannelsAsync(int first, int channels, bool summed)
    {
        _settings.FirstChannel = first;
        _settings.Channels = channels;
        _settings.SumToMono = channels == 1 && summed;
        _settings.Save();
        _silentSide = null;
        await OpenInputAsync();
    }

    private void ShowFormat()
    {
        ChannelsText.Text = ChannelName(_settings.FirstChannel, _settings.Channels, _settings.SumToMono);
        RatePanel.Children.Clear();
        foreach (var rate in RecordingSettings.SampleRates)
        {
            var native = _details is null || _details.SampleRates.Contains(rate);
            var radio = new RadioButton
            {
                Theme = FindTheme("SegmentButton"),
                GroupName = "Rate",
                Content = (rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture),
                IsChecked = rate == _settings.SampleRate,
            };
            ToolTip.SetTip(radio, native ? $"{rate:N0} samples a second" : "The interface can't run at this rate, so macOS converts to it.");
            radio.Click += async (_, _) =>
            {
                _settings.SampleRate = rate;
                _settings.Save();
                await OpenInputAsync();
            };
            RatePanel.Children.Add(radio);
        }

        BitsPanel.Children.Clear();
        foreach (var bits in RecordingSettings.BitDepths)
        {
            var radio = new RadioButton
            {
                Theme = FindTheme("SegmentButton"),
                GroupName = "Bits",
                Content = bits == 32 ? "32-bit float" : $"{bits}-bit",
                IsChecked = bits == _settings.BitDepth,
            };
            radio.Click += (_, _) =>
            {
                _settings.BitDepth = bits;
                _settings.Save();
                if (_recorder is { IsRecording: false } recorder)
                {
                    recorder.BitDepth = bits;
                }

                ShowFormat();
            };
            BitsPanel.Children.Add(radio);
        }

        var rateText = Kilohertz(_settings.SampleRate);
        var hint = _settings.BitDepth switch
        {
            16 => "16-bit is CD quality.",
            24 => "24-bit leaves room to record quieter without hiss: peaks around −6 dB are plenty.",
            _ => "32-bit float can't clip in the file, though the interface itself still can.",
        };
        if (_details is { } details && !details.SampleRates.Contains(_settings.SampleRate))
        {
            var native = details.NativeRate ?? details.SampleRates.FirstOrDefault();
            hint = $"{_input?.Name} can't run at {rateText}, so macOS converts it. To record it untouched, choose {Kilohertz(native)}. " + hint;
        }
        else if (_details is not null)
        {
            hint = $"{_input?.Name} is set to {rateText} while this window is open. " + hint;
        }

        var perHour = BytesPerSecond() * 3600.0;
        FormatHint.Text = hint + $" About {Size((long)perHour)} an hour.";
        ShowFolder();
    }

    private long BytesPerSecond() =>
        (long)_settings.SampleRate * _settings.Channels * (_settings.BitDepth / 8) * (DrySwitch.IsChecked == true ? 2 : 1);

    // ---- Listening ----

    private void OnListen(object? sender, RoutedEventArgs e)
    {
        _settings.Listen = ListenSwitch.IsChecked == true;
        _settings.Save();
        UpdateListening();
    }

    private void UpdateListening()
    {
        _speakers?.Dispose();
        _speakers = null;
        var device = OutputDevices.Open(_appSettings.OutputDevice);
        var outputName = device?.Name ?? "the speakers";
        ListenSwitch.Content = $"Hear it through {outputName}";
        if (_recorder is not { } recorder || !_settings.Listen)
        {
            _recorder?.ListenThrough(false);
            ListenHint.Text = "Off: listen on your mixer or interface, with no delay. On, you hear the plugins too, a moment late.";
            return;
        }

        try
        {
            if (device is null)
            {
                throw new IOException("There's no output to play through.");
            }

            var output = new AudioQueueOutput(40, device.Id);
            output.Init(recorder.ListenThrough(true)!);
            output.Play();
            _speakers = output;
            ListenHint.Text = "After the plugins, as it's recorded. Turn it off if you hear an echo with your mixer's own monitoring.";
        }
        catch (Exception ex)
        {
            recorder.ListenThrough(false);
            ListenHint.Text = $"Couldn't play through {outputName}: {ex.Message}";
        }
    }

    // ---- Live plugins ----

    private async void OnAddPlugin(object? sender, RoutedEventArgs e)
    {
        if (await EnsureCatalogAsync() is not { } catalog)
        {
            return;
        }

        var editorSettings = EditorSettings.Load();
        var entries = PluginBrowser.Entries(catalog.Plugins, editorSettings);
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        if (entries.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No VST3 effects were found", IsEnabled = false });
        }

        var favorites = entries.Where(entry => entry.IsFavorite).ToList();
        foreach (var entry in favorites)
        {
            menu.Items.Add(PluginItem(entry));
        }

        if (favorites.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var (heading, plugins) in PluginBrowser.Group(entries, byMaker: true))
        {
            var group = new MenuItem { Header = heading };
            foreach (var entry in plugins)
            {
                group.Items.Add(PluginItem(entry));
            }

            menu.Items.Add(group);
        }

        menu.Items.Add(new Separator());
        var rescan = new MenuItem { Header = "Look for plugins again" };
        rescan.Click += async (_, _) => await ScanAsync(everything: true);
        menu.Items.Add(rescan);
        menu.Open(AddPluginButton);
    }

    private MenuItem PluginItem(PluginEntry entry)
    {
        var item = new MenuItem { Header = entry.Plugin.Name };
        ToolTip.SetTip(item, entry.Type);
        item.Click += async (_, _) => await AddPluginAsync(entry.Plugin, PluginCatalog.LoadSettings(entry.Plugin), bypassed: false);
        return item;
    }

    private async Task<PluginCatalog?> EnsureCatalogAsync()
    {
        if (!PluginHost.IsAvailable)
        {
            PluginHint.Text = "Lucent's plugin helper is missing from the app, so plugins can't be used. Reinstalling Lucent puts it back.";
            return null;
        }

        _catalog ??= await Task.Run(PluginCatalog.Load);
        var catalog = _catalog;
        if ((await Task.Run(catalog.FindUnscanned)).Count > 0)
        {
            await ScanAsync(everything: false);
        }

        return catalog;
    }

    private async Task ScanAsync(bool everything)
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var progress = new Progress<double>(fraction => PluginHint.Text = $"Looking for VST3 plugins… {fraction:P0}");
        try
        {
            await catalog.ScanAsync(everything, progress, CancellationToken.None);
            PluginHint.Text = $"Found {catalog.Plugins.Count} VST3 effect{(catalog.Plugins.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception ex)
        {
            PluginHint.Text = $"Couldn't look for plugins: {ex.Message}";
        }
    }

    /// <summary>Opens a plugin at the recording's format and adds it to the end of the chain.</summary>
    private async Task AddPluginAsync(PluginInfo plugin, byte[]? settings, bool bypassed)
    {
        if (_busyWithPlugins)
        {
            return;
        }

        _busyWithPlugins = true;
        PluginHint.Text = $"Opening {plugin.Name}…";
        try
        {
            var rate = _settings.SampleRate;
            var channels = _settings.Channels;
            var host = await Task.Run(() => OpenHost(plugin, settings, rate, channels));
            _chain.Add(new ChainSlot(plugin, host) { IsBypassed = bypassed });
            _pluginFormat = rate * 10 + channels;
            _recorder?.SetPlugins(_chain.Slots);
            ShowPlugins();
            if (host.HasWindow && !bypassed && settings is null)
            {
                await ShowPluginWindowAsync(_chain.Slots[^1]);
            }
        }
        catch (Exception ex)
        {
            PluginHint.Text = ex is PluginException ? ex.Message : $"{plugin.Name} couldn't be opened: {ex.Message}";
        }
        finally
        {
            _busyWithPlugins = false;
        }
    }

    // Starts a plugin's helper, gives it its settings and gets it ready to run live. Waits, so call it off the UI thread.
    private static PluginHost OpenHost(PluginInfo plugin, byte[]? settings, int rate, int channels)
    {
        var host = PluginHost.Open(plugin, rate, channels);
        try
        {
            if (settings is not null)
            {
                try
                {
                    host.SetSettings(settings);
                }
                catch (PluginException) when (!host.IsGone)
                {
                    // Settings from another version of it: it starts as new.
                }
            }

            host.Start(offline: false);
            return host;
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    /// <summary>Opens the plugins again when the rate or channels change, keeping their settings.</summary>
    private async Task FitPluginsAsync()
    {
        var format = _settings.SampleRate * 10 + _settings.Channels;
        if (_chain.Count == 0 || format == _pluginFormat)
        {
            _recorder?.SetPlugins(_chain.Slots);
            return;
        }

        var old = _chain.Clear();
        _recorder?.SetPlugins(Array.Empty<ChainSlot>());
        ShowPlugins();
        var kept = await Task.Run(() => old.Select(slot =>
        {
            byte[]? settings = null;
            try
            {
                settings = slot.Host.IsGone ? null : slot.Host.GetSettings();
            }
            catch (PluginException)
            {
                // It starts with its own defaults.
            }

            slot.Host.Dispose();
            return (slot.Plugin, settings, slot.IsBypassed);
        }).ToList());
        foreach (var (plugin, settings, bypassed) in kept)
        {
            await AddPluginAsync(plugin, settings, bypassed);
        }
    }

    private void OnChains(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var saved = ChainPreset.Saved();
        if (saved.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No chains saved yet: save one in the editor's Inserts", IsEnabled = false });
        }

        foreach (var (name, path) in saved)
        {
            var item = new MenuItem { Header = name };
            item.Click += async (_, _) =>
            {
                try
                {
                    await ClearPluginsAsync();
                    await LoadChainAsync(ChainPreset.Load(path));
                }
                catch (Exception ex)
                {
                    PluginHint.Text = $"Couldn't open {name}: {ex.Message}";
                }
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Take out every plugin", IsEnabled = _chain.Count > 0 };
        clear.Click += async (_, _) => await ClearPluginsAsync();
        menu.Items.Add(clear);
        menu.Open(ChainsButton);
    }

    private async Task LoadChainAsync(ChainPreset preset)
    {
        if (await EnsureCatalogAsync() is not { } catalog)
        {
            return;
        }

        var missing = new List<string>();
        foreach (var resolved in preset.Resolve(catalog.Plugins))
        {
            if (resolved.Plugin is { } plugin)
            {
                await AddPluginAsync(plugin, resolved.Wanted.SettingsBytes(), resolved.Wanted.Bypassed);
            }
            else
            {
                missing.Add(resolved.Wanted.Name);
            }
        }

        if (missing.Count > 0)
        {
            PluginHint.Text = $"Not installed here, so left out: {string.Join(", ", missing)}.";
        }
    }

    private async Task ClearPluginsAsync()
    {
        var slots = _chain.Clear();
        _recorder?.SetPlugins(_chain.Slots);
        ShowPlugins();
        await Task.Run(() =>
        {
            foreach (var slot in slots)
            {
                slot.Host.Dispose();
            }
        });
    }

    private void ShowPlugins()
    {
        PluginList.Children.Clear();
        var slots = _chain.Slots;
        for (var i = 0; i < slots.Count; i++)
        {
            PluginList.Children.Add(PluginRow(slots[i], i));
        }

        if (!_busyWithPlugins)
        {
            PluginHint.Text = slots.Count == 0
                ? "VST3 effects the sound goes through on its way to the file, top to bottom: a limiter, an EQ, a compressor."
                : "What's recorded is what comes out of the last one. Click a name to show its window; changes are heard straight away.";
        }
    }

    private Control PluginRow(ChainSlot slot, int index)
    {
        var power = new CheckBox
        {
            Theme = FindTheme("SwitchToggle"),
            IsChecked = !slot.IsBypassed,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(power, "Switch it off and the sound goes past it");
        power.Click += (_, _) =>
        {
            slot.IsBypassed = power.IsChecked != true;
            ShowPlugins();
        };
        var name = new Button
        {
            Theme = FindTheme("PickerButton"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new TextBlock { Text = $"{index + 1}.  {slot.Title}", TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 },
            Opacity = slot.IsBypassed ? 0.5 : 1,
            Margin = new Thickness(8, 0, 6, 0),
        };
        ToolTip.SetTip(name, slot.Host.HasWindow ? "Show its window" : "It has no window of its own");
        name.Click += async (_, _) => await ShowPluginWindowAsync(slot);
        var remove = new Button
        {
            Theme = FindTheme("IconButton"),
            Content = new Avalonia.Controls.Shapes.Path { Theme = FindTheme("LineIcon"), Data = ThemeIcon("CloseIcon") },
        };
        ToolTip.SetTip(remove, "Take it out");
        remove.Click += async (_, _) =>
        {
            var at = _chain.Slots.ToList().IndexOf(slot);
            if (at < 0)
            {
                return;
            }

            _chain.RemoveAt(at);
            _recorder?.SetPlugins(_chain.Slots);
            ShowPlugins();
            await Task.Run(() =>
            {
                try
                {
                    if (!slot.Host.IsGone)
                    {
                        PluginCatalog.SaveSettings(slot.Plugin, slot.Host.GetSettings());
                    }
                }
                catch (Exception)
                {
                    // Only its remembered settings are lost.
                }

                slot.Host.Dispose();
            });
        };
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(power, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        row.Children.Add(power);
        row.Children.Add(remove);
        row.Children.Add(name);
        return row;
    }

    private async Task ShowPluginWindowAsync(ChainSlot slot)
    {
        if (!slot.Host.HasWindow || slot.Host.IsGone)
        {
            return;
        }

        // The helper is another process, so it's told this window's number, which it opens the plugin's window above.
        var owner = (IntPtr)WindowNumber();
        try
        {
            await Task.Run(() => slot.Host.ShowWindow(owner, $"{slot.Host.Name} - Record - Lucent"));
        }
        catch (PluginException ex)
        {
            PluginHint.Text = ex.Message;
        }
    }

    // ---- Naming and where it goes ----

    private void InsertToken(string token)
    {
        var at = TemplateInput.CaretIndex;
        var text = TemplateInput.Text ?? string.Empty;
        at = Math.Clamp(at, 0, text.Length);
        var spaced = (at > 0 && text[at - 1] != ' ' ? " " : string.Empty) + token;
        TemplateInput.Text = text.Insert(at, spaced);
        TemplateInput.CaretIndex = at + spaced.Length;
        TemplateInput.Focus();
    }

    private void OnNameChanged(object? sender, TextChangedEventArgs e)
    {
        if (!IsLoaded || _timer is null)
        {
            return;
        }

        _settings.Name = NameInput.Text ?? string.Empty;
        _settings.Template = TemplateInput.Text ?? string.Empty;
        _settings.Save();
        ShowFolder();
    }

    private string Folder => string.IsNullOrWhiteSpace(_settings.Folder) ? RecordingSettings.DefaultFolder : _settings.Folder;

    private RecordingNameParts NameParts(DateTime started) =>
        new(string.IsNullOrWhiteSpace(NameInput.Text) ? "Recording" : NameInput.Text!, started, _settings.SampleRate, _settings.BitDepth,
            _input?.Name ?? "Input");

    private void ShowFolder()
    {
        if (FileNameText is null)
        {
            return;
        }

        FolderText.Text = Folder;
        ToolTip.SetTip(FolderText, Folder);
        var next = RecordingNames.NextPath(Directory.Exists(Folder) ? Folder : Path.GetTempPath(), TemplateInput.Text ?? string.Empty, NameParts(DateTime.Now));
        FileNameText.Text = (_recorder?.IsRecording == true ? "Next: " : string.Empty) + Path.GetFileName(next);
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(Folder));
            var free = root is null ? -1 : new DriveInfo(root).AvailableFreeSpace;
            SpaceHint.Text = free < 0 ? string.Empty
                : $"{Size(free)} free there: room for about {Hours(free / (double)Math.Max(1, BytesPerSecond()))} at this quality.";
        }
        catch (Exception)
        {
            SpaceHint.Text = string.Empty;
        }
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose where recordings are saved",
                SuggestedStartLocation = Directory.Exists(Folder) ? await StorageProvider.TryGetFolderFromPathAsync(Folder) : null,
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                _settings.Folder = path;
                _settings.Save();
                ShowFolder();
            }
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Choosing where recordings go", ex);
        }
    }

    private void OnOptionSwitch(object? sender, RoutedEventArgs e)
    {
        _settings.KeepDryCopy = DrySwitch.IsChecked == true;
        _settings.DetectSongChanges = DetectSwitch.IsChecked == true;
        _settings.WriteCueSheet = CueSwitch.IsChecked == true;
        _settings.AddToLibrary = LibrarySwitch.IsChecked == true;
        _settings.IdentifySongs = IdentifySwitch.IsChecked == true;
        _settings.Save();
        if (_recorder is { IsRecording: false } recorder)
        {
            recorder.DetectSongChanges = _settings.DetectSongChanges;
        }

        // Song lookups can be turned on or off part way through a set.
        if (_recorder is { } open)
        {
            open.Identifier = MakeIdentifier();
        }

        ShowSongId();

        ShowFormat();
    }

    // ---- Recording ----

    private async void OnRecord(object? sender, RoutedEventArgs e) => await ToggleRecordingAsync();

    private async Task ToggleRecordingAsync()
    {
        if (_recorder is not { } recorder || _stopping)
        {
            return;
        }

        if (recorder.IsRecording)
        {
            await StopRecordingAsync();
            return;
        }

        try
        {
            Directory.CreateDirectory(Folder);
        }
        catch (Exception ex)
        {
            ShowProblem($"Couldn't make the folder {Folder}: {ex.Message}");
            return;
        }

        recorder.BitDepth = _settings.BitDepth;
        recorder.DetectSongChanges = _settings.DetectSongChanges;
        recorder.Identifier = MakeIdentifier();
        recorder.Start(NextTarget());
        _finished = null;
        _shownTracks = -1;
        ProblemText.IsVisible = false;
        ShowRecording(true);
    }

    private RecordingTarget NextTarget()
    {
        var path = RecordingNames.NextPath(Folder, TemplateInput.Text ?? string.Empty, NameParts(DateTime.Now));
        return new RecordingTarget(path, Path.GetFileNameWithoutExtension(path), _settings.KeepDryCopy ? RecordingNames.DryPath(path) : null, _settings.WriteCueSheet);
    }

    private async Task StopRecordingAsync()
    {
        if (_recorder is not { IsRecording: true } recorder)
        {
            return;
        }

        _stopping = true;
        StatusText.Text = "Finishing the file…";
        try
        {
            ShowFinished(await recorder.StopAsync());
        }
        finally
        {
            _stopping = false;
            ShowRecording(false);
        }
    }

    private async void OnSplit(object? sender, RoutedEventArgs e) => await SplitAsync();

    private async Task SplitAsync()
    {
        if (_recorder is not { IsRecording: true } recorder || _stopping)
        {
            return;
        }

        var finished = await recorder.SplitAsync(NextTarget());
        _shownTracks = -1;
        if (finished is not null)
        {
            AddToLibrary(finished);
            StatusText.Text = $"Saved {Path.GetFileName(finished.Path)}; carrying on in a new file.";
        }
    }

    private void OnMark(object? sender, RoutedEventArgs e) => _recorder?.MarkTrack();

    private void ShowRecording(bool recording)
    {
        RecordText.Text = recording ? "Stop" : "Record";
        ToolTip.SetTip(RecordButton, recording ? "Stop recording and save the file (R)" : "Start recording (R)");
        MarkButton.IsEnabled = recording;
        SplitButton.IsEnabled = recording;
        foreach (var control in new Control[] { InputButton, ChannelsButton, RatePanel, BitsPanel, DrySwitch, FolderButton, DetectSwitch, CueSwitch })
        {
            control.IsEnabled = !recording;
        }

        if (recording)
        {
            ShowFileButton.IsVisible = false;
            QueueButton.IsVisible = false;
        }

        ShowFolder();
    }

    private void ShowFinished(FinishedRecording? finished)
    {
        _finished = finished;
        if (finished is null)
        {
            return;
        }

        AddToLibrary(finished);
        var tracks = finished.Tracks.Count;
        StatusText.Text = $"Saved {Path.GetFileName(finished.Path)}: {TimeFormat.Format(finished.Duration)}" +
                          (tracks > 1 ? $", {tracks} tracks." : ".");
        ShowFileButton.IsVisible = true;
        QueueButton.IsVisible = true;
    }

    // Finished recordings show up in the library (and the folder is watched for the next ones), under the folder's own heading.
    private void AddToLibrary(FinishedRecording finished)
    {
        if (_settings.AddToLibrary && (Editor.AudioTools.MainWindow) is { } main)
        {
            main.AddRecordingToLibrary(finished.Path);
        }
    }

    private void OnRecordingFailed(Exception ex)
    {
        ShowRecording(false);
        ShowProblem($"Recording stopped: {ex.Message} What was recorded up to then is saved.");
    }

    private void OnShowFile(object? sender, RoutedEventArgs e)
    {
        if (_finished is { } finished)
        {
            try
            {
                // Opens the folder in Finder with the recording selected.
                var open = new ProcessStartInfo("open") { UseShellExecute = false };
                open.ArgumentList.Add("-R");
                open.ArgumentList.Add(finished.Path);
                Process.Start(open)?.Dispose();
            }
            catch (Exception)
            {
                // Finder normally opens; nothing more to do if it doesn't.
            }
        }
    }

    private void OnAddToQueue(object? sender, RoutedEventArgs e)
    {
        if (_finished is { } finished && Editor.AudioTools.MainWindow is { } main && File.Exists(finished.Path))
        {
            main.AddToQueue(new[] { WinampInspiredVisualizer.Playlist.Track.FromFile(finished.Path) });
            StatusText.Text = $"{Path.GetFileName(finished.Path)} is in the queue.";
        }
    }

    // ---- What's happening ----

    private void ShowProgress()
    {
        if (_recorder is not { } recorder)
        {
            ShowMeters(0, 0, false);
            return;
        }

        var (left, right, clipped) = recorder.ReadMeters();
        ShowMeters(left, right, clipped);
        WatchForMono(left, right);
        if (recorder.IsRecording)
        {
            TimeText.Text = Clock(recorder.Elapsed);
            var tracks = recorder.Tracks;
            if (!_stopping)
            {
                StatusText.Text = $"Recording {Path.GetFileName(recorder.CurrentPath)}  ·  {Size(recorder.Bytes)}  ·  {(tracks.Count > 0 ? tracks[^1].Title : "Track 1")}";
            }

            if (recorder.TracksVersion != _shownTracks)
            {
                _shownTracks = recorder.TracksVersion;
                ShowTracks(tracks);
            }

            RecordDot.Opacity = DateTime.Now.Millisecond < 500 ? 1 : 0.35;
        }
        else
        {
            RecordDot.Opacity = 1;
        }

        if (recorder.Dropped > TimeSpan.Zero)
        {
            ShowProblem($"The computer fell behind and {recorder.Dropped.TotalSeconds:0.0} seconds of sound were lost. Closing other apps or taking out a heavy plugin may help.");
        }
        else if (recorder.PluginProblem is { } problem)
        {
            ShowProblem(problem);
        }
        else if (recorder.IdentifyProblem is { } lookupProblem)
        {
            ShowProblem($"Songs can't be named: {lookupProblem}");
        }
    }

    private void ShowMeters(float left, float right, bool clipped)
    {
        // Falls back gently, and the peak line holds for a moment, like a hardware meter.
        _levelLeft = Math.Max(Fraction(left), _levelLeft - 0.02);
        _levelRight = Math.Max(Fraction(right), _levelRight - 0.02);
        var now = DateTime.UtcNow;
        if (_levelLeft >= _holdLeft.Level || now - _holdLeft.At > TimeSpan.FromSeconds(1.5))
        {
            _holdLeft = (_levelLeft, now);
        }

        if (_levelRight >= _holdRight.Level || now - _holdRight.At > TimeSpan.FromSeconds(1.5))
        {
            _holdRight = (_levelRight, now);
        }

        var width = Math.Max(0, LeftTrack.Bounds.Width);
        LeftFill.Width = width * _levelLeft;
        RightFill.Width = width * _levelRight;
        LeftHold.Margin = new Thickness(Math.Max(0, width * _holdLeft.Level - 2), 0, 0, 0);
        RightHold.Margin = new Thickness(Math.Max(0, width * _holdRight.Level - 2), 0, 0, 0);
        var peak = Math.Max(_holdLeft.Level, _holdRight.Level);
        PeakText.Text = peak <= 0 ? "−∞ dB" : $"{(peak - 1) * 60:0.0} dB".Replace('-', '−');
        if (clipped)
        {
            _clipLit = true;
        }

        ClipLight.Opacity = _clipLit ? 1 : 0.25;
    }

    // A meter's 60 dB range as 0 to 1.
    private static double Fraction(float peak) => peak <= 0 ? 0 : Math.Clamp(1 + 20 * Math.Log10(peak) / 60, 0, 1);

    private void OnClearClip(object? sender, PointerPressedEventArgs e) => _clipLit = false;

    private void ShowTracks(IReadOnlyList<TrackMarker> tracks)
    {
        TrackList.Children.Clear();
        foreach (var track in tracks)
        {
            var row = new DockPanel { Margin = new Thickness(2, 0, 0, 2) };
            if (track.Frame > 0)
            {
                var remove = new Button
                {
                    Theme = FindTheme("IconButton"),
                    Width = 22,
                    Height = 22,
                    Content = new Avalonia.Controls.Shapes.Path { Theme = FindTheme("LineIcon"), Data = ThemeIcon("CloseIcon") },
                };
                ToolTip.SetTip(remove, "Take this mark out");
                var frame = track.Frame;
                remove.Click += (_, _) =>
                {
                    _recorder?.RemoveMarker(frame);
                    _shownTracks = -1;
                };
                DockPanel.SetDock(remove, Dock.Right);
                row.Children.Add(remove);
            }

            var at = TimeSpan.FromSeconds((double)track.Frame / Math.Max(1, _recorder?.SampleRate ?? 48000));
            row.Children.Add(new TextBlock
            {
                Text = $"{track.Title}   {Clock(at)}{(track.Automatic ? "   heard by Lucent" : string.Empty)}",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush(track.Automatic ? "MutedTextBrush" : "TextBrush"),
            });
            TrackList.Children.Add(row);
        }
    }

    // A stereo recording with sound on one side only is most likely a mono mic or instrument in one input,
    // which would play in one ear: after a few seconds of that, the window offers to record that input in mono.
    private void WatchForMono(float left, float right)
    {
        int? silent = null;
        if (_settings.Channels == 2)
        {
            if (left > 0.003f && right < 0.0003f)
            {
                silent = 1;
            }
            else if (right > 0.003f && left < 0.0003f)
            {
                silent = 0;
            }
        }

        // Quiet moments say nothing either way; sound on both sides, or a mono choice, ends it.
        var quiet = left <= 0.003f && right <= 0.003f;
        if (silent is null && quiet && _settings.Channels == 2)
        {
            return;
        }

        if (silent != _silentSide)
        {
            _silentSide = silent;
            _lopsidedSince = DateTime.UtcNow;
            MonoHint.IsVisible = false;
            return;
        }

        if (silent is { } side && DateTime.UtcNow - _lopsidedSince > TimeSpan.FromSeconds(3))
        {
            var heard = _settings.FirstChannel + 1 + (1 - side);
            var other = _settings.FirstChannel + 1 + side;
            MonoHintText.Text = $"Only input {heard} has sound; input {other} is silent. A mono mic or instrument recorded in stereo plays in one ear.";
            MonoButton.Content = $"RECORD INPUT {heard} IN MONO";
            MonoButton.IsEnabled = _recorder?.IsRecording != true;
            MonoHint.IsVisible = true;
        }
    }

    private async void OnUseMono(object? sender, RoutedEventArgs e)
    {
        if (_silentSide is not { } side || _recorder?.IsRecording == true)
        {
            return;
        }

        MonoHint.IsVisible = false;
        await UseChannelsAsync(_settings.FirstChannel + (1 - side), 1, summed: false);
    }

    // ---- Naming the songs ----

    private SongIdentifier? MakeIdentifier() =>
        _settings.IdentifySongs && _key is { IsComplete: true } key ? new SongIdentifier(Http, key) : null;

    private void ShowSongId()
    {
        KeyButton.Content = _key is null ? "SET UP" : "KEY";
        RemoveKeyButton.IsEnabled = _key is not null;
        IdentifyHint.Text = _key is null
            ? "With your own ACRCloud key (SET UP), Lucent looks up each track's song about 25 seconds in and names its mark after it. ACRCloud has a free trial; after that lookups are paid on your account."
            : $"Each track is looked up about 25 seconds in, and again a minute later if it isn't known, with your key ending {Tail(_key.AccessKey)} ({_key.CleanHost}).";
    }

    private void OnKeyButton(object? sender, RoutedEventArgs e)
    {
        KeyPanel.IsVisible = !KeyPanel.IsVisible;
        if (KeyPanel.IsVisible)
        {
            HostInput.Text = _key?.CleanHost ?? string.Empty;
            AccessKeyInput.Text = _key?.AccessKey ?? string.Empty;
            // The secret isn't shown again; leaving it empty keeps the saved one.
            SecretInput.Text = string.Empty;
            HostInput.Focus();
        }
    }

    private void OnSaveKey(object? sender, RoutedEventArgs e)
    {
        var secret = string.IsNullOrWhiteSpace(SecretInput.Text) ? _key?.AccessSecret ?? string.Empty : SecretInput.Text;
        var key = new AcrCloudKey(HostInput.Text ?? string.Empty, (AccessKeyInput.Text ?? string.Empty).Trim(), secret.Trim());
        if (!key.IsComplete || !key.CleanHost.Contains('.', StringComparison.Ordinal))
        {
            ShowProblem("Fill in the host (like identify-eu-west-1.acrcloud.com), the access key, and the access secret from your ACRCloud project.");
            return;
        }

        if (!SongIdAccount.Save(key))
        {
            ShowProblem("The key couldn't be saved in the keychain. It's used until the window closes.");
        }
        else
        {
            ProblemText.IsVisible = false;
        }

        _key = key;
        SecretInput.Text = string.Empty;
        KeyPanel.IsVisible = false;
        IdentifySwitch.IsChecked = true;
        OnOptionSwitch(sender, e);
    }

    private void OnRemoveKey(object? sender, RoutedEventArgs e)
    {
        SongIdAccount.Clear();
        _key = null;
        HostInput.Text = AccessKeyInput.Text = SecretInput.Text = string.Empty;
        KeyPanel.IsVisible = false;
        OnOptionSwitch(sender, e);
    }

    private void OnOpenConsole(object? sender, RoutedEventArgs e) => OpenUrl(AcrCloudConsole);

    private static string Tail(string text) => text.Length <= 4 ? text : text[^4..];

    private void ShowProblem(string text)
    {
        ProblemText.Text = text;
        ProblemText.Foreground = ClipBrush;
        ProblemText.IsVisible = true;
    }

    // ---- Keys and closing ----

    private async void OnKey(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox || e.KeyModifiers != KeyModifiers.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.R:
                e.Handled = true;
                await ToggleRecordingAsync();
                break;
            case Key.M:
                e.Handled = true;
                _recorder?.MarkTrack();
                break;
            case Key.N:
                e.Handled = true;
                await SplitAsync();
                break;
        }
    }

    private void OnCloseButton(object? sender, RoutedEventArgs e) => Close();

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_recorder is not { IsRecording: true })
        {
            return;
        }

        e.Cancel = true;
        if (_stopping)
        {
            return;
        }

        if (!_closeAnyway && await MessageWindow.ShowAsync(this, "Stop recording and close? What's been recorded is saved.", "Record",
                MessageButtons.YesNo, MessageResult.No) != MessageResult.Yes)
        {
            return;
        }

        await StopRecordingAsync();
        Close();
    }

    // Saves the plugins for next time and lets go of everything: the input, the speakers, the plugins, and the visuals.
    private void TearDown()
    {
        _timer.Stop();
        if (_preview)
        {
            return;
        }

        CloseInput();
        var slots = _chain.Clear();
        var settings = _settings;
        _ = Task.Run(() =>
        {
            settings.Plugins = slots.Count > 0 ? PluginChain.ToPreset("Record", slots) : null;
            settings.Save();
            foreach (var slot in slots)
            {
                slot.Host.Dispose();
            }
        });
    }

    // ---- Formatting ----

    private static string Kilohertz(int rate) => (rate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz";

    private static string Clock(TimeSpan time) => $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}";

    private static string Size(long bytes) => bytes switch
    {
        >= 1L << 40 => $"{bytes / (double)(1L << 40):0.0} TB",
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        _ => $"{Math.Max(0, bytes) / (double)(1L << 20):0} MB",
    };

    private static string Hours(double seconds) =>
        seconds >= 7200 ? $"{seconds / 3600:N0} hours" : $"{seconds / 60:N0} minutes";

    private static void OpenUrl(string target)
    {
        try
        {
            var open = new ProcessStartInfo("open") { UseShellExecute = false };
            open.ArgumentList.Add(target);
            Process.Start(open)?.Dispose();
        }
        catch (Exception)
        {
            // Nothing more to do if macOS won't open it.
        }
    }

    private long WindowNumber()
    {
        try
        {
            if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: var window } && window != IntPtr.Zero)
            {
                return (long)ObjC.Send(window, "windowNumber");
            }
        }
        catch (Exception)
        {
            // The plugin's window then just opens on its own, centred.
        }

        return 0;
    }

    private ControlTheme? FindTheme(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var theme) ? theme as ControlTheme : null;

    private Geometry? ThemeIcon(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var icon) ? icon as Geometry : null;

    private IBrush? ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var brush) ? brush as IBrush : null;
}
