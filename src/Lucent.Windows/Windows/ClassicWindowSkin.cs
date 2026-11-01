using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using Lucent.ClassicSkins;

namespace Lucent.Windows;

/// <summary>
/// Puts a classic skin's frame for other windows (gen.bmp, <see cref="ClassicWindowFrame"/>) round one of Lucent's
/// windows, and colours what's inside it from the skin (pledit.txt's colours and font), so every window matches the
/// classic player, equalizer and playlist. The window's own content stays as it is inside the frame, so all its
/// buttons and lists work as ever. Its edges still resize it; the skin's title bar moves it and its × closes it.
/// While the window is maximized or full screen, the frame steps aside.
/// </summary>
public static class ClassicWindowSkin
{
    // The theme's colours and shapes the window's own content uses, which are swapped for the skin's while it's framed.
    private static readonly string[] CornerKeys =
    {
        "TinyCorner", "SegmentCorner", "ControlCorner", "BarCorner", "PanelCorner", "CardCorner", "TrackCorner", "RoundCorner",
    };

    private static readonly ConditionalWeakTable<Window, Framed> States = new();

    /// <summary>Frames the window in the skin at that zoom (1, or 2 for double size), or takes the frame off with null.</summary>
    public static void Apply(Window window, string title, ClassicSkin? skin, double zoom)
    {
        States.TryGetValue(window, out var framed);
        if (skin is null)
        {
            framed?.Remove();
            return;
        }

        if (framed is null)
        {
            framed = new Framed(window);
            States.Add(window, framed);
        }

        framed.Show(title, skin, zoom);
    }

    private sealed class Framed
    {
        private readonly Window _window;
        private readonly ClassicWindowFrame _frame;
        private readonly ClassicSurfaceView _view;
        private readonly Border _contentHost = new();
        private readonly Grid _root;
        private readonly Dictionary<string, object?> _savedResources = new();
        private object? _lucentContent;
        private WindowChrome? _lucentChrome;
        private bool _on;

        public Framed(Window window)
        {
            _window = window;
            _frame = new ClassicWindowFrame(ClassicSkinLibrary.Default);
            _frame.CloseRequested += window.Close;
            _view = new ClassicSurfaceView(_frame)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                ContextMenu = new ContextMenu(),
            };
            // A right-click on the frame gets the same menu as on the window's own title bar.
            LucentFullScreen.AddTo(_view.ContextMenu, window);
            _root = new Grid { Children = { _view, _contentHost } };
            window.SizeChanged += (_, _) => Fit();
            window.StateChanged += (_, _) => Fit();
            window.Activated += (_, _) => Redraw();
            window.Deactivated += (_, _) => Redraw();
        }

