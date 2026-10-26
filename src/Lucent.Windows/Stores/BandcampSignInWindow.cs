using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Lucent.Stores;

/// <summary>
/// Bandcamp's own sign-in page in a small browser window. Lucent never sees the password: once the page
/// has signed in, the window keeps the website's cookies, checks they work, and closes.
/// </summary>
public sealed class BandcampSignInWindow : Window
{
    private const string SignInPage = "https://bandcamp.com/login";

    private readonly WebView2 _browser = new();
    private bool _checking;

    private BandcampSignInWindow()
    {
        Title = "Sign in to Bandcamp - Lucent";
        Width = 520;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Content = _browser;
        Loaded += async (_, _) => await OpenAsync();
    }

    /// <summary>The signed-in cookies and the fan's username, once signed in.</summary>
    private (string Cookies, string Username)? Result { get; set; }

    /// <summary>Shows Bandcamp's sign-in page and returns the cookies and username, or null if the window was closed first.</summary>
    public static (string Cookies, string Username)? SignIn(Window owner)
    {
        var window = new BandcampSignInWindow { Owner = owner };
        window.ShowDialog();
        return window.Result;
    }

    private async Task OpenAsync()
    {
        try
        {
            Directory.CreateDirectory(BandcampSignIn.BrowserDataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(null, BandcampSignIn.BrowserDataFolder);
            await _browser.EnsureCoreWebView2Async(environment);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            var answer = MessageBox.Show(this,
                "Signing in to Bandcamp needs Microsoft Edge WebView2, which isn't installed on this PC.\n\n" +
                "Open Microsoft's download page now?",
                "Bandcamp", MessageBoxButton.YesNo, MessageBoxImage.Information);
            if (answer == MessageBoxResult.Yes)
            {
                Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            }

            Close();
            return;
        }

        _browser.CoreWebView2.NavigationCompleted += async (_, _) => await CheckSignedInAsync();
        _browser.CoreWebView2.Navigate(SignInPage);
    }

    /// <summary>After each page loads: if Bandcamp has signed in (its "identity" cookie), check it works and finish.</summary>
    private async Task CheckSignedInAsync()
    {
        if (_checking || Result is not null)
        {
            return;
        }

        _checking = true;
        try
        {
            var cookies = await _browser.CoreWebView2.CookieManager.GetCookiesAsync("https://bandcamp.com/");
            if (!cookies.Any(cookie => cookie.Name == "identity" && cookie.Value.Length > 0))
            {
                return;
            }

            var header = string.Join("; ", cookies.Select(cookie => $"{cookie.Name}={cookie.Value}"));
            using var client = new BandcampClient(header);
            var username = await client.GetUsernameAsync(CancellationToken.None);
            Result = (header, username);
            Close();
        }
        catch (Exception ex) when (ex is BandcampSignInException or BandcampPageException or HttpRequestException or System.Text.Json.JsonException)
        {
            // Not signed in yet (or not finished): keep the page open.
        }
        finally
        {
            _checking = false;
        }
    }
}
