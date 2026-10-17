using Avalonia.Controls;
using Avalonia.Media;

namespace Lucent.Mac.Editor;

/// <summary>The fonts the editor's drawn views use: the app's own, and a fixed-width one for times and levels.</summary>
internal static class EditorFonts
{
    /// <summary>The Mac's fixed-width font, where Windows uses Consolas.</summary>
    public static readonly FontFamily Mono = new("Menlo, Consolas, monospace");

    /// <summary>The app's UiFont resource as seen from <paramref name="control"/>, so it follows the skin.</summary>
    public static FontFamily Ui(Control control) =>
        control.TryFindResource("UiFont", control.ActualThemeVariant, out var font) && font is FontFamily family
            ? family
            : FontFamily.Default;
}
