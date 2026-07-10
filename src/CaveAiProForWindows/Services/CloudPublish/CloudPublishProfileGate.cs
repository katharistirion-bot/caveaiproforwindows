using System.Diagnostics;
using System.Windows;
using CaveAiProForWindows.Services.UserProfile;

namespace CaveAiProForWindows.Services.CloudPublish;

public static class CloudPublishProfileGate
{
    public const string ProfileSettingsUrl = "https://www.caveaipro.com/account/profile";

    public static async Task<bool> EnsureReadyForPublishAsync(
        FirebaseRestClient rest,
        FirebaseIdToken token,
        string publishedCaveDocId,
        Window? ownerWindow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rest);
        ArgumentNullException.ThrowIfNull(token);
        if (string.IsNullOrWhiteSpace(publishedCaveDocId))
            return false;

        PublishedCaveDocument cave;
        try
        {
            cave = await rest.GetPublishedCaveDocumentAsync(publishedCaveDocId.Trim(), token, cancellationToken)
                .ConfigureAwait(true);
        }
        catch
        {
            cave = new PublishedCaveDocument { DocumentId = publishedCaveDocId.Trim() };
        }

        if (HasPublisherIdentity(cave))
            return true;

        UserProfileDocument? profile;
        try
        {
            profile = await new UserProfileService().LoadProfileAsync(token, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(ownerWindow, ex.Message, "Publisher profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (profile?.IsCompleteForPublish() == true)
            return true;

        return PromptCompleteProfile(ownerWindow);
    }

    public static bool HasPublisherIdentity(PublishedCaveDocument cave) =>
        !string.IsNullOrWhiteSpace(cave.PublisherFirstName)
        && !string.IsNullOrWhiteSpace(cave.PublisherLastName)
        && !string.IsNullOrWhiteSpace(cave.PublisherCountry);

    public static bool PromptCompleteProfile(Window? ownerWindow)
    {
        var result = MessageBox.Show(
            ownerWindow,
            "Complete your publisher profile (first name, last name, country) before publishing to the Public Library.\n\nOpen your profile settings in the browser now?",
            "Publisher profile required",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return false;
        try
        {
            Process.Start(new ProcessStartInfo(ProfileSettingsUrl) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ownerWindow, $"Could not open the browser.\n\n{ProfileSettingsUrl}\n\n{ex.Message}", "Open profile settings", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return false;
    }
}