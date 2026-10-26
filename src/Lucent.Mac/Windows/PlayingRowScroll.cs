using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Layout;

namespace Lucent.Mac.Windows;

/// <summary>
/// Keeps the song playing in the middle of a list (the library, a streaming playlist opened in it), gliding to it
/// when the song changes (see <see cref="ScrollGlide"/>). It leaves the list be for a few seconds after the user
/// scrolls it, or moves through it with the keys, then catches up with the song playing by then.
/// </summary>
public sealed class PlayingRowScroll
{
    private readonly ListBox _list;
    private readonly DispatcherTimer _wait = new();
    private readonly Stopwatch _clock = new();
    private Func<object?>? _row;
    private bool _glide;
    private DateTime _touchedAt = DateTime.MinValue;
    private ScrollViewer? _scroller;
    private double _from;
    private double _to;
    private int _glideNumber;

    public PlayingRowScroll(ListBox list)
    {
        _list = list;
        _wait.Tick += (_, _) =>
        {
            _wait.Stop();
            Center();
        };
        list.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => Touched(), RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if ((e.Source as Visual)?.FindAncestorOfType<ScrollBar>(includeSelf: true) is not null)
            {
                Touched();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        list.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            {
                Touched();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    /// <summary>
    /// Puts the row <paramref name="row"/> finds in the middle of the list: gliding there, or straight there when
    /// <paramref name="glide"/> is false (the list has only just been filled). The row is found again if the user
    /// has the list held, once they let it go.
    /// </summary>
    public void Show(Func<object?> row, bool glide)
    {
        _row = row;
        _glide = glide;
        Center();
    }

    /// <summary>Forgets the row, as when the list is emptied or swapped for another.</summary>
    public void Forget()
    {
        _row = null;
        _wait.Stop();
        StopGlide();
    }

    private void Touched()
    {
        _touchedAt = DateTime.UtcNow;
        StopGlide();
        if (_row is not null && !_wait.IsEnabled)
        {
            Center();
        }
    }

    private void Center()
    {
        if (_row is null)
        {
            return;
        }

        var held = _touchedAt + ScrollGlide.HoldAfterUserScroll - DateTime.UtcNow;
        if (held > TimeSpan.Zero)
        {
            // Catches up once the user lets the list go; it glides then, as the list is already showing.
            _glide = true;
            _wait.Interval = held;
            _wait.Start();
            return;
        }

        if (!_list.IsEffectivelyVisible || _row() is not { } item
            || (_scroller ??= _list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()) is not { } scroller)
        {
            return;
        }

        StopGlide();
        var from = scroller.Offset.Y;
        var glide = _glide;
        _row = null;
        _list.ScrollIntoView(item);
        Dispatcher.UIThread.Post(() =>
        {
            if (_list.ContainerFromItem(item) is not { IsEffectivelyVisible: true } row
                || row.TranslatePoint(new Point(), scroller) is not { } at)
            {
                return;
            }

            var top = scroller.Offset.Y + at.Y;
            var to = ScrollGlide.CenteredOffset(top, row.Bounds.Height, scroller.Viewport.Height, scroller.Extent.Height);
            if (!glide || TopLevel.GetTopLevel(_list) is not { } topLevel)
            {
                scroller.Offset = new Vector(scroller.Offset.X, to);
                return;
            }

            _from = ScrollGlide.Start(from, to, scroller.Viewport.Height);
            _to = to;
            scroller.Offset = new Vector(scroller.Offset.X, _from);
            _clock.Restart();
            var number = ++_glideNumber;
            topLevel.RequestAnimationFrame(_ => OnFrame(topLevel, number));
        }, DispatcherPriority.Loaded);
    }

    private void OnFrame(TopLevel topLevel, int number)
    {
        // A newer glide, or the user, has taken over.
        if (number != _glideNumber || _scroller is not { } scroller)
        {
            return;
        }

        scroller.Offset = new Vector(scroller.Offset.X, ScrollGlide.At(_from, _to, _clock.Elapsed));
        if (!ScrollGlide.IsDone(_clock.Elapsed))
        {
            topLevel.RequestAnimationFrame(_ => OnFrame(topLevel, number));
        }
    }

    private void StopGlide() => _glideNumber++;
}
