using System.IO;

namespace CaveAiProForWindows.Services;

/// <summary>Calm, actionable user-facing error copy. See docs/ERROR_COPY_GUIDE.md (website repo).</summary>
public static class UserFacingErrors
{
    public static string LastErrorLogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CaveAiProForWindows",
            "last-error.txt");

    public static string StartupFailed(Exception? ex = null)
    {
        var path = LastErrorLogPath;
        var detail = ex?.Message;
        var blocks = new List<string>
        {
            "CaveAI Pro could not finish starting.",
            "This is usually a one-time setup or sign-in issue - your survey files on disk are not affected.",
            "What to do:\r\n" +
            "1. Close the app completely and open it again.\r\n" +
            "2. If sign-in was in progress, complete Google sign-in and subscription check.\r\n" +
            "3. If it keeps happening, send the support file below to CaveAI Pro support.",
            "Support log saved to:\r\n" + path,
        };
        if (!string.IsNullOrWhiteSpace(detail) &&
            !detail.Contains("Exception", StringComparison.OrdinalIgnoreCase))
        {
            blocks.Add("Summary: " + detail.Trim());
        }

        return string.Join("\r\n\r\n", blocks);
    }

    public static string SurfaceMapWebViewFailed() =>
        "The surface map could not start on this PC.\r\n\r\n" +
        "Why: the WebView2 component may be missing or blocked.\r\n\r\n" +
        "What to do:\r\n" +
        "1. Install or update Microsoft Edge WebView2 Runtime from microsoft.com/edge/webview2.\r\n" +
        "2. Restart CaveAI Pro and open Surface Map again.\r\n" +
        "3. Use Open in browser as a temporary workaround.";

    public static string SurfaceMapNavigationFailed() =>
        "The surface map page did not load.\r\n\r\n" +
        "Why: offline mode, blocked network, or missing map assets.\r\n\r\n" +
        "What to do:\r\n" +
        "1. Check your internet connection.\r\n" +
        "2. Cached tiles may still appear - pan the map to retry.\r\n" +
        "3. Tap Open in browser if the embedded map keeps failing.";

    public static string CloudPublishFailed(string? technicalMessage = null)
    {
        if (TryMapCloudPublish(technicalMessage, out var mapped))
            return mapped;

        return "Publish to the Public Library did not complete.\r\n\r\n" +
               "Why: sign-in, network, or upload checks may have failed.\r\n\r\n" +
               "What to do:\r\n" +
               "1. Confirm you are signed in with Google (Push to Cloud - Sign in).\r\n" +
               "2. Check your internet connection.\r\n" +
               "3. Accept the legal disclaimer on LEGAL and SETTINGS, then try again.";
    }

    public static string AuthTokenExpired() =>
        "Your Google sign-in session expired.\r\n\r\n" +
        "Why: Firebase tokens time out after a while for security.\r\n\r\n" +
        "What to do:\r\n" +
        "1. Open Push to Cloud or sign in again from the toolbar.\r\n" +
        "2. Complete Google sign-in in the browser window.\r\n" +
        "3. Retry your publish or cloud action.";

    public static string SignInTimedOut() =>
        "Sign-in did not finish in time.\r\n\r\n" +
        "Why: the Google or subscription check window may have been closed early.\r\n\r\n" +
        "What to do:\r\n" +
        "1. Start the app again.\r\n" +
        "2. Complete Google sign-in and wait for subscription verification.\r\n" +
        "3. Check your internet connection.";

    private static bool TryMapCloudPublish(string? message, out string mapped)
    {
        mapped = "";
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var m = message.Trim();
        if (m.Contains("token", StringComparison.OrdinalIgnoreCase) &&
            (m.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
             m.Contains("missing", StringComparison.OrdinalIgnoreCase)))
        {
            mapped = AuthTokenExpired();
            return true;
        }

        if (m.Contains("disclaimer", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("LEGAL", StringComparison.Ordinal))
        {
            mapped = "Accept the disclaimer on LEGAL and SETTINGS before using Push to Cloud.";
            return true;
        }

        if (m.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            mapped = "Publish was cancelled - nothing was uploaded.";
            return true;
        }

        if (m.Contains("Select a survey", StringComparison.OrdinalIgnoreCase))
        {
            mapped = "Open or select a survey project before publishing.";
            return true;
        }

        if (m.Contains("Storage upload failed", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Firestore", StringComparison.OrdinalIgnoreCase))
        {
            mapped = "Upload to the cloud did not complete.\r\n\r\n" +
                     "Why: network interruption or permission check failed.\r\n\r\n" +
                     "What to do:\r\n" +
                     "1. Check your connection.\r\n" +
                     "2. Sign in again with Google.\r\n" +
                     "3. Retry Push to Cloud.";
            return true;
        }

        if (m.Contains("timed out", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("Timeout", StringComparison.Ordinal))
        {
            mapped = SignInTimedOut();
            return true;
        }

        return false;
    }
}