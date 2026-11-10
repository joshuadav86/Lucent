using System.IO;
using System.Windows;
using System.Windows.Input;
using Lucent.Radio;
using Microsoft.Win32;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// Asks for a station that isn't listed: its stream's address (typed, read from a .pls or .m3u file, or found on the
/// station's web page) and a name. A page that doesn't link its stream gives a name to search the list for instead.
/// </summary>
public partial class AddStationWindow : Window
{
    private const string Explanation =
        "Paste the stream's address or the station's web page, and Lucent finds the stream. A .pls, .m3u, or .m3u8 link works too, and so do video stations.";

    private readonly Func<string, string?, CancellationToken, Task<StationLookup>> _lookUp;
    private CancellationTokenSource? _looking;
    private StationLookup? _found;
    private bool _favorite;

    private AddStationWindow(Func<string, string?, CancellationToken, Task<StationLookup>> lookUp)
    {
        _lookUp = lookUp;
        InitializeComponent();
        Hint.Text = Explanation;
        Closed += (_, _) => _looking?.Cancel();
    }

    /// <summary>
    /// The station asked for and whether to keep it in the favorites, or what to search the list for when the page
    /// given doesn't link its stream. Null if cancelled. <paramref name="lookUp"/> is <see cref="StationAddress.LookUpAsync(string, string?, string, CancellationToken)"/>.
    /// </summary>
    public static (RadioStation? Station, bool Favorite, string? Search)? Ask(Window owner, Func<string, string?, CancellationToken, Task<StationLookup>> lookUp)
    {
        // Centred on the window the user sees: a window that's a panel in the single window is out of sight.
        var window = new AddStationWindow(lookUp) { Owner = SingleWindow.FrameOf(owner) };
        window.Loaded += (_, _) => window.Address.Focus();
        if (window.ShowDialog() != true || window._found is not { } found)
        {
            return null;
        }

        return found.Address is { } address
            ? (RadioGuide.FromAddress(address, window.StationName.Text), window._favorite, null)
            : (null, false, found.Search);
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

    private async void Finish(bool favorite)
    {
        if (_looking is not null)
        {
            return;
        }

        if (Address.Text.Trim().Length == 0)
        {
            ShowProblem("Paste the stream's address first, or choose its .pls or .m3u file.");
            Address.Focus();
            return;
        }

        var looking = _looking = new CancellationTokenSource();
        ShowLooking(true);
        try
        {
            var found = await _lookUp(Address.Text, StationName.Text, looking.Token);
            if (looking.IsCancellationRequested)
            {
                return;
            }

            if (found.Problem is { } problem)
            {
                ShowLooking(false);
                ShowProblem(problem);
                Address.Focus();
                return;
            }

            _found = found;
            _favorite = favorite;
            DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            // Cancelled, and closed with it.
        }
        finally
        {
            _looking = null;
            looking.Dispose();
        }
    }

    /// <summary>While the address is looked at, only Cancel works.</summary>
    private void ShowLooking(bool looking)
    {
        Address.IsEnabled = StationName.IsEnabled = FileButton.IsEnabled = PlayButton.IsEnabled = FavoriteButton.IsEnabled = !looking;
        if (looking)
        {
            Hint.Text = "Looking at that address…";
            Hint.SetResourceReference(ForegroundProperty, "MutedTextBrush");
        }
    }

    private void ShowProblem(string problem)
    {
        Hint.Text = problem;
        Hint.SetResourceReference(ForegroundProperty, "AccentBrush");
    }
}
