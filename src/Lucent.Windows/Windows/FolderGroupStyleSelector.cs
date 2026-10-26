using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Lucent.Library;

namespace Lucent.Windows;

/// <summary>
/// How the library's folder groups look. The list is grouped twice: by section (a store's one heading, or a
/// folder's own), then by folder. A section gets a heading; inside a store's, each folder gets a subheading;
/// anywhere else the folder is the section itself, so it shows just its songs.
/// </summary>
public sealed class FolderGroupStyleSelector : StyleSelector
{
    public Style? Heading { get; set; }

    public Style? Subheading { get; set; }

    public Style? NoHeading { get; set; }

    public override Style? SelectStyle(object item, DependencyObject container) => item switch
    {
        CollectionViewGroup { IsBottomLevel: false } => Heading,
        CollectionViewGroup group when group.Items.OfType<LibraryItem>().FirstOrDefault() is { IsInSection: true } => Subheading,
        _ => NoHeading,
    };
}
