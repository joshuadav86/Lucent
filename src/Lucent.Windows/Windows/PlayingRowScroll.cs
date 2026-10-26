using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lucent.Layout;

namespace Lucent.Windows;

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
    private bool _gliding;

    public PlayingRowScroll(ListBox list)
    {
        _list = list;
        _wait.Tick += (_, _) =>
        {
            _wait.Stop();
            Center();
        };
        list.PreviewMouseWheel += (_, _) => Touched();
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (Ancestor<ScrollBar>(e.OriginalSource as DependencyObject) is not null)
            {
                Touched();
            }
        };
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
            {
                Touched();
            }
        };
        list.ManipulationStarted += (_, _) => Touched();
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

        if (!_list.IsVisible || _row() is not { } item || (_scroller ??= FindScroller(_list)) is not { } scroller)
        {
            return;
        }

        StopGlide();
        var from = scroller.VerticalOffset;
        var glide = _glide;
        _row = null;
        _list.ScrollIntoView(item);
        _ = _list.Dispatcher.InvokeAsync(() =>
        {
            if (FindRow(scroller, item) is not { } row || !row.IsVisible)
            {
                return;
            }

            var top = scroller.VerticalOffset + row.TransformToAncestor(scroller).Transform(new Point()).Y;
            var to = ScrollGlide.CenteredOffset(top, row.ActualHeight, scroller.ViewportHeight, scroller.ExtentHeight);
            if (!glide || !SystemParameters.ClientAreaAnimation)
            {
                scroller.ScrollToVerticalOffset(to);
                return;
            }

            _from = ScrollGlide.Start(from, to, scroller.ViewportHeight);
            _to = to;
            scroller.ScrollToVerticalOffset(_from);
            _clock.Restart();
            _gliding = true;
            CompositionTarget.Rendering += OnFrame;
        }, DispatcherPriority.Loaded);
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        if (_scroller is not { } scroller)
        {
            StopGlide();
            return;
        }

        scroller.ScrollToVerticalOffset(ScrollGlide.At(_from, _to, _clock.Elapsed));
        if (ScrollGlide.IsDone(_clock.Elapsed))
        {
            StopGlide();
        }
    }

    private void StopGlide()
    {
        if (_gliding)
        {
            _gliding = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    // The row showing item: found among the rows on screen, as a grouped list's own lookup only covers its headings.
    private static FrameworkElement? FindRow(DependencyObject parent, object item)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ListBoxItem row && ReferenceEquals(row.DataContext, item))
            {
                return row;
            }

            if (child is not ListBoxItem && FindRow(child, item) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static ScrollViewer? FindScroller(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as ScrollViewer ?? FindScroller(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null and not T)
        {
            source = source is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
        }

        return source as T;
    }
}
