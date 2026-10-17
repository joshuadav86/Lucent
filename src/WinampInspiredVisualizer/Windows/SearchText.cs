using System.Globalization;
using System.Text;
using System.Windows.Controls;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// The search boxes in the library and the visual picker. What's typed is split into words, and a row
/// matches when every word is found somewhere in it, ignoring case and accents ("beyonce" finds "Beyoncé").
/// </summary>
public static class SearchText
{
    /// <summary>The words typed in a search box, folded for matching. Empty when nothing is typed.</summary>
    public static string[] Words(string? text) =>
        Fold(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A row's fields, folded and joined into one string to search in.</summary>
    public static string Key(IEnumerable<string?> fields) => Fold(string.Join('\n', fields));

    /// <summary>Whether every word is in the key (always true when there are no words).</summary>
    public static bool Matches(string key, IReadOnlyList<string> words)
    {
        foreach (var word in words)
        {
            if (!key.Contains(word, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Lower case, without accents.</summary>
    private static string Fold(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(char.ToLowerInvariant(c));
            }
        }

        return folded.ToString();
    }

    /// <summary>Selects a row in a list, scrolls to it and puts the keyboard focus on it, e.g. after Down in a search box.</summary>
    public static void FocusRow(ListBox list, object item)
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
