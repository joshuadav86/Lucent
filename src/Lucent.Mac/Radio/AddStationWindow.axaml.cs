using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lucent.Mac.Windows;
using Lucent.Radio;

namespace Lucent.Mac;

/// <summary>Asks for a station that isn't listed: its stream's address, typed or read from a .pls or .m3u file, and a name.</summary>
public partial class AddStationWindow : Window
{
    private const string Explanation =
        "Paste the stream's address from the station's website. Its .pls, .m3u or .m3u8 link works too, and so do video stations.";

    private bool _favorite;

    public AddStationWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        Hint.Text = Explanation;
    }

    /// <summary>The station asked for and whether to keep it in the favorites, or null if cancelled.</summary>
    /// <remarks>Windows' version waits in ShowDialog; Avalonia's dialogs are awaited instead.</remarks>
    public static async Task<(RadioStation Station, bool Favorite)?> AskAsync(Window owner)
    {
        var window = new AddStationWindow();
        window.Opened += (_, _) => window.Address.Focus();
        return await window.ShowDialog<bool>(owner)
            ? (RadioGuide.FromAddress(window.Address.Text ?? string.Empty, window.StationName.Text), window._favorite)
            : null;
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the station's playlist file",
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Station playlists") { Patterns = new[] { "*.pls", "*.m3u", "*.m3u8" } },
                FilePickerFileTypes.All,
            },
        });
        if (chosen.Count == 0 || chosen[0].TryGetLocalPath() is not { } file)
        {
            return;
        }

        string text;
        try
        {
            text = await File.ReadAllTextAsync(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowProblem("Lucent couldn't read that file.");
            return;
        }

        if (RadioGuide.FirstStreamIn(text) is not { } found)
        {
            ShowProblem("That file doesn't list a web stream (an http:// or https:// address).");
            return;
        }

        Address.Text = found.Address;
        if (string.IsNullOrWhiteSpace(StationName.Text) && found.Name is { } name)
        {
            StationName.Text = name;
        }
    }

    private void OnPlay(object? sender, RoutedEventArgs e) => Finish(favorite: false);

    private void OnAddToFavorites(object? sender, RoutedEventArgs e) => Finish(favorite: true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnFieldKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish(favorite: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    private void Finish(bool favorite)
    {
        var address = (Address.Text ?? string.Empty).Trim();
        if (!RadioGuide.IsStreamAddress(address))
        {
            ShowProblem(address.Length == 0
                ? "Paste the stream's address first, or choose its .pls or .m3u file."
                : "That doesn't look like a web address (http:// or https://).");
            Address.Focus();
            return;
        }

        _favorite = favorite;
        Close(true);
    }

    private void ShowProblem(string problem)
    {
        Hint.Text = problem;
        Hint.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
    }
}
