using System.IO;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Views;

/// <summary>In-app Public Library browser that returns a <c>published_caves</c> document id from the map URL.</summary>
public partial class LibraryCavePickerWindow : Window
{
    private bool _initialized;
    private string? _selectedDocId;

    public LibraryCavePickerWindow()
    {
        InitializeComponent();
        Loaded += OnLoadedAsync;
    }

    public static bool TryPick(Window? owner, out string? docId)
    {
        var dlg = new LibraryCavePickerWindow { Owner = owner };
        var ok = dlg.ShowDialog() == true;
        docId = ok ? dlg._selectedDocId : null;
        return ok && !string.IsNullOrWhiteSpace(docId);
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
            await PickerWebView.EnsureCoreWebView2Async(environment);

            var core = PickerWebView.CoreWebView2
                ?? throw new InvalidOperationException("WebView2 core is unavailable.");

            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = false;
            CloudPublishWebViewHost.EnsureAuthBridgeAttached(core);

            WebView2AuthPopupHost.WirePopupHandling(core, this, IsAllowedNavigation);

            core.NavigationStarting += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Uri))
                    return;
                if (!IsAllowedNavigation(args.Uri))
                {
                    args.Cancel = true;
                    TryOpenExternal(args.Uri);
                }
            };

            core.SourceChanged += (_, _) => UpdateDetectedId(core.Source);
            core.NavigationCompleted += (_, _) => UpdateDetectedId(core.Source);

            core.Navigate(PublicLibraryCatalog.WebMapUrlEmbedded);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "WebView2 could not start.\n\n" + ex.Message,
                "Public Cave Library",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void UpdateDetectedId(string? url)
    {
        if (PublishedCaveUrlParser.TryExtractDocId(url, out var id))
        {
            _selectedDocId = id;
            DetectedIdText.Text = id;
            UseCaveButton.IsEnabled = true;
            return;
        }

        _selectedDocId = null;
        DetectedIdText.Text = "(none — open a cave on the map)";
        UseCaveButton.IsEnabled = false;
    }

    private void UseCave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDocId))
            return;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (PickerWebView?.CoreWebView2?.CanGoBack == true)
            PickerWebView.CoreWebView2.GoBack();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) =>
        PickerWebView?.CoreWebView2?.Reload();

    private void TryOpenExternal(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static bool IsAllowedNavigation(string uri) =>
        PublicLibraryWebWindowNavigationPolicy.IsAllowed(uri);
}
