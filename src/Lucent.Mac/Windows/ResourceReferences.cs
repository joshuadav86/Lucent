using Avalonia;
using Avalonia.Controls;

namespace Lucent.Mac.Windows;

/// <summary>WPF's SetResourceReference, for code that colours or shapes a control from a theme or skin resource.</summary>
public static class ResourceReferences
{
    /// <summary>
    /// Makes <paramref name="property"/> follow the resource <paramref name="key"/>, so it changes when
    /// the theme or skin does (like a DynamicResource in XAML).
    /// </summary>
    public static void SetResourceReference(this Control control, AvaloniaProperty property, string key) =>
        control.Bind(property, control.GetResourceObservable(key));
}
