using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Views;

public partial class PublicLibraryWebWindow : Window
{
    private bool _initialized;

    /// <summary>First navigation URL; defaults to embedded public map.</summary>
    public string InitialUrl { get; set; } = PublicLibraryCatalog.WebMapUrlEmbedded;

    public PublicLibraryWebWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CaveAiProForWindows",
                "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await LibraryWebView.EnsureCoreWebView2Async(environment);

            var core = LibraryWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 core is unavailable.");

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.UserAgent = core.Settings.UserAgent + " CaveAiProForWindows/1.0";

            // Passive capture of Firebase ID tokens on Firestore/Storage requests (Push to Cloud REST uploads).
            CloudPublishWebViewHost.EnsureAuthSnifferAttached(core);

            core.NewWindowRequested += (_, args) =>
            {
                // Google OAuth / Firebase auth popups — navigate in the same view instead of blocking.
                args.Handled = true;
                if (!string.IsNullOrWhiteSpace(args.Uri))
                    core.Navigate(args.Uri);
            };

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

            var start = string.IsNullOrWhiteSpace(InitialUrl)
                ? PublicLibraryCatalog.WebMapUrlEmbedded
                : InitialUrl;
            core.Navigate(start);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "WebView2 could not start. Install the Microsoft Edge WebView2 Runtime, or use Help → Public Library → Open in browser.\n\n" +
                ex.Message,
                "Public Cave Library",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static bool IsAllowedNavigation(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme is not ("http" or "https"))
            return false;

        var origin = PublicLibraryCatalog.WebOrigin;
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var allowedOrigin))
            return true;

        if (string.Equals(parsed.Host, allowedOrigin.Host, StringComparison.OrdinalIgnoreCase))
            return true;

        // Firebase Auth / Google OAuth during sign-in.
        if (parsed.Host.EndsWith(".google.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".googleusercontent.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".firebaseapp.com", StringComparison.OrdinalIgnoreCase)
            || parsed.Host.EndsWith(".web.app", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryWebView?.CoreWebView2?.CanGoBack == true)
            LibraryWebView.CoreWebView2.GoBack();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        LibraryWebView?.CoreWebView2?.Reload();
    }

    /// <summary>Sniffed Firebase Auth token from this WebView session (null until user signs in and Firestore loads).</summary>
    public FirebaseIdToken? TryGetSniffedFirebaseToken() =>
        CloudPublishWebViewHost.TokenCache.TryGetUsableToken();

    private void External_Click(object sender, RoutedEventArgs e)
    {
        var url = LibraryWebView?.Source?.ToString();
        if (string.IsNullOrWhiteSpace(url))
            url = PublicLibraryCatalog.WebMapUrlEmbedded;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open in browser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
