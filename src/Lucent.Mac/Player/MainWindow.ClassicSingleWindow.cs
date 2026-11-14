using Avalonia;
using Avalonia.Controls;
using Lucent.Layout;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

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
    /// own, which moves the window); with Lucent's own player, its title bar across the top.
    /// </summary>
    private void ShowClassicInSingleWindow()
    {
        if (_header is null)
        {
            return;
        }

        _header.IsVisible = !IsClassic;
        CanResize = true;
        RetileSoon();
    }

    /// <summary>As single window ends with a classic skin on: the player's window fits its skin again.</summary>
    private void ShowClassicOnItsOwn()
    {
        if (!IsClassic)
        {
            return;
        }

        MaxWidth = double.PositiveInfinity;
        SizeToContent = SizeToContent.WidthAndHeight;
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
            GiveBack(window, show: false);
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
        if (WindowState == WindowState.Normal && wide > Bounds.Width + 0.5)
        {
            var screen = Placement.WorkArea(this);
            var place = SingleWindowLayout.OnScreen(new TileRect(Left, Top, wide, Bounds.Height),
                new TileRect(screen.X, screen.Y, screen.Width, screen.Height));
            this.MoveTo(place.Left, place.Top);
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
        if (!InClassicColumn || WindowState != WindowState.Normal || _singleBeforeFill is not null || Bounds.Width <= 0
            || !arranged.Places.TryGetValue("Player", out var player)
            || arranged.Places.Any(entry => entry.Key != "Player" && Math.Min(entry.Value.Bottom, player.Bottom) - Math.Max(entry.Value.Top, player.Top) > 1))
        {
            return false;
        }

        var width = player.Width + Math.Max(0, Bounds.Width - body.Width);
        var tall = _laidOutTiles is { } tiles ? ClassicColumnHeight(tiles) : null;
        var height = tall is { } column ? column + HeaderHeight + Math.Max(0, Bounds.Height - HeaderHeight - body.Height) : double.PositiveInfinity;
        MaxWidth = width;
        MaxHeight = height;
        if (Math.Abs(Bounds.Width - width) <= 0.5 && (tall is null || Math.Abs(Bounds.Height - height) <= 0.5))
        {
            return false;
        }

        // Its width now, to have back once there's more than the column (or, made this narrow, room for a big window beside it).
        _classicWideWidth ??= Bounds.Width > width + 1 ? Bounds.Width : player.Width * 3;
        Width = width;
        if (tall is not null)
        {
            Height = height;
        }

        RetileSoon();
        return true;
    }

    /// <summary>For the layout check: picks a classic skin by name, or Lucent's own player with null, as the skin picker does.</summary>
    internal void UseClassicSkin(string? name) => ChooseClassicSkin(name);

    /// <summary>For the layout check: whether Lucent's title bar is shown across the single window (not with a classic skin).</summary>
    internal bool ShowsLucentTitleBar => _header?.IsVisible ?? true;

    /// <summary>For the layout check: the player's panel's width in the single window, its skin's with a classic skin.</summary>
    internal double PlayerPanelWidth => PlayerPaneWidth;

    /// <summary>For the layout check: opens the equalizer if it isn't, and gives back a way to close it again if it did.</summary>
    internal Action OpenEqualizerToo()
    {
        if (OpenEqualizerWindow is not null)
        {
            return () => { };
        }

        ToggleEqualizer();
        return () =>
        {
            if (OpenEqualizerWindow is not null)
            {
                ToggleEqualizer();
            }
        };
    }
}
