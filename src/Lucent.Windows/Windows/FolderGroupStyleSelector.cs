using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Lucent.Library;

namespace Lucent.Windows;

/// <summary>
/// How the library's folder groups look. The list is grouped twice: by heading (a store's one heading, a
/// folder's own, or an artist's), then by subheading (a folder, or an album). A heading shows; inside a store's,
/// each folder gets a subheading, and inside an artist's each album does; anywhere else the subheading is the
/// heading itself, so it shows just its songs.
/// </summary>
public sealed class FolderGroupStyleSelector : StyleSelector
{
    public Style? Heading { get; set; }

    public Style? Subheading { get; set; }

    public Style? NoHeading { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) => item switch
    {
        CollectionViewGroup { IsBottomLevel: false } => Heading,
        CollectionViewGroup group when group.Items.OfType<LibraryItem>().FirstOrDefault() is { HasSubheading: true } => Subheading,
        _ => NoHeading,
    };
}

/// <summary>
/// A heading's count: the songs under it, leaving out the stand-in row an empty playlist shows, so an empty one says 0.
/// Bound to the group's ItemCount (so it updates) and the group itself.
/// </summary>
public sealed class HeadingCountConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        (values.Length > 1 && values[1] is CollectionViewGroup group ? Songs(group) : values[0] is int count ? count : 0).ToString(culture);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();

    private static int Songs(CollectionViewGroup group) => group.Items.Sum(item => item switch
    {
        CollectionViewGroup inner => Songs(inner),
        LibraryItem { IsPlaceholder: true } => 0,
        _ => 1,
    });
}
