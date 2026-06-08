using System.Windows;
using CaveAiProForWindows.Services.Secrets;
using CaveAiProForWindows.Views;

namespace CaveAiProForWindows.Services.GenerativeMap;

/// <summary>Blocks generative AI calls when the user's Replicate token is missing; prompts for API Settings.</summary>
public static class ReplicateTokenGate
{
    public static bool EnsureConfigured(Window? owner, out string? errorMessage)
    {
        errorMessage = null;
        if (ReplicateApiTokenStore.IsConfigured())
            return true;

        errorMessage =
            "A Replicate API token is required. CAVE AI PRO uses Bring Your Own Key — your token stays on this PC and is never uploaded with survey data.";
        PromptOpenApiSettings(owner);
        return false;
    }

    public static void PromptOpenApiSettings(Window? owner)
    {
        var result = MessageBox.Show(
            owner,
            "Generative AI requires your personal Replicate API token (Bring Your Own Key).\n\n"
            + "Open API Settings now to paste and save your token in Windows Credential Manager?",
            "Replicate API token required",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
            ApiSettingsWindow.Show(owner);
    }

    public static void PromptInvalidToken(Window? owner)
    {
        var result = MessageBox.Show(
            owner,
            "Replicate rejected your API token (invalid or expired).\n\nOpen API Settings to enter a new token?",
            "Replicate API token invalid",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            ApiSettingsWindow.Show(owner);
    }

    public static bool IsAuthError(Exception ex) =>
        ex.Message.Contains("401", StringComparison.Ordinal)
        || ex.Message.Contains("403", StringComparison.Ordinal)
        || ex.Message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("invalid authentication", StringComparison.OrdinalIgnoreCase);
}
