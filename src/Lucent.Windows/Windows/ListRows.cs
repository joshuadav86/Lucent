using System.Windows.Controls;

namespace WinampInspiredVisualizer.Windows;

/// <summary>Keyboard help for the lists that have a search box above them.</summary>
public static class ListRows
{
    /// <summary>Selects a row in a list, scrolls to it and puts the keyboard focus on it, e.g. after Down in a search box.</summary>
    public static void Focus(ListBox list, object item)
    {
        list.SelectedItem = item;
        list.ScrollIntoView(item);
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(item) is ListBoxItem row)
        {
            row.Focus();
        }
        else
        {
            list.Focus();
        }
    }
}
