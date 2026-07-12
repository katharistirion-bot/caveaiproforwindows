using System.Windows;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using CaveAiProForWindows.Services.UserProfile;

namespace CaveAiProForWindows.Views;

public partial class PublisherProfileMiniWindow : Window
{
    public bool Saved { get; private set; }

    public PublisherProfileMiniWindow(UserProfileDocument? existing)
    {
        InitializeComponent();
        FirstNameBox.Text = existing?.FirstName ?? "";
        LastNameBox.Text = existing?.LastName ?? "";
        CountryBox.Text = existing?.Country ?? "";
    }

    public static bool TryPrompt(Window? owner, out bool saved)
    {
        saved = false;
        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token == null)
        {
            MessageBox.Show(owner, "Sign in before editing your publisher profile.", "Publisher profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        UserProfileDocument? existing = null;
        try { existing = new UserProfileService().LoadProfileAsync(token).GetAwaiter().GetResult(); } catch { }

        var dlg = new PublisherProfileMiniWindow(existing) { Owner = owner };
        var ok = dlg.ShowDialog() == true;
        saved = ok && dlg.Saved;
        return ok;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var first = FirstNameBox.Text.Trim();
        var last = LastNameBox.Text.Trim();
        var country = CountryBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last) || string.IsNullOrWhiteSpace(country))
        {
            MessageBox.Show(this, "Enter first name, last name, and country.", "Publisher profile",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var token = CloudPublishWebViewHost.TokenCache.TryGetUsableToken();
        if (token == null)
        {
            MessageBox.Show(this, "Sign in again and retry.", "Publisher profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await new UserProfileService().SaveProfileAsync(token, first, last, country).ConfigureAwait(true);
            Saved = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Publisher profile", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}