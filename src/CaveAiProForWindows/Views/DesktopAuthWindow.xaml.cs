using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Modal WebView2 sign-in for Push to Cloud — navigates to the secure desktop auth endpoint
/// and receives the Firebase ID token through the postMessage bridge.
/// </summary>
public partial class DesktopAuthWindow : Window
{
    private readonly FirebaseAuthTokenCache _cache;
    private readonly TaskCompletionSource<FirebaseIdToken?> _completion;
    private bool _initialized;
    private bool _completed;

    private DesktopAuthWindow(FirebaseAuthTokenCache cache, TaskCompletionSource<FirebaseIdToken?> completion)
    {
        _cache = cache;
        _completion = completion;
        InitializeComponent();
        Loaded += OnLoadedAsync;
        Closed += (_, _) => Complete(null);
    }

    /// <summary>
    /// Opens the auth window and returns when a usable token arrives or the user closes / cancels.
    /// </summary>
    public static async Task<FirebaseIdToken?> AcquireTokenAsync(Window? owner, FirebaseAuthTokenCache cache)
    {
        var existing = cache.TryGetUsableToken();
        if (existing != null)
            return existing;

        var tcs = new TaskCompletionSource<FirebaseIdToken?>(TaskCreationOptions.RunContinuationsAsynchronously);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var window = new DesktopAuthWindow(cache, tcs) { Owner = owner };
            window.ShowDialog();
            if (!tcs.Task.IsCompleted)
                tcs.TrySetResult(cache.TryGetUsableToken());
        }).Task.ConfigureAwait(true);

        return await tcs.Task.ConfigureAwait(true);
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;
        _initialized = true;

        var cached = _cache.TryGetUsableToken();
        if (cached != null)
        {
            Complete(cached);
            Close();
            return;
        }

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await AuthWebView.EnsureCoreWebView2Async(environment);

            var core = AuthWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 core is unavailable.");

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.UserAgent = core.Settings.UserAgent + " CaveAiProForWindows/1.0";

            CloudPublishWebViewHost.EnsureAuthBridgeAttached(core);
            DesktopAuthFallback.TryRegisterVirtualHost(core);
            await DesktopAuthFallback.EnsureFirebaseConfigScriptRegisteredAsync(core).ConfigureAwait(true);

            WebView2AuthPopupHost.WirePopupHandling(core, this, IsAllowedNavigation);

            core.NavigationStarting += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Uri))
                    return;
                if (!IsAllowedNavigation(args.Uri))
                {
                    args.Cancel = true;
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri)
                        {
                            UseShellExecute = true,
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            _cache.TokenUpdated += OnCacheTokenUpdated;
            Closed += (_, _) => _cache.TokenUpdated -= OnCacheTokenUpdated;

            StatusText.Text = "Sign in with Google on the page below…";
            if (DesktopAuthFallback.HasUsableFirebaseConfig())
            {
                await DesktopAuthFallback.PrepareFallbackNavigationAsync(core).ConfigureAwait(true);
                core.Navigate(DesktopAuthFallback.FallbackUri);
            }
            else
            {
                core.Navigate(PublicLibraryCatalog.DesktopAuthUrl);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "WebView2 could not start.";
            MessageBox.Show(
                this,
                "WebView2 could not start. Install the Microsoft Edge WebView2 Runtime.\n\n" + ex.Message,
                "Sign in — Push to Cloud",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnCacheTokenUpdated(object? sender, FirebaseIdToken token)
    {
        if (!token.IsUsable())
            return;

        Dispatcher.Invoke(() =>
        {
            StatusText.Text = "Signed in — closing…";
            Complete(token);
            Close();
        });
    }

    private void Complete(FirebaseIdToken? token)
    {
        if (_completed)
            return;
        _completed = true;
        _completion.TrySetResult(token ?? _cache.TryGetUsableToken());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Complete(null);
        Close();
    }

    private void FallbackAuth_Click(object sender, RoutedEventArgs e)
    {
        var core = AuthWebView?.CoreWebView2;
        if (core == null)
            return;
        StatusText.Text = "Bundled sign-in — use Google on this page…";
        _ = NavigateFallbackAsync(core);
    }

    private static async Task NavigateFallbackAsync(CoreWebView2 core)
    {
        await DesktopAuthFallback.PrepareFallbackNavigationAsync(core).ConfigureAwait(true);
        core.Navigate(DesktopAuthFallback.FallbackUri);
    }

    private static bool IsAllowedNavigation(string uri)
    {
        if (DesktopAuthFallback.IsFallbackUri(uri))
            return true;

        return PublicLibraryWebWindowNavigationPolicy.IsAllowed(uri);
    }
}
