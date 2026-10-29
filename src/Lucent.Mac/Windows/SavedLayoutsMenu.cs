using Avalonia.Controls;
using Avalonia.Input;
using Lucent.Layout;

namespace Lucent.Mac.Windows;

/// <summary>
/// "Layouts" in the right-click menus of Lucent's windows, under "Tidy layout": the window layouts saved by
/// name, to put the windows back that way (the first nine also on the keys 1 to 9), then "Save current
/// layout…" and, once there are some, "Rename layout" and "Delete layout". It goes in with "Full screen
/// Lucent" (<see cref="LucentFullScreen.AddTo(ContextMenu, Window)"/>). The work is done by the player
/// (MainWindow.Layouts.cs), which sets this up as Lucent starts.
/// </summary>
public static class SavedLayoutsMenu
{
    /// <summary>The saved layouts, in order.</summary>
    public static Func<IReadOnlyList<SavedLayout>>? Layouts { get; set; }

    /// <summary>Puts the windows back as a layout has them.</summary>
    public static Action<SavedLayout>? Open { get; set; }

    /// <summary>Asks for a name, over the window the menu was in, and saves the windows as they are now under it.</summary>
    public static Action<Window>? Save { get; set; }

    /// <summary>Asks for a new name for a layout, over the window the menu was in.</summary>
    public static Action<Window, SavedLayout>? Rename { get; set; }

    public static Action<SavedLayout>? Delete { get; set; }

    /// <summary>"Layouts", with its items for now. A menu that's kept fills it again as it opens (<see cref="Fill"/>).</summary>
    public static MenuItem Item(Window window)
    {
        var item = new MenuItem { Header = "Layouts" };
        Fill(item, window);
        return item;
    }

    /// <summary>Lists the saved layouts in "Layouts" again, as they are now.</summary>
    public static void Fill(MenuItem item, Window window)
    {
        item.Items.Clear();
        var layouts = Layouts?.Invoke() ?? Array.Empty<SavedLayout>();
        foreach (var layout in layouts)
        {
            var key = SavedLayouts.KeyOf(layouts, layout);
            var entry = new MenuItem { Header = Shown(layout.Name), InputGesture = key is null ? null : new KeyGesture(Key.D0 + int.Parse(key)) };
            entry.Click += (_, _) => Open?.Invoke(layout);
            item.Items.Add(entry);
        }

        if (layouts.Count > 0)
        {
            item.Items.Add(new Separator());
        }

        var save = new MenuItem { Header = "Save current layout…" };
        save.Click += (_, _) => Save?.Invoke(window);
        item.Items.Add(save);
        if (layouts.Count == 0)
        {
            return;
        }

        var rename = new MenuItem { Header = "Rename layout" };
        var delete = new MenuItem { Header = "Delete layout" };
        foreach (var layout in layouts)
        {
            var renameOne = new MenuItem { Header = Shown(layout.Name) + "…" };
            renameOne.Click += (_, _) => Rename?.Invoke(window, layout);
            rename.Items.Add(renameOne);
            var deleteOne = new MenuItem { Header = Shown(layout.Name) };
            deleteOne.Click += (_, _) => Delete?.Invoke(layout);
            delete.Items.Add(deleteOne);
        }

        item.Items.Add(rename);
        item.Items.Add(delete);
    }

    /// <summary>A name as a menu shows it: an underscore in it stays one, rather than marking a shortcut letter.</summary>
    private static string Shown(string name) => name.Replace("_", "__");
}
