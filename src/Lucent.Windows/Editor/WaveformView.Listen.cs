using System.Windows;
using System.Windows.Media;

namespace Lucent.Editor;

/// <summary>
/// Solo and mute for each channel's lane (L and R), in small M and S buttons under the lane's name, like a mixer's.
/// They change only what's heard while the editor plays, never the sound itself; a lane that isn't heard is dimmed.
/// With any lane soloed, only the soloed lanes are heard. A mono sound has one lane, so it has no buttons.
/// </summary>
public sealed partial class WaveformView
{
    private const double ListenButtonWidth = 18;
    private const double ListenButtonHeight = 16;

    private static readonly Brush MuteBrush = Frozen(new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x3D)));

    private readonly bool[] _muted = new bool[2];
    private readonly bool[] _soloed = new bool[2];

    /// <summary>Raised when a click on a lane's M or S button changes which channels are heard.</summary>
    public event EventHandler? HeardChannelsChanged;

    /// <summary>Which channels are heard, left then right, after solo and mute.</summary>
    public bool[] HeardChannels => new[] { IsHeard(0), IsHeard(1) };

    /// <summary>Every channel heard again, as for a newly opened sound.</summary>
    public void ResetListening()
    {
        if (_muted.Any(on => on) || _soloed.Any(on => on))
        {
            Array.Clear(_muted);
            Array.Clear(_soloed);
            InvalidateVisual();
            HeardChannelsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool IsHeard(int channel) =>
        channel >= _muted.Length || (_soloed.Any(on => on) ? _soloed[channel] : !_muted[channel]);

    private bool HasListenButtons => _peaks is { Audio.ChannelCount: 2 };

    private static Rect ListenButton(double laneTop, bool solo) =>
        new(8 + (solo ? ListenButtonWidth + 4 : 0), laneTop + 20, ListenButtonWidth, ListenButtonHeight);

    private void DrawListenButtons(DrawingContext dc, int channel, double laneTop)
    {
        if (!HasListenButtons)
        {
            return;
        }

        var outline = new Pen(LineBrush ?? Brushes.Gray, 1);
        foreach (var solo in new[] { false, true })
        {
            var on = solo ? _soloed[channel] : _muted[channel];
            var box = ListenButton(laneTop, solo);
            dc.DrawRoundedRectangle(on ? solo ? WaveBrush : MuteBrush : LaneBrush, on ? null : outline, box, 4, 4);
            var text = Text(solo ? "S" : "M", 10, on ? Brushes.Black : LabelBrush);
            dc.DrawText(text, new Point(box.X + (box.Width - text.Width) / 2, box.Y + (box.Height - text.Height) / 2));
        }
    }

    /// <summary>The lane and button (mute or solo) at a point, or null when it isn't on one.</summary>
    private (int Channel, bool Solo)? ListenButtonAt(Point position)
    {
        if (!HasListenButtons)
        {
            return null;
        }

        var laneHeight = LaneHeight(ActualHeight, 2);
        for (var c = 0; c < 2; c++)
        {
            foreach (var solo in new[] { false, true })
            {
                if (ListenButton(LaneTop(c, laneHeight), solo).Contains(position))
                {
                    return (c, solo);
                }
            }
        }

        return null;
    }

    private void ToggleListen(int channel, bool solo)
    {
        var flags = solo ? _soloed : _muted;
        flags[channel] = !flags[channel];
        InvalidateVisual();
        HeardChannelsChanged?.Invoke(this, EventArgs.Empty);
    }

}
