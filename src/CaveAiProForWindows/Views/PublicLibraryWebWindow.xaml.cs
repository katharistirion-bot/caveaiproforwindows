using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

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
        Closing += OnClosingPersistViewport;
    }

    private void OnClosingPersistViewport(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var url = LibraryWebView?.Source?.ToString();
        if (!string.IsNullOrWhiteSpace(url) &&
            url.Contains("view=explore", StringComparison.OrdinalIgnoreCase))
        {
            PublicLibraryCatalog.RememberExploreMapViewportUrl(url);
        }
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

            // postMessage bridge for Push to Cloud.
            CloudPublishWebViewHost.EnsureAuthBridgeAttached(core);

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
                if (!PublicLibraryWebWindowNavigationPolicy.IsAllowed(args.Uri))
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
    public FirebaseIdToken? TryGetFirebaseToken() =>
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

    private async void DownloadBackup_Click(object sender, RoutedEventArgs e)
    {
        var url = LibraryWebView?.Source?.ToString();
        if (!PublishedCaveUrlParser.TryExtractDocId(url, out var docId) || string.IsNullOrWhiteSpace(docId))
        {
            MessageBox.Show(
                this,
                "Open a cave on the map first (?cave= document id in the URL).",
                "Download as backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dlg = new SaveFileDialog
        {
            Title = "Download Public Library backup",
            Filter = "CaveAI ZIP|*.zip|JSON|*.json",
            FileName = docId + ".zip",
            DefaultExt = ".zip",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        try
        {
            var token = TryGetFirebaseToken();
            var progress = new Progress<string>(m => Title = "Public Library — " + m);
            var result = await PublicLibraryBackupDownloader.DownloadAsync(
                docId,
                dlg.FileName,
                token).ConfigureAwait(true);

            var openNow = MessageBox.Show(
                this,
                $"Saved {(result.CaveName ?? docId)} with {result.AssetCount} cartography asset(s).\n\n{result.OutputPath}\n\nOpen in workspace now?",
                "Download as backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (openNow == MessageBoxResult.Yes && Application.Current.MainWindow is MainWindow shell &&
                shell.DataContext is ViewModels.MainViewModel vm)
            {
                vm.LoadFromPaths(new[] { result.OutputPath });
            }
            else
            {
                MessageBox.Show(
                    this,
                    $"Saved {(result.CaveName ?? docId)} with {result.AssetCount} cartography asset(s).\n\n{result.OutputPath}",
                    "Download as backup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Download as backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            Title = "Public Cave Library — CaveAI Pro";
        }
    }
}
