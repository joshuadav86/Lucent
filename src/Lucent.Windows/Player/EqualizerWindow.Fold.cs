using System.Windows;

namespace Lucent;

// Folding the equalizer in the single window. With the equalizer under the player and the library (or the queue, or the
// art) under it, the line between them can be dragged up to give the panel under it more room (PanelDividers in
// Lucent.Core; the equalizer's panel says how far it folds, TileWindow.FoldsTo): the equalizer folds. First its response
// curve gets shorter, down to a strip; then its sliders get shorter; last the strip goes, the sliders taking its room
// back. Its window keeps its own height all the while (Height: what the layouts give it, and what it is as a window again),
// and only its panel is shorter, by just what it's folded. The Mac app's EqualizerWindow.Fold.cs is the same.
public partial class EqualizerWindow
{
    /// <summary>The response curve's own height, and the shortest it's drawn before it goes.</summary>
    private const double CurveHeight = 84;

    private const double ShortestCurve = 20;

    /// <summary>The sliders' own height (as the BandSlider style has it), and the shortest they get.</summary>
    private const double SliderHeight = 150;

    private const double ShortestSliders = 90;

    /// <summary>The most the equalizer folds: the curve all the way, and the sliders as far as they go.</summary>
    public const double MostFold = CurveHeight + SliderHeight - ShortestSliders;

    /// <summary>How much shorter than its own height it's folded now; 0 as a window.</summary>
    private double _folded;

    /// <summary>
    /// Folds the equalizer <paramref name="by"/> shorter than its own height (as far as <see cref="MostFold"/>), as the line
    /// under its panel has been dragged up; 0 unfolds it. Its window's height stays its own.
    /// </summary>
    public void Fold(double by)
    {
        by = Math.Round(Math.Clamp(by, 0, MostFold));
        if (ClassicSkin is not null || Math.Abs(by - _folded) < 0.5)
        {
            return;
        }

        FinishGlide();
        _folded = by;
        ApplyFold();
        RedrawCurve();
    }

    /// <summary>The curve and the sliders at the heights the fold leaves them, all their own when it's unfolded.</summary>
    private void ApplyFold()
    {
        double curve;
        double sliders;
        if (_folded <= CurveHeight - ShortestCurve)
        {
            (curve, sliders) = (CurveHeight - _folded, SliderHeight);
        }
        else if (_folded <= CurveHeight - ShortestCurve + SliderHeight - ShortestSliders)
        {
            (curve, sliders) = (ShortestCurve, SliderHeight - (_folded - CurveHeight + ShortestCurve));
        }
        else
        {
            // The strip goes, and the sliders take its room back, so it's still folded by just as much.
            (curve, sliders) = (0, SliderHeight - (_folded - CurveHeight));
        }

        CurveFrame.Visibility = curve > 0 ? Visibility.Visible : Visibility.Collapsed;
        ResponseLabel.Visibility = curve > 0 ? Visibility.Visible : Visibility.Collapsed;
        CurveFrame.Height = curve > 0 ? curve : CurveHeight;
        ScaleBox.Height = sliders;
        foreach (var slider in _bandSliders.Append(PreampSlider))
        {
            if (_folded > 0)
            {
                slider.Height = sliders;
            }
            else
            {
                slider.ClearValue(FrameworkElement.HeightProperty);
            }
        }
    }
}
