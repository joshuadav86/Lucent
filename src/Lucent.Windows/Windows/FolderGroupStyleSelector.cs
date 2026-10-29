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
