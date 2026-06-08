using System.Diagnostics;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Hosts Google / Firebase OAuth popups from <c>signInWithPopup</c> and <c>window.open</c>.
/// Setting <see cref="CoreWebView2NewWindowRequestedEventArgs.Handled"/> without
/// <see cref="CoreWebView2NewWindowRequestedEventArgs.NewWindow"/> closes the popup immediately.
/// </summary>
internal static class WebView2AuthPopupHost
{
    public static void WirePopupHandling(
        CoreWebView2 opener,
        Window owner,
        Func<string, bool> isNavigationAllowed)
    {
        ArgumentNullException.ThrowIfNull(opener);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(isNavigationAllowed);

        opener.NewWindowRequested += (_, args) =>
            _ = HandleNewWindowRequestedAsync(opener, owner, args, isNavigationAllowed);
    }

    private static async Task HandleNewWindowRequestedAsync(
        CoreWebView2 opener,
        Window owner,
        CoreWebView2NewWindowRequestedEventArgs args,
        Func<string, bool> isNavigationAllowed)
    {
        args.Handled = true;
        var deferral = args.GetDeferral();

        try
        {
            var environment = opener.Environment
                ?? throw new InvalidOperationException("WebView2 environment is not initialized.");

            var popupWidth = 520;
            var popupHeight = 720;
            if (args.WindowFeatures is { HasSize: true })
            {
                popupWidth = Math.Max(480, (int)args.WindowFeatures.Width);
                popupHeight = Math.Max(640, (int)args.WindowFeatures.Height);
            }

            var popupWindow = new Window
            {
                Owner = owner,
                Title = "Sign in — Google",
                Width = popupWidth,
                Height = popupHeight,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Background = System.Windows.Media.Brushes.White,
            };

            var popupWebView = new WebView2();
            popupWindow.Content = popupWebView;

            popupWindow.Closed += (_, _) =>
            {
                try
                {
                    popupWebView.Dispose();
                }
                catch
                {
                    /* shutting down */
                }
            };

            await popupWebView.EnsureCoreWebView2Async(environment).ConfigureAwait(true);

            var popupCore = popupWebView.CoreWebView2
                ?? throw new InvalidOperationException("OAuth popup WebView2 core is unavailable.");

            popupCore.Settings.AreDefaultScriptDialogsEnabled = true;
            popupCore.Settings.IsScriptEnabled = true;
            popupCore.Settings.AreDefaultContextMenusEnabled = true;
            popupCore.Settings.IsStatusBarEnabled = false;

            WirePopupHandling(popupCore, popupWindow, isNavigationAllowed);

            popupCore.NavigationStarting += (_, navArgs) =>
            {
                if (string.IsNullOrWhiteSpace(navArgs.Uri))
                    return;
                if (!isNavigationAllowed(navArgs.Uri))
                {
                    navArgs.Cancel = true;
                    Debug.WriteLine("[WebView2AuthPopup] Blocked navigation: " + navArgs.Uri);
                }
            };

            popupCore.WindowCloseRequested += (_, _) =>
            {
                try
                {
                    popupWindow.Close();
                }
                catch
                {
                    /* ignore */
                }
            };

            args.NewWindow = popupCore;
            popupWindow.Show();

            Debug.WriteLine("[WebView2AuthPopup] Opened OAuth popup for: " + (args.Uri ?? "(no uri)"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[WebView2AuthPopup] Failed to open OAuth popup: " + ex.Message);
            App.WriteStartupLog("WebView2AuthPopup: " + ex.Message);

            if (!string.IsNullOrWhiteSpace(args.Uri) && isNavigationAllowed(args.Uri))
            {
                try
                {
                    opener.Navigate(args.Uri);
                }
                catch
                {
                    /* last resort failed */
                }
            }
        }
        finally
        {
            deferral.Complete();
        }
    }
}
