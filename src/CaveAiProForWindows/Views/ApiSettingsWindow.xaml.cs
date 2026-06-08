using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using CaveAiProForWindows.Services.GenerativeMap;
using CaveAiProForWindows.Services.Secrets;

namespace CaveAiProForWindows.Views;

/// <summary>BYOK settings — Replicate API token stored in Windows Credential Manager only.</summary>
public partial class ApiSettingsWindow : Window
{
    public ApiSettingsWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => RefreshStatus();
    }

    /// <summary>Shows the dialog; returns true if a token is configured after close.</summary>
    public static bool TryShow(Window? owner)
    {
        var dlg = new ApiSettingsWindow { Owner = owner };
        dlg.ShowDialog();
        return ReplicateApiTokenStore.IsConfigured();
    }

    public static void Show(Window? owner)
    {
        var dlg = new ApiSettingsWindow { Owner = owner };
        dlg.ShowDialog();
    }

    private void RefreshStatus()
    {
        StatusText.Text = ReplicateApiTokenStore.IsConfigured()
            ? "Status: token saved in Windows Credential Manager (CaveAiProForWindows:ReplicateApiToken)."
            : "Status: no token saved — AI Render is blocked until you save a valid token.";
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password?.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this, "Paste your Replicate API token first.", "API Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            TokenBox.Focus();
            return;
        }

        if (!ReplicateApiTokenStore.TrySave(token))
        {
            MessageBox.Show(this, "Could not save the token to Windows Credential Manager.", "API Settings",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        TokenBox.Password = "";
        RefreshStatus();

        var valid = await ReplicateApiTokenValidator.ValidateAsync(token).ConfigureAwait(true);
        if (valid)
        {
            MessageBox.Show(this, "Token saved and validated with Replicate.", "API Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            return;
        }

        ReplicateApiTokenStore.TryClear();
        RefreshStatus();
        MessageBox.Show(this,
            "The token was rejected by Replicate (invalid or expired). It was not kept on this PC.",
            "API Settings",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private async void Validate_Click(object sender, RoutedEventArgs e)
    {
        var token = TokenBox.Password?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            token = ReplicateApiTokenStore.TryRead();

        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this, "Enter a token in the box or save one first.", "API Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        StatusText.Text = "Validating with Replicate…";
        var valid = await ReplicateApiTokenValidator.ValidateAsync(token).ConfigureAwait(true);
        RefreshStatus();
        MessageBox.Show(this,
            valid ? "Token is valid." : "Token is invalid or Replicate rejected the request.",
            "API Settings",
            MessageBoxButton.OK,
            valid ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ReplicateApiTokenStore.TryClear();
        TokenBox.Password = "";
        RefreshStatus();
        MessageBox.Show(this, "Cleared the saved Replicate token from Windows Credential Manager.", "API Settings",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void ReplicateLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        e.Handled = true;
    }
}
