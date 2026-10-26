using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinampInspiredVisualizer.Library;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// A song's cover, small, beside it in a list (the queue, the library, Spotify's playlist), as Spotify's own lists
/// show it. Give it the song's <see cref="Path"/> (its cover is read from the file, or folder.jpg beside it) or a
/// <see cref="Url"/> to download it from. Covers load in the background only for rows that are on screen, and the
/// last few hundred are remembered, so long lists scroll smoothly. A faint note stands in until one arrives, or
/// when there's none.
/// </summary>
public sealed class TrackThumb : Border
{
    public const double Size = 36;

    public static readonly DependencyProperty PathProperty = DependencyProperty.Register(nameof(Path), typeof(string),
        typeof(TrackThumb), new PropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty UrlProperty = DependencyProperty.Register(nameof(Url), typeof(string),
        typeof(TrackThumb), new PropertyMetadata(null, OnSourceChanged));

    private readonly Viewbox _note;
    private CancellationTokenSource? _loading;

    public TrackThumb()
    {
        Width = Size;
        Height = Size;
        CornerRadius = new CornerRadius(4);
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
        var icon = new System.Windows.Shapes.Path();
        icon.SetResourceReference(System.Windows.Shapes.Path.DataProperty, "MusicIcon");
        icon.SetResourceReference(System.Windows.Shapes.Path.FillProperty, "FaintTextBrush");
        _note = new Viewbox { Width = 14, Height = 14, Child = icon };
        Child = _note;
        Loaded += (_, _) =>
        {
            Thumbnails.Forgotten += OnForgotten;
            Show();
        };
        Unloaded += (_, _) =>
        {
            Thumbnails.Forgotten -= OnForgotten;
            StopLoading();
        };
    }

    /// <summary>The music file whose cover to show.</summary>
    public string? Path
    {
        get => (string?)GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>Where to download the cover from, for a song in another app.</summary>
    public string? Url
    {
        get => (string?)GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    private static void OnSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs e) => ((TrackThumb)owner).Show();

    private void Show()
    {
        StopLoading();
        var key = Thumbnails.KeyFor(Path, Url);
        if (key is null)
        {
            ShowCover(null);
            return;
        }

        if (Thumbnails.TryGet(key, out var cached))
        {
            ShowCover(cached);
            return;
        }

        ShowCover(null);
        if (!IsLoaded)
        {
            return;
        }

        var loading = _loading = new CancellationTokenSource();
        _ = LoadAsync(key, loading.Token);
    }

    private async Task LoadAsync(string key, CancellationToken token)
    {
        try
        {
            var cover = await Thumbnails.LoadAsync(key, Path, Url, token);
            if (!token.IsCancellationRequested)
            {
                ShowCover(cover);
            }
        }
        catch (OperationCanceledException)
        {
            // Scrolled away, or the row shows another song now.
        }
    }

    private void OnForgotten(IReadOnlySet<string> paths)
    {
        if (Path is { } path && paths.Contains(path))
        {
            Show();
        }
    }

    private void StopLoading()
    {
        _loading?.Cancel();
        _loading = null;
    }

    private void ShowCover(ImageSource? cover)
    {
        if (cover is null)
        {
            SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
            _note.Visibility = Visibility.Visible;
            return;
        }

        Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
        _note.Visibility = Visibility.Collapsed;
    }
}

/// <summary>The small covers <see cref="TrackThumb"/> shows: loaded a few at a time, off the UI thread, and remembered.</summary>
public static class Thumbnails
{
    // Big enough to stay sharp at 200% scaling.
    private const int Pixels = 96;
    private const int Remembered = 600;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Loading = new(4);
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource? Cover)>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<(string Key, ImageSource? Cover)> Order = new();

    /// <summary>What a cover is remembered by: its web address, or the music file's path. Null when there's nothing to show.</summary>
    public static string? KeyFor(string? path, string? url) =>
        !string.IsNullOrEmpty(url) ? url
        : !string.IsNullOrEmpty(path) && !path.Contains("://", StringComparison.Ordinal) ? path
        : null;

    /// <summary>The remembered cover for <paramref name="key"/> (null when it has none), if it's been loaded.</summary>
    public static bool TryGet(string key, out ImageSource? cover)
    {
        if (Cache.TryGetValue(key, out var node))
        {
            Order.Remove(node);
            Order.AddFirst(node);
            cover = node.Value.Cover;
            return true;
        }

        cover = null;
        return false;
    }

    /// <summary>Raised by <see cref="Forget"/>, so rows showing those files load their covers again.</summary>
    public static event Action<IReadOnlySet<string>>? Forgotten;

    /// <summary>Forgets the covers of files whose tags were just saved, so a new cover shows.</summary>
    public static void Forget(IEnumerable<string> paths)
    {
        var forgotten = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in forgotten)
        {
            if (Cache.Remove(path, out var node))
            {
                Order.Remove(node);
            }
        }

        Forgotten?.Invoke(forgotten);
    }

    /// <summary>Loads (and remembers) a cover. Called on the UI thread; the work happens off it.</summary>
    public static async Task<ImageSource?> LoadAsync(string key, string? path, string? url, CancellationToken token)
    {
        await Loading.WaitAsync(token);
        ImageSource? cover;
        try
        {
            token.ThrowIfCancellationRequested();
            cover = !string.IsNullOrEmpty(url) ? await DownloadAsync(url, token) : await ReadAsync(path!, token);
        }
        finally
        {
            Loading.Release();
        }

        Remember(key, cover);
        return cover;
    }

    private static Task<ImageSource?> ReadAsync(string path, CancellationToken token) =>
        Task.Run<ImageSource?>(() => File.Exists(path) ? CoverArt.LoadForTrack(path, Pixels, token)?.Image : null, token);

    private static async Task<ImageSource?> DownloadAsync(string url, CancellationToken token)
    {
        try
        {
            var data = await Http.GetByteArrayAsync(url, token);
            return await Task.Run<ImageSource?>(() => CoverArt.Decode(data, Pixels)?.Image, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            // Offline, or the address has gone: the note stays.
            return null;
        }
    }

    private static void Remember(string key, ImageSource? cover)
    {
        if (Cache.Remove(key, out var old))
        {
            Order.Remove(old);
        }

        Cache[key] = Order.AddFirst((key, cover));
        while (Order.Count > Remembered && Order.Last is { } last)
        {
            Cache.Remove(last.Value.Key);
            Order.RemoveLast();
        }
    }
}
