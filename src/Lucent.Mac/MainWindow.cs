using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Visuals;
using SharpMetal.Metal;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// A first Mac window: a row of player controls over the visuals. It plays files you open or drop
/// on it, one after another, and the visuals move to the music like on Windows. The rest of the
/// Windows app's windows (playlist, library, equalizer, editor...) come over in later steps.
/// </summary>
public sealed class MainWindow : Window
{
    private readonly MacPlayer _player = new();
    private readonly SongClock _songClock = new();
    private readonly List<string> _queue = new();
    private readonly VisualEngine? _engine;
    private readonly MetalView? _view;
    private readonly TextBlock _trackTitle = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _visualName = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 180, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _time = new() { VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
    private readonly Button _playButton = new() { Content = "Play" };
    private readonly Slider _position = new() { Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly CheckBox _auto = new() { Content = "Auto", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private CancellationTokenSource? _beatSearch;
    private int _queueIndex = -1;
    private bool _settingPosition;

    public MainWindow()
    {
        Title = "Lucent";
        Width = 1100;
        Height = 700;
        MinWidth = 640;
        MinHeight = 400;
        Background = Brushes.Black;
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Lucent-64.png");
        if (File.Exists(icon))
        {
            Icon = new WindowIcon(new Bitmap(icon));
        }

        Control visuals;
        var device = MTLDevice.CreateSystemDefaultDevice();
        if (device.NativePtr == IntPtr.Zero)
        {
            visuals = Message("This Mac can't run Metal, so the visuals can't be shown. Music still plays.");
        }
        else if (TryStartVisuals(device, out var problem) is not { } engine)
        {
            visuals = Message($"The visuals couldn't start: {problem}");
        }
        else
        {
            _engine = engine;
            _engine.PresetChanged += index => Dispatcher.UIThread.Post(() => ShowVisualName(index));
            _view = new MetalView(_engine);
            _view.RendererStarted += (_, renderer) =>
                renderer.Failed += message => Dispatcher.UIThread.Post(() => _visualName.Text = $"Visuals stopped: {message}");
            visuals = _engine.Presets.Count == 0
                ? Message("No visuals were found next to the app. The Metal shaders are made when the app is built (see the README).")
                : _view;
            ShowVisualName(0);
        }

        Content = new DockPanel
        {
            LastChildFill = true,
            Children =
            {
                BuildControls(),
                visuals,
            },
        };

        _player.TrackEnded += (_, _) => Dispatcher.UIThread.Post(() => PlayAt(_queueIndex + 1));
        _clock.Tick += (_, _) => UpdateClock();
        _clock.Start();

        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DataFormats.Files) ? DragDropEffects.Copy : DragDropEffects.None);
        AddHandler(DragDrop.DropEvent, (_, e) => AddFiles(e.Data.GetFiles()?.Select(item => item.TryGetLocalPath()) ?? Enumerable.Empty<string?>()));
        KeyDown += OnKeyDown;
        Closed += (_, _) =>
        {
            _clock.Stop();
            _view?.Renderer?.Dispose();
            _engine?.Dispose();
            _player.Dispose();
        };
    }

    private Control BuildControls()
    {
        var open = new Button { Content = "Open…" };
        open.Click += async (_, _) => await OpenFilesAsync();
        _playButton.Click += (_, _) => TogglePlay();
        var previousTrack = new Button { Content = "⏮" };
        previousTrack.Click += (_, _) => PlayAt(_queueIndex - 1);
        var nextTrack = new Button { Content = "⏭" };
        nextTrack.Click += (_, _) => PlayAt(_queueIndex + 1);
        var previousVisual = new Button { Content = "◀" };
        previousVisual.Click += (_, _) => StepVisual(-1);
        var nextVisual = new Button { Content = "▶" };
        nextVisual.Click += (_, _) => StepVisual(1);
        _auto.IsCheckedChanged += (_, _) =>
        {
            if (_engine is not null)
            {
                _engine.AutoCycle = _auto.IsChecked == true;
            }
        };
        var volume = new Slider { Minimum = 0, Maximum = 1, Value = _player.Volume, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        volume.ValueChanged += (_, e) => _player.Volume = (float)e.NewValue;
        _position.ValueChanged += (_, e) =>
        {
            if (!_settingPosition)
            {
                _player.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        };

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { open, previousTrack, _playButton, nextTrack },
        };
        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { previousVisual, _visualName, nextVisual, _auto, new TextBlock { Text = "Volume", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 }, volume },
        };
        var top = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(left, Dock.Left);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(left);
        top.Children.Add(right);
        top.Children.Add(new Border { Padding = new Thickness(12, 0), Child = _trackTitle });

        var seek = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_time, Dock.Right);
        seek.Children.Add(_time);
        seek.Children.Add(_position);

        var panel = new StackPanel { Spacing = 4, Children = { top, seek } };
        var bar = new Border
        {
            Padding = new Thickness(10, 8),
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x12, 0x1C)),
            Child = panel,
        };
        DockPanel.SetDock(bar, Dock.Top);
        return bar;
    }

    private VisualEngine? TryStartVisuals(MTLDevice device, out string? problem)
    {
        try
        {
            problem = null;
            return new VisualEngine(device, new AudioAnalyzer(_player.Capture), new MetalShaderFiles(), MTLPixelFormat.BGRA8Unorm)
            {
                SongClock = _songClock,
            };
        }
        catch (Exception ex)
        {
            problem = ex.Message;
            return null;
        }
    }

    private static Control Message(string text) => new TextBlock
    {
        Text = text,
        Foreground = Brushes.White,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        MaxWidth = 480,
    };

    private async Task OpenFilesAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open music",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Music") { Patterns = AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext).ToList() },
            },
        });
        AddFiles(files.Select(file => file.TryGetLocalPath()));
    }

    /// <summary>Adds files (and the music in any folders) to the end of the queue, and starts playing if nothing is.</summary>
    private void AddFiles(IEnumerable<string?> paths)
    {
        var start = _queue.Count;
        foreach (var path in paths.OfType<string>())
        {
            if (Directory.Exists(path))
            {
                _queue.AddRange(Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(AudioReaderFactory.IsSupported)
                    .Order(StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(path) && AudioReaderFactory.IsSupported(path))
            {
                _queue.Add(path);
            }
        }

        if (!_player.IsPlaying && _queue.Count > start)
        {
            PlayAt(start);
        }
    }

    private void PlayAt(int index)
    {
        if (index < 0 || index >= _queue.Count)
        {
            return;
        }

        _queueIndex = index;
        var path = _queue[index];
        try
        {
            _player.Load(path);
            _player.Play();
            _trackTitle.Text = Path.GetFileNameWithoutExtension(path);
            FindBeats(path);
        }
        catch (Exception ex)
        {
            _trackTitle.Text = $"Couldn't play {Path.GetFileName(path)}: {ex.Message}";
        }
    }

    private void TogglePlay()
    {
        if (_player.CurrentPath is null)
        {
            PlayAt(Math.Max(0, _queueIndex));
        }
        else if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }
    }

    private void StepVisual(int step)
    {
        if (_engine is null || _engine.Presets.Count == 0)
        {
            return;
        }

        var count = _engine.Presets.Count;
        var index = _engine.PresetIndex;
        for (var tries = 0; tries < count; tries++)
        {
            index = (index + step + count) % count;
            if (!_engine.HasFailed(index))
            {
                break;
            }
        }

        _engine.PresetIndex = index;
        ShowVisualName(index);
    }

    private void ShowVisualName(int index)
    {
        if (_engine is { Presets.Count: > 0 } engine)
        {
            _visualName.Text = engine.Presets[Math.Clamp(index, 0, engine.Presets.Count - 1)].Name;
        }
    }

    private void UpdateClock()
    {
        _playButton.Content = _player.IsPlaying ? "Pause" : "Play";
        var duration = _player.Duration.TotalSeconds;
        var position = _player.Position.TotalSeconds;
        _settingPosition = true;
        _position.Maximum = Math.Max(1, duration);
        if (!_position.IsPointerOver)
        {
            _position.Value = Math.Min(position, _position.Maximum);
        }

        _settingPosition = false;
        _time.Text = $"{TimeSpan.FromSeconds(position):m\\:ss} / {TimeSpan.FromSeconds(duration):m\\:ss}";

        // Where the song is: where the speakers are, a little behind the decoder.
        var heard = position - AudioCapture.OutputLatencyMs / 1000.0;
        _songClock.Update(_player.CurrentPath, Math.Max(0, heard), _player.IsPlaying);
    }

    /// <summary>Works out the song's bars in the background, so Auto changes visual on a downbeat.</summary>
    private async void FindBeats(string path)
    {
        _beatSearch?.Cancel();
        var search = _beatSearch = new CancellationTokenSource();
        try
        {
            var beats = await Task.Run(() => BeatMap.Analyze(path, search.Token), search.Token);
            _songClock.SetBeats(path, beats);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            // No bars to follow: Auto changes on a beat instead.
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                break;
            case Key.Left:
                StepVisual(-1);
                break;
            case Key.Right:
                StepVisual(1);
                break;
            case Key.F:
                WindowState = WindowState == WindowState.FullScreen ? WindowState.Normal : WindowState.FullScreen;
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
