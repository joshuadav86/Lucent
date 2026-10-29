using System.IO;
using System.Windows;
using System.Windows.Input;
using Lucent.Radio;
using Microsoft.Win32;

namespace Lucent;

/// <summary>Asks for a station that isn't listed: its stream's address, typed or read from a .pls or .m3u file, and a name.</summary>
public partial class AddStationWindow : Window
{
    private const string Explanation =
        "Paste the stream's address from the station's website. Its .pls, .m3u or .m3u8 link works too, and so do video stations.";

    private bool _favorite;

    private AddStationWindow()
    {
        InitializeComponent();
        Hint.Text = Explanation;
    }

    /// <summary>The station asked for and whether to keep it in the favorites, or null if cancelled.</summary>
    public static (RadioStation Station, bool Favorite)? Ask(Window owner)
    {
        var window = new AddStationWindow { Owner = owner };
        window.Loaded += (_, _) => window.Address.Focus();
        return window.ShowDialog() == true
            ? (RadioGuide.FromAddress(window.Address.Text, window.StationName.Text), window._favorite)
            : null;
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose the station's playlist file",
            Filter = "Station playlists (*.pls, *.m3u, *.m3u8)|*.pls;*.m3u;*.m3u8|All files (*.*)|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(dialog.FileName);
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

    private void OnPlay(object sender, RoutedEventArgs e) => Finish(favorite: false);

    private void OnAddToFavorites(object sender, RoutedEventArgs e) => Finish(favorite: true);

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnFieldKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Finish(favorite: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
    }

    private void Finish(bool favorite)
    {
        if (!RadioGuide.IsStreamAddress(Address.Text))
        {
            ShowProblem(Address.Text.Trim().Length == 0
                ? "Paste the stream's address first, or choose its .pls or .m3u file."
                : "That doesn't look like a web address (http:// or https://).");
            Address.Focus();
            return;
        }

        _favorite = favorite;
        DialogResult = true;
    }

    private void ShowProblem(string problem)
    {
        Hint.Text = problem;
        Hint.SetResourceReference(ForegroundProperty, "AccentBrush");
    }
}
