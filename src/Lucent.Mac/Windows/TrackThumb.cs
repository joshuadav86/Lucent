using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Lucent.Mac.Library;

namespace Lucent.Mac.Windows;

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

    public static readonly StyledProperty<string?> PathProperty = AvaloniaProperty.Register<TrackThumb, string?>(nameof(Path));

    public static readonly StyledProperty<string?> UrlProperty = AvaloniaProperty.Register<TrackThumb, string?>(nameof(Url));

    private readonly Viewbox _note;
    private CancellationTokenSource? _loading;
    // Its cover is to load once the list stops gliding (see Thumbnails.Hold).
    private bool _waiting;
    private bool _attached;

    static TrackThumb()
    {
        PathProperty.Changed.AddClassHandler<TrackThumb>((thumb, _) => thumb.Show());
        UrlProperty.Changed.AddClassHandler<TrackThumb>((thumb, _) => thumb.Show());
    }

    public TrackThumb()
    {
        Width = Size;
        Height = Size;
        CornerRadius = new CornerRadius(4);
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        this.SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
        var icon = new Avalonia.Controls.Shapes.Path();
        icon.SetResourceReference(Avalonia.Controls.Shapes.Path.DataProperty, "MusicIcon");
        icon.SetResourceReference(Shape.FillProperty, "FaintTextBrush");
        _note = new Viewbox { Width = 14, Height = 14, Child = icon };
        Child = _note;
    }

    /// <summary>The music file whose cover to show.</summary>
    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>Where to download the cover from, for a song in another app.</summary>
    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        Thumbnails.Forgotten += OnForgotten;
        Thumbnails.Released += OnReleased;
        Show();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _attached = false;
        Thumbnails.Forgotten -= OnForgotten;
        Thumbnails.Released -= OnReleased;
        StopLoading();
    }

    private void Show()
    {
        StopLoading();
        _waiting = false;
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
        if (!_attached)
        {
            return;
        }

        // Not for a row flying past as the list glides a long way.
        if (Thumbnails.Held)
        {
            _waiting = true;
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

    private void OnReleased()
    {
        if (_waiting)
        {
            Show();
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

    private void ShowCover(Bitmap? cover)
    {
        if (cover is null)
        {
            this.SetResourceReference(BackgroundProperty, "SurfaceRaisedBrush");
            _note.IsVisible = true;
            return;
        }

        Background = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
        _note.IsVisible = false;
    }
}

/// <summary>The small covers <see cref="TrackThumb"/> shows: loaded a few at a time, off the UI thread, and remembered.</summary>
public static class Thumbnails
{
    // Big enough to stay sharp on a Retina screen.
    private const int Pixels = 96;
    private const int Remembered = 600;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly SemaphoreSlim Loading = new(4);
    private static readonly Dictionary<string, LinkedListNode<(string Key, Bitmap? Cover)>> Cache = new(StringComparer.Ordinal);
    private static readonly LinkedList<(string Key, Bitmap? Cover)> Order = new();
    private static int _holds;

    /// <summary>What a cover is remembered by: its web address, or the music file's path. Null when there's nothing to show.</summary>
    public static string? KeyFor(string? path, string? url) =>
        !string.IsNullOrEmpty(url) ? url
        : !string.IsNullOrEmpty(path) && !path.Contains("://", StringComparison.Ordinal) ? path
        : null;

    /// <summary>The remembered cover for <paramref name="key"/> (null when it has none), if it's been loaded.</summary>
    public static bool TryGet(string key, out Bitmap? cover)
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

    /// <summary>
    /// True while a list glides a long way to the song playing: rows flying past show only covers already remembered,
    /// rather than starting (and straight away dropping) downloads, and those still waiting load theirs on landing.
    /// </summary>
    public static bool Held => _holds > 0;

    /// <summary>Raised when the last hold is let go, so rows that waited load their covers.</summary>
    public static event Action? Released;

    /// <summary>Holds back new covers until <see cref="Release"/>.</summary>
    public static void Hold() => _holds++;

    /// <summary>Lets go of a <see cref="Hold"/>.</summary>
    public static void Release()
    {
        if (_holds > 0 && --_holds == 0)
        {
            Released?.Invoke();
        }
    }

    /// <summary>Raised by <see cref="Forget"/>, so rows showing those files load their covers again.</summary>
    public static event Action<IReadOnlySet<string>>? Forgotten;

    /// <summary>Forgets the covers of files whose tags were just saved, so a new cover shows.</summary>
    public static void Forget(IEnumerable<string> paths)
    {
        var forgotten = new HashSet<string>(paths, StringComparer.Ordinal);
        foreach (var path in forgotten)
        {
            if (Cache.Remove(path, out var node))
            {
                Order.Remove(node);
            }
        }

        Forgotten?.Invoke(forgotten);
    }

    /// <summary>Remembers <paramref name="cover"/> as <paramref name="key"/>'s, for screenshots of songs that aren't real files.</summary>
    public static void Preview(string key, Bitmap cover) => Remember(key, cover);

    /// <summary>Loads (and remembers) a cover. Called on the UI thread; the work happens off it.</summary>
    public static async Task<Bitmap?> LoadAsync(string key, string? path, string? url, CancellationToken token)
    {
        await Loading.WaitAsync(token);
        Bitmap? cover;
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

    private static Task<Bitmap?> ReadAsync(string path, CancellationToken token) =>
        Task.Run<Bitmap?>(() => File.Exists(path) ? CoverArt.LoadForTrack(path, Pixels, token)?.Image : null, token);

    private static async Task<Bitmap?> DownloadAsync(string url, CancellationToken token)
    {
        try
        {
            var data = await Http.GetByteArrayAsync(url, token);
            return await Task.Run<Bitmap?>(() => CoverArt.Decode(data, Pixels)?.Image, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !token.IsCancellationRequested)
        {
            // Offline, or the address has gone: the note stays.
            return null;
        }
    }

    // Covers that fall out aren't disposed: a row may still be showing one.
    private static void Remember(string key, Bitmap? cover)
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
