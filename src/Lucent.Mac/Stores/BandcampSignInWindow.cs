using System.Net.Http;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucent.Mac.Interop;
using Lucent.Stores;

namespace Lucent.Mac.Stores;

/// <summary>
/// Bandcamp's own sign-in page in a small browser window (Safari's WebKit, where Windows uses WebView2).
/// Lucent never sees the password: once the page has signed in, the window keeps the website's
/// cookies, checks they work, and closes.
/// </summary>
public sealed class BandcampSignInWindow : Window
{
    private const string SignInPage = "https://bandcamp.com/login";

    // Only one sign-in window is open at a time (it's modal), so WebKit's callbacks go to this one.
    private static BandcampSignInWindow? _current;
    private static TaskCompletionSource<IReadOnlyList<Cookie>>? _cookieRequest;
    private static IntPtr _cookiesBlock;
    private static IntPtr _doneBlock;
    private static IntPtr _delegateClass;

    private bool _checking;

    internal BandcampSignInWindow()
    {
        Title = "Sign in to Bandcamp - Lucent";
        Width = 520;
        Height = 720;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Brushes.White;
        Content = new BrowserHost();
        _current = this;
        Activated += (_, _) => AddEditMenu();
        Closed += (_, _) =>
        {
            RemoveEditMenu();
            if (_current == this)
            {
                _current = null;
            }
        };
    }

    /// <summary>The signed-in cookies and the fan's username, once signed in.</summary>
    private (string Cookies, string Username)? Result { get; set; }

    /// <summary>Shows Bandcamp's sign-in page and returns the cookies and username, or null if the window was closed first.</summary>
    public static async Task<(string Cookies, string Username)?> SignInAsync(Window owner)
    {
        var window = new BandcampSignInWindow();
        await window.ShowDialog(owner);
        return window.Result;
    }

