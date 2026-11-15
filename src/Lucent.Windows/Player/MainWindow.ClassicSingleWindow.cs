using System.Windows;
using Lucent.Layout;
using Lucent.Windows;

namespace Lucent;

// Single window with a classic skin on (MainWindow.SingleWindow.cs has the rest of it): every panel keeps its skin, as
// Winamp's windows looked docked together. The classic player draws its own title bar, so Lucent's isn't shown over it,
// and dragging the player (or the equalizer) moves the single window; the playlist and the other windows' skinned title
// bars drag their panels to swap them. The player and the equalizer are pictures that can't stretch: with just them and the
// playlist open, the window is the player's width, a column like Winamp's; open anything else and it gets its room back.
// Picking a skin, or double size, gives each window its insides back for the skin to change and then lends them again.
public partial class MainWindow
{
    /// <summary>The single window's width before it was narrowed to the classic player's column, to go back to.</summary>
    private double? _classicWideWidth;

    /// <summary>The classic player's width, and its button strip's under it: the player's panel in the single window.</summary>
    private double ClassicPlayerWidth
    {
        get
        {
            var strip = _classicToolbar is { Width: > 0 } toolbar ? toolbar.Width : 0;
            return Math.Ceiling(Math.Max(_classicView!.PixelWidth, strip));
        }
    }

    /// <summary>
    /// The single window as the player's look needs it: with a classic skin, no Lucent title bar (the skin's player has its
    /// own, which moves the window) and edges that resize it; with Lucent's own player, its title bar across the top.
    /// </summary>
    private void ShowClassicInSingleWindow()
    {
        if (_header is null)
        {
            return;
        }

        _header.Visibility = IsClassic ? Visibility.Collapsed : Visibility.Visible;
        if (IsClassic)
        {
            SetCaptionHeight(0);
            CaptionRepaint.SetResizeMode(this, ResizeMode.CanResize);
        }

        RetileSoon();
    }

    /// <summary>As single window ends with a classic skin on: the player's window fits its skin again, which moves it.</summary>
    private void ShowClassicOnItsOwn()
    {
        if (!IsClassic)
        {
            return;
        }

        MaxWidth = double.PositiveInfinity;
        SizeToContent = SizeToContent.WidthAndHeight;
        SetCaptionHeight(0);
    }

    /// <summary>
    /// A skin picked (or double size) in the single window: each window gets its insides back for the skin to change them
    /// (the classic equalizer and playlist swap their own insides, the other windows are framed round theirs), then lends
    /// them to their panels again, which are laid out again at their new sizes.
    /// </summary>
    private void ChangeSkinInSingleWindow(Action change)
    {
        var hosted = _panes.Keys.ToList();
        foreach (var window in hosted)
        {
            GiveBack(window);
        }

        change();
        ApplyPlayerSize();
        foreach (var window in hosted.Where(window => window.IsVisible))
        {
            Host(window);
        }

        RetileSoon(grow: true);
    }

    /// <summary>
    /// Just the classic player with its equalizer and playlist, which make a column as wide as the player: nothing else
    /// that could go beside them.
    /// </summary>
    private bool InClassicColumn => IsClassic && _panes.Values.All(pane => TileKindOf(pane.Window) is TileKind.Equalizer or TileKind.Queue);

    /// <summary>
    /// The classic column's height with no playlist under the player and equalizer to fill the window down to its bottom:
    /// the window's no taller than them. Null when there's a playlist, or anything else.
    /// </summary>
    private double? ClassicColumnHeight(IReadOnlyList<TileWindow> tiles) =>
        InClassicColumn && tiles.All(tile => tile.Kind != TileKind.Queue) ? tiles.Sum(tile => tile.Height) : null;

    /// <summary>
    /// Before the panels are laid out: no limit on the window's size, which <see cref="NarrowToClassicColumn"/> puts back
    /// for the classic column; and once there's more than the column, or Lucent's own player, the width it had before it
    /// was narrowed to the column. True if it got that width back, so the panels are laid out again at it.
    /// </summary>
    private bool WidenFromClassicColumn(bool grow)
    {
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        if (_classicWideWidth is not { } wide || InClassicColumn)
        {
            return false;
        }

        _classicWideWidth = null;
        if (WindowState == WindowState.Normal && wide > ActualWidth + 0.5)
        {
            var screen = ScreenArea.Of(this);
            var place = SingleWindowLayout.OnScreen(new TileRect(Left, Top, wide, ActualHeight),
                new TileRect(screen.Left, screen.Top, screen.Width, screen.Height));
            Left = place.Left;
            Width = place.Width;
            RetileSoon(grow);
            return true;
        }

        return false;
    }

    /// <summary>
    /// With just the classic column (nothing beside the player), the window is as wide as the player, and with no playlist
    /// as tall as the player and equalizer, so there's no empty block beside or under them: they're pictures that can't
    /// stretch as Lucent's own player does. True if the window's size changed, so the panels are laid out again.
    /// </summary>
    private bool NarrowToClassicColumn(TileResult arranged, Size body)
    {
        if (!InClassicColumn || WindowState != WindowState.Normal || _singleBeforeFill is not null || ActualWidth <= 0
            || !arranged.Places.TryGetValue("Player", out var player)
            || arranged.Places.Any(entry => entry.Key != "Player" && Math.Min(entry.Value.Bottom, player.Bottom) - Math.Max(entry.Value.Top, player.Top) > 1))
        {
            return false;
        }

        var width = player.Width + Math.Max(0, ActualWidth - body.Width);
        var tall = _laidOutTiles is { } tiles ? ClassicColumnHeight(tiles) : null;
        var height = tall is { } column ? column + HeaderHeight + Math.Max(0, ActualHeight - HeaderHeight - body.Height) : double.PositiveInfinity;
        MaxWidth = width;
        MaxHeight = height;
        if (Math.Abs(ActualWidth - width) <= 0.5 && (tall is null || Math.Abs(ActualHeight - height) <= 0.5))
        {
            return false;
        }

        // Its width now, to have back once there's more than the column (or, made this narrow, room for a big window beside it).
        _classicWideWidth ??= ActualWidth > width + 1 ? ActualWidth : player.Width * 3;
        Width = width;
        if (tall is not null)
        {
            Height = height;
        }

        RetileSoon();
        return true;
    }
}