        public void Show(string title, ClassicSkin skin, double zoom)
        {
            _frame.Skin = skin;
            _frame.Title = title;
            _view.Zoom = zoom;
            if (!_on)
            {
                _on = true;
                _lucentContent = _window.Content;
                _window.Content = null;
                _contentHost.Child = _lucentContent as UIElement;
                _window.Content = _root;
                _lucentChrome = WindowChrome.GetWindowChrome(_window);
            }

            UseSkinColours(skin);
            // The skin's title bar moves the window now; the edges still resize it.
            CaptionRepaint.Change(_window, () => WindowChrome.SetWindowChrome(_window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = _lucentChrome?.ResizeBorderThickness ?? new Thickness(6),
                GlassFrameThickness = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            }));
            Fit();
        }

        public void Remove()
        {
            if (!_on)
            {
                return;
            }

            _on = false;
            _window.Content = null;
            _contentHost.Child = null;
            _window.Content = _lucentContent;
            _lucentContent = null;
            foreach (var (key, value) in _savedResources)
            {
                if (value is null)
                {
                    _window.Resources.Remove(key);
                }
                else
                {
                    _window.Resources[key] = value;
                }
            }

            _savedResources.Clear();
            var chrome = _lucentChrome;
            CaptionRepaint.Change(_window, () => WindowChrome.SetWindowChrome(_window, chrome));
        }

        /// <summary>Sizes the frame to the window and lays the window's own content over its middle; or no frame while it fills the screen.</summary>
        private void Fit()
        {
            if (!_on)
            {
                return;
            }

            var normal = _window.WindowState == WindowState.Normal;
            _view.Visibility = normal ? Visibility.Visible : Visibility.Collapsed;
            if (!normal)
            {
                _contentHost.Margin = new Thickness(0);
                return;
            }

            var scale = _view.Scale;
            var width = _window.ActualWidth > 0 ? _window.ActualWidth : _window.Width;
            var height = _window.ActualHeight > 0 ? _window.ActualHeight : _window.Height;
            if (double.IsNaN(width) || double.IsNaN(height) || width <= 0 || height <= 0)
            {
                return;
            }

            // Rounded up, so the frame reaches the window's edges (a sliver of its right and bottom edges may be cut).
            _frame.SetSize((int)Math.Ceiling(width / scale), (int)Math.Ceiling(height / scale));
            var (left, top, _, _) = _frame.ContentArea;
            _contentHost.Margin = new Thickness(left * scale, top * scale,
                width - (_frame.Width - ClassicWindowFrame.RightWidth) * scale,
                height - (_frame.Height - ClassicWindowFrame.BottomHeight) * scale);
            Redraw();
        }

        private void Redraw()
        {
            if (_on && _view.Visibility == Visibility.Visible)
            {
                _frame.IsActive = _window.IsActive;
                _view.Refresh();
            }
        }

        /// <summary>The window's own content in the skin's colours: its playlist text, background, playing song and selection.</summary>
        private void UseSkinColours(ClassicSkin skin)
        {
            var style = skin.Playlist;
            var background = ColourOf(style.NormalBackground);
            var text = ColourOf(style.Normal);
            var current = ColourOf(style.Current);
            var selected = ColourOf(style.SelectedBackground);
            Set("BackgroundBrush", Solid(background));
            Set("SurfaceBrush", Solid(background));
            Set("SurfaceRaisedBrush", Solid(Mix(background, text, 0.08)));
            Set("HoverBrush", Solid(Mix(background, text, 0.14)));
            Set("HairlineBrush", Solid(Mix(background, text, 0.2)));
            Set("TextBrush", Solid(text));
            Set("MutedTextBrush", Solid(Mix(text, background, 0.35)));
            Set("FaintTextBrush", Solid(Mix(text, background, 0.6)));
            Set("AccentBrush", Solid(current));
            Set("AccentSoftBrush", Solid(selected));
            var gradient = new LinearGradientBrush(current, Mix(current, text, 0.4), new Point(0, 0), new Point(1, 1));
            gradient.Freeze();
            Set("AccentGradientBrush", gradient);
            Set("AccentForegroundBrush", Solid(background));
            // The frame draws the window's edges and title bar, so Lucent's own outline and finish step back.
            Set("WindowFrameBrush", Brushes.Transparent);
            Set("PanelSheenBrush", Brushes.Transparent);
            Set("TitleBarBrush", Solid(Mix(background, text, 0.05)));
            Set("UiFont", new FontFamily($"{style.Font}, Arial"));
            foreach (var key in CornerKeys)
            {
                Set(key, new CornerRadius(0));
            }
        }

        private void Set(string key, object value)
        {
            if (!_savedResources.ContainsKey(key))
            {
                // Whatever the window had of its own, to put back; null when it only had the app's.
                _savedResources[key] = _window.Resources.Contains(key) ? _window.Resources[key] : null;
            }

            _window.Resources[key] = value;
        }

        private static Color ColourOf(uint argb) => Color.FromArgb(0xFF, (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);

        private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t),
            (byte)Math.Round(a.G + (b.G - a.G) * t),
            (byte)Math.Round(a.B + (b.B - a.B) * t));

        private static SolidColorBrush Solid(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }
    }
}
