using System.Windows;
using System.Windows.Controls;

namespace Lucent.Windows;

/// <summary>
/// A <see cref="Border"/> whose corners are never rounder than its size allows. WPF draws a corner radius larger than
/// half the border's height as an oval: <c>RoundCorner</c>'s 999 on a 36 × 20 switch gives an ellipse, not a pill
/// with round ends. This shrinks all four radii together until neighbouring corners just meet, as CSS and Avalonia
/// (the Mac app) do, so pills keep half-circle ends and square knobs stay circles. Use it for anything shaped from
/// the skin's corners (some skins make them larger) or with a radius that could outgrow the shape.
/// </summary>
public sealed class RoundedBorder : Border
{
    static RoundedBorder()
    {
        CornerRadiusProperty.OverrideMetadata(typeof(RoundedBorder), new FrameworkPropertyMetadata(
            new CornerRadius(),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender,
            null,
            (element, wanted) => Fit((CornerRadius)wanted, ((RoundedBorder)element).RenderSize)));
    }

    // Coercion keeps the radius asked for (a skin's resource, a style or a trigger) and fits it again at each new size.
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CoerceValue(CornerRadiusProperty);
    }

    /// <summary>
    /// The corners scaled down together, if need be, so the two on any side add up to no more than that side's length.
    /// Unchanged before the border has a size.
    /// </summary>
    public static CornerRadius Fit(CornerRadius wanted, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0)
        {
            return wanted;
        }

        var scale = Math.Min(
            Math.Min(Room(size.Width, wanted.TopLeft + wanted.TopRight), Room(size.Width, wanted.BottomLeft + wanted.BottomRight)),
            Math.Min(Room(size.Height, wanted.TopLeft + wanted.BottomLeft), Room(size.Height, wanted.TopRight + wanted.BottomRight)));
        return scale >= 1
            ? wanted
            : new CornerRadius(wanted.TopLeft * scale, wanted.TopRight * scale, wanted.BottomRight * scale, wanted.BottomLeft * scale);
    }

    private static double Room(double side, double radii) => radii > side ? side / radii : 1;
}