    /// <summary>Forgets everything the sign-in page's browser kept (Bandcamp's cookies and site data).</summary>
    public static void ClearBrowserData()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var store = ObjC.Send(ObjC.Class("WKWebsiteDataStore"), "defaultDataStore");
            var types = ObjC.Send(ObjC.Class("WKWebsiteDataStore"), "allWebsiteDataTypes");
            var distantPast = ObjC.Send(ObjC.Class("NSDate"), "distantPast");
            WebKit.SendVoid(store, "removeDataOfTypes:modifiedSince:completionHandler:", types, distantPast, DoneBlock());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            // The browser may still be closing; the website's own sign-out still applies.
        }
    }

    /// <summary>After each page loads: if Bandcamp has signed in (its "identity" cookie), check it works and finish.</summary>
    private async Task CheckSignedInAsync(IntPtr cookieStore)
    {
        if (_checking || Result is not null)
        {
            return;
        }

        _checking = true;
        try
        {
            var cookies = (await GetCookiesAsync(cookieStore)).Where(AppliesToBandcamp).ToList();
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

    /// <summary>The cookies WebView2's GetCookiesAsync("https://bandcamp.com/") would give: bandcamp.com's own and its parent domain's, for every path.</summary>
    private static bool AppliesToBandcamp(Cookie cookie) =>
        cookie.Domain.TrimStart('.').Equals("bandcamp.com", StringComparison.OrdinalIgnoreCase) && cookie.Path is "" or "/";

    /// <summary>Every cookie in the browser's cookie store.</summary>
    private static Task<IReadOnlyList<Cookie>> GetCookiesAsync(IntPtr cookieStore)
    {
        var request = new TaskCompletionSource<IReadOnlyList<Cookie>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cookieRequest = request;
        ObjC.SendVoid(cookieStore, "getAllCookies:", CookiesBlock());
        return request.Task;
    }

    private static unsafe IntPtr CookiesBlock()
    {
        if (_cookiesBlock == IntPtr.Zero)
        {
            _cookiesBlock = WebKit.GlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, IntPtr, void>)&OnCookies, "v16@?0@8");
        }

        return _cookiesBlock;
    }

    private static unsafe IntPtr DoneBlock()
    {
        if (_doneBlock == IntPtr.Zero)
        {
            _doneBlock = WebKit.GlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, void>)&OnDone, "v8@?0");
        }

        return _doneBlock;
    }

    /// <summary>WKHTTPCookieStore's getAllCookies: answer, an NSArray of NSHTTPCookie.</summary>
    [UnmanagedCallersOnly]
    private static void OnCookies(IntPtr block, IntPtr array)
    {
        var cookies = new List<Cookie>();
        try
        {
            for (nuint i = 0, count = WebKit.Count(array); i < count; i++)
            {
                var cookie = WebKit.ObjectAt(array, i);
                cookies.Add(new Cookie(
                    WebKit.ReadString(ObjC.Send(cookie, "name")),
                    WebKit.ReadString(ObjC.Send(cookie, "value")),
                    WebKit.ReadString(ObjC.Send(cookie, "domain")),
                    WebKit.ReadString(ObjC.Send(cookie, "path"))));
            }
        }
        catch (Exception)
        {
            // An exception mustn't cross back into WebKit; whatever was read is used.
        }

        var request = _cookieRequest;
        _cookieRequest = null;
        request?.TrySetResult(cookies);
    }

    [UnmanagedCallersOnly]
    private static void OnDone(IntPtr block)
    {
    }

    /// <summary>WKNavigationDelegate's webView:didFinishNavigation:: a page has loaded.</summary>
    [UnmanagedCallersOnly]
    private static void OnNavigationFinished(IntPtr self, IntPtr selector, IntPtr webView, IntPtr navigation)
    {
        try
        {
            var configuration = ObjC.Send(webView, "configuration");
            var cookieStore = ObjC.Send(ObjC.Send(configuration, "websiteDataStore"), "httpCookieStore");
            Dispatcher.UIThread.Post(async () =>
            {
                if (_current is { } window)
                {
                    await window.CheckSignedInAsync(cookieStore);
                }
            });
        }
        catch (Exception)
        {
            // An exception mustn't cross back into WebKit; the next page load checks again.
        }
    }

    /// <summary>
    /// WKUIDelegate's webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:: a link
    /// that opens a new window (WebView2 would open a popup) opens in the sign-in window itself.
    /// </summary>
    [UnmanagedCallersOnly]
    private static IntPtr OnNewWindow(IntPtr self, IntPtr selector, IntPtr webView, IntPtr configuration, IntPtr action, IntPtr features)
    {
        try
        {
            var request = ObjC.Send(action, "request");
            if (request != IntPtr.Zero)
            {
                ObjC.Send(webView, "loadRequest:", request);
            }
        }
        catch (Exception)
        {
            // An exception mustn't cross back into WebKit.
        }

        return IntPtr.Zero;
    }

    private static unsafe IntPtr DelegateClass()
    {
        if (_delegateClass == IntPtr.Zero)
        {
            _delegateClass = WebKit.DefineClass("LucentBandcampSignInDelegate",
                ("webView:didFinishNavigation:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, void>)&OnNavigationFinished, "v@:@@"),
                ("webView:createWebViewWithConfiguration:forNavigationAction:windowFeatures:",
                    (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr, IntPtr>)&OnNewWindow, "@@:@@@@"));
            WebKit.AddProtocol(_delegateClass, "WKNavigationDelegate");
            WebKit.AddProtocol(_delegateClass, "WKUIDelegate");
        }

        return _delegateClass;
    }

    /// <summary>
    /// WebKit only cuts, copies and pastes through the menu bar's Edit menu (⌘X, ⌘C, ⌘V), so the sign-in
    /// page gets one while it's open: pasting a password works as it does in WebView2 on Windows.
    /// </summary>
    private static void AddEditMenu()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var mainMenu = ObjC.Send(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), "mainMenu");
            if (mainMenu == IntPtr.Zero || ObjC.Send(mainMenu, "itemWithTitle:", WebKit.String(EditMenuTitle)) != IntPtr.Zero)
            {
                return;
            }

            var menu = ObjC.Send(ObjC.Send(ObjC.Class("NSMenu"), "alloc"), "initWithTitle:", WebKit.String(EditMenuTitle));
            foreach (var (title, action, key) in EditCommands)
            {
                if (title.Length == 0)
                {
                    ObjC.SendVoid(menu, "addItem:", ObjC.Send(ObjC.Class("NSMenuItem"), "separatorItem"));
                    continue;
                }

                var item = WebKit.Send(ObjC.Send(ObjC.Class("NSMenuItem"), "alloc"), "initWithTitle:action:keyEquivalent:",
                    WebKit.String(title), ObjC.Selector(action), WebKit.String(key));
                ObjC.SendVoid(menu, "addItem:", item);
                ObjC.Release(item);
            }

            var editItem = WebKit.Send(ObjC.Send(ObjC.Class("NSMenuItem"), "alloc"), "initWithTitle:action:keyEquivalent:",
                WebKit.String(EditMenuTitle), IntPtr.Zero, WebKit.String(string.Empty));
            ObjC.SendVoid(editItem, "setSubmenu:", menu);
            ObjC.SendVoid(mainMenu, "addItem:", editItem);
            ObjC.Release(editItem);
            ObjC.Release(menu);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            // Typing still works; only the menu shortcuts are missing.
        }
    }

    private static void RemoveEditMenu()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var mainMenu = ObjC.Send(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), "mainMenu");
            var editItem = mainMenu == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(mainMenu, "itemWithTitle:", WebKit.String(EditMenuTitle));
            if (editItem != IntPtr.Zero)
            {
                ObjC.SendVoid(mainMenu, "removeItem:", editItem);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException)
        {
            // Nothing was added.
        }
    }

    private const string EditMenuTitle = "Edit";

    private static readonly (string Title, string Action, string Key)[] EditCommands =
    {
        ("Undo", "undo:", "z"),
        ("Redo", "redo:", "Z"),
        (string.Empty, string.Empty, string.Empty),
        ("Cut", "cut:", "x"),
        ("Copy", "copy:", "c"),
        ("Paste", "paste:", "v"),
        ("Select All", "selectAll:", "a"),
    };

    private readonly record struct Cookie(string Name, string Value, string Domain, string Path);

    /// <summary>The WKWebView, hosted in the window like the visuals' Metal view is in the player.</summary>
    private sealed class BrowserHost : NativeControlHost
    {
        private IntPtr _webView;
        private IntPtr _delegate;

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            if (!OperatingSystem.IsMacOS())
            {
                return base.CreateNativeControlCore(parent);
            }

            var configuration = ObjC.New("WKWebViewConfiguration");
            ObjC.SendVoid(configuration, "setWebsiteDataStore:", ObjC.Send(ObjC.Class("WKWebsiteDataStore"), "defaultDataStore"));
            // Present as Safari, so Bandcamp's sign-in (and its captcha) treats the page like any other visit.
            ObjC.SendVoid(configuration, "setApplicationNameForUserAgent:", WebKit.String("Version/17.6 Safari/605.1.15"));
            _webView = WebKit.Send(ObjC.Send(ObjC.Class("WKWebView"), "alloc"), "initWithFrame:configuration:",
                new ObjC.CGRect { Width = Bounds.Width > 0 ? Bounds.Width : 520, Height = Bounds.Height > 0 ? Bounds.Height : 720 }, configuration);
            ObjC.Release(configuration);

            _delegate = ObjC.Send(ObjC.Send(DelegateClass(), "alloc"), "init");
            ObjC.SendVoid(_webView, "setNavigationDelegate:", _delegate);
            ObjC.SendVoid(_webView, "setUIDelegate:", _delegate);

            var url = ObjC.Send(ObjC.Class("NSURL"), "URLWithString:", WebKit.String(SignInPage));
            var request = ObjC.Send(ObjC.Class("NSURLRequest"), "requestWithURL:", url);
            ObjC.Send(_webView, "loadRequest:", request);
            return new PlatformHandle(_webView, "NSView");
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (!OperatingSystem.IsMacOS())
            {
                base.DestroyNativeControlCore(control);
                return;
            }

            ObjC.SendVoid(_webView, "stopLoading");
            ObjC.SendVoid(_webView, "setNavigationDelegate:", IntPtr.Zero);
            ObjC.SendVoid(_webView, "setUIDelegate:", IntPtr.Zero);
            ObjC.Release(_webView);
            ObjC.Release(_delegate);
            _webView = IntPtr.Zero;
            _delegate = IntPtr.Zero;
        }
    }
}
