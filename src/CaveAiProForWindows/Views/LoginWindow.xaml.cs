using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.Web.WebView2.Core;

namespace CaveAiProForWindows.Views;

/// <summary>
/// Startup gate: Google sign-in via WebView2, then Firestore <c>user_entitlements</c> monthly subscription check.
/// </summary>
public partial class LoginWindow : Window
{
    private readonly FirebaseAuthTokenCache _cache = CloudPublishWebViewHost.TokenCache;
    private readonly SubscriptionEntitlementService _entitlements = new();
    private bool _initialized;
    private bool _completed;
    private bool _validating;
    private string? _lastAccessDeniedDetail;
    private CancellationTokenRegistration _startupTimeoutRegistration;

    /// <summary>Linked to startup gate timeout from <see cref="AppLockBootstrapper"/>.</summary>
    public CancellationToken StartupCancellationToken { get; set; }

    public LoginWindow()
    {
        InitializeComponent();
#if DEBUG
        FallbackAuthButton.ToolTip =
            "Use bundled Firebase sign-in when the web auth page is unavailable (requires CAVEAIPRO_FIREBASE_API_KEY)";
#else
        FallbackAuthButton.ToolTip =
            "Use the bundled sign-in page when the live auth page is unavailable.";
#endif
        Loaded += OnLoadedAsync;
        Closed += (_, _) => _startupTimeoutRegistration.Dispose();
    }

    /// <summary>Called when the outer startup gate times out while this dialog is open.</summary>
    public void HandleStartupTimeout()
    {
        if (_completed)
            return;

        Debug.WriteLine("[LoginWindow] Startup gate timeout — closing dialog.");
        App.WriteStartupLog("LoginWindow: startup timeout");
        _ = HandleNoSubscriptionAsync(
            SubscriptionEntitlementResult.AccessDeniedNoSubscriptionMessage +
            "\n\nSign-in timed out while verifying your account.");
        DialogResult = false;
        Close();
    }

    private async void OnLoadedAsync(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;
        _initialized = true;

        AppStartupSplashController.DismissForAuthentication();

        if (StartupCancellationToken.CanBeCanceled)
        {
            _startupTimeoutRegistration = StartupCancellationToken.Register(() =>
            {
                try
                {
                    Dispatcher.Invoke(HandleStartupTimeout);
                }
                catch
                {
                    /* shutting down */
                }
            });
        }

        Activate();
        Focus();

        var cached = _cache.TryGetUsableToken();
        if (cached != null)
        {
            Debug.WriteLine("[LoginWindow] Cached Firebase token present — validating subscription.");
            if (await ValidateAndCompleteIfSubscribedAsync(cached).ConfigureAwait(true))
                return;

            await HandleNoSubscriptionAsync(_lastAccessDeniedDetail).ConfigureAwait(true);
        }

        await InitializeWebViewAsync().ConfigureAwait(true);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            SetIdleStatus("Starting secure sign-in…");
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
            core.Settings.IsScriptEnabled = true;
            // Do not customize UserAgent — Google OAuth rejects many embedded / modified agents.

            CloudPublishWebViewHost.EnsureAuthBridgeAttached(core);
            DesktopAuthFallback.TryRegisterVirtualHost(core);
            await DesktopAuthFallback.EnsureFirebaseConfigScriptRegisteredAsync(core).ConfigureAwait(true);

            WebView2AuthPopupHost.WirePopupHandling(core, this, IsAllowedNavigation);

            core.NavigationStarting += (_, args) =>
            {
                if (string.IsNullOrWhiteSpace(args.Uri))
                    return;

                Debug.WriteLine("[LoginWindow] NavigationStarting: " + args.Uri);
                App.WriteStartupLog("LoginWindow nav: " + args.Uri);
                FirebaseProjectConfig.TryObserveWebApiKeyFromUri(args.Uri);

                if (!IsAllowedNavigation(args.Uri))
                {
                    args.Cancel = true;
                    try
                    {
                        Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, ex.Message, "Open link", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            };

            _cache.TokenUpdated += OnCacheTokenUpdated;
            Closed += (_, _) => _cache.TokenUpdated -= OnCacheTokenUpdated;

            core.NavigationCompleted += async (_, _) =>
            {
                if (!DesktopAuthFallback.IsFallbackUri(core.Source))
                    return;
                await DesktopAuthFallback.PushFirebaseConfigToPageAsync(core).ConfigureAwait(true);
            };

            HideAccessDenied();
            await NavigateAuthEntryAsync(core).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LoginWindow] WebView2 init failed: " + ex.Message);
            SetIdleStatus("WebView2 could not start.");
            MessageBox.Show(
                this,
                "WebView2 could not start. Install the Microsoft Edge WebView2 Runtime.\n\n" + ex.Message,
                "Sign in — CAVE AI PRO",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void OnCacheTokenUpdated(object? sender, FirebaseIdToken token)
    {
        if (!token.IsUsable() || _validating || _completed)
            return;

        Debug.WriteLine(
            $"[LoginWindow] TokenUpdated — sub={token.Subject ?? "?"} email={token.Email ?? "?"} exp={token.ExpiresAtUtc:O}");

        _validating = true;
        try
        {
            if (await ValidateAndCompleteIfSubscribedAsync(token).ConfigureAwait(true))
                return;

            await HandleNoSubscriptionAsync(_lastAccessDeniedDetail).ConfigureAwait(true);
        }
        finally
        {
            _validating = false;
        }
    }

    /// <summary>Runs Firestore entitlement check; returns true only when subscription is active.</summary>
    private async Task<bool> ValidateAndCompleteIfSubscribedAsync(FirebaseIdToken token)
    {
        StartupCancellationToken.ThrowIfCancellationRequested();

        ShowValidatingOverlay("Verifying CaveAI Pro access…");
        Debug.WriteLine("[LoginWindow] Validating entitlement via Firestore…");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            StartupCancellationToken,
            new CancellationTokenSource(AppStartupGateOptions.SubscriptionValidationTimeout).Token);

        try
        {
            var result = await _entitlements.ValidateMonthlySubscriptionAsync(token, linked.Token)
                .ConfigureAwait(true);

            if (!result.IsEntitled)
            {
                Debug.WriteLine("[LoginWindow] Entitlement denied: " + (result.DenialReason ?? "unknown"));
                App.WriteStartupLog("LoginWindow: entitlement denied — " + (result.DenialReason ?? "unknown"));
                _lastAccessDeniedDetail = result.DenialReason;
                return false;
            }

            HideValidatingOverlay();
            _cache.Update(token);
            SetIdleStatus("Access verified — starting…");
            Debug.WriteLine(
                $"[LoginWindow] Entitlement granted ({result.AccessKind}) — completing startup.");
            App.WriteStartupLog(
                "LoginWindow: entitlement verified (" + result.AccessKind + ") for " +
                (token.Email ?? token.Subject ?? "?"));
            AccountSessionState.Apply(result);
            CompleteSuccess();
            return true;
        }
        catch (OperationCanceledException) when (StartupCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine("[LoginWindow] Subscription validation timed out.");
            App.WriteStartupLog("LoginWindow: subscription validation timed out");
            _lastAccessDeniedDetail =
                "Could not reach the subscription server in time. Check your internet connection and try again.";
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LoginWindow] Subscription validation error: " + ex.Message);
            App.WriteStartupLog("LoginWindow: subscription error — " + ex.Message);
            _lastAccessDeniedDetail =
                "Could not verify your CaveAI Pro subscription. Check your internet connection and try again.";
            return false;
        }
        finally
        {
            HideValidatingOverlay();
        }
    }

    /// <summary>Auth succeeded but subscription is missing — never leave splash/overlay spinning.</summary>
    private async Task HandleNoSubscriptionAsync(string? supplementalMessage = null)
    {
        AppStartupSplashController.DismissForAuthentication();
        HideValidatingOverlay();
        _validating = false;
        _cache.Clear();

        var message = SubscriptionEntitlementResult.AccessDeniedNoSubscriptionMessage;
        if (!string.IsNullOrWhiteSpace(supplementalMessage))
            message += "\n\n" + supplementalMessage.Trim();

        ShowAccessDenied(message);
        SetIdleStatus("Subscription required — choose another account or subscribe on Google Play.");
        Debug.WriteLine("[LoginWindow] Access denied — subscription inactive; auth screen reset.");

        await TrySignOutWebSessionAsync().ConfigureAwait(true);

        SwitchAccountButton.Focus();
        Activate();
    }

    private async Task TrySignOutWebSessionAsync()
    {
        try
        {
            var core = AuthWebView.CoreWebView2;
            if (core == null)
                return;

            await core.ExecuteScriptAsync(
                    """
                    try {
                      if (window.firebase && window.firebase.auth) window.firebase.auth().signOut();
                    } catch (e) {}
                    """)
                .ConfigureAwait(true);
            core.Reload();
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LoginWindow] Web sign-out skipped: " + ex.Message);
        }
    }

    private void CompleteSuccess()
    {
        if (_completed)
            return;
        _completed = true;
        HideAccessDenied();
        DialogResult = true;
        Close();
    }

    private void ShowAccessDenied(string message)
    {
        AccessDeniedBody.Text = message;
        AccessDeniedPanel.Visibility = Visibility.Visible;
        Activate();
    }

    private void HideAccessDenied()
    {
        AccessDeniedPanel.Visibility = Visibility.Collapsed;
        AccessDeniedBody.Text = "";
    }

    private void ShowValidatingOverlay(string text)
    {
        ValidatingOverlayText.Text = text;
        ValidatingOverlay.Visibility = Visibility.Visible;
        SwitchAccountButton.IsEnabled = false;
    }

    private void HideValidatingOverlay()
    {
        ValidatingOverlay.Visibility = Visibility.Collapsed;
        SwitchAccountButton.IsEnabled = true;
    }

    private void SetIdleStatus(string text) => StatusText.Text = text;

    private async void HostSignIn_Click(object sender, RoutedEventArgs e)
    {
        var core = AuthWebView?.CoreWebView2;
        if (core == null)
        {
            MessageBox.Show(this, "WebView2 is not ready yet.", "Sign in — CAVE AI PRO",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!DesktopAuthFallback.HasUsableFirebaseConfig())
        {
            MessageBox.Show(this, DesktopAuthFallback.MissingConfigUserMessage, "Sign in — CAVE AI PRO",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        HideAccessDenied();
        SetIdleStatus("Redirecting to Google…");
        HostSignInButton.IsEnabled = false;

        try
        {
            if (!DesktopAuthFallback.IsFallbackUri(core.Source))
            {
                await NavigateAuthEntryAsync(core).ConfigureAwait(true);
                await Task.Delay(400).ConfigureAwait(true);
            }

            var clicked = await core.ExecuteScriptAsync(
                    """
                    (function () {
                      try {
                        var btn = document.getElementById('signin');
                        if (btn && !btn.disabled) { btn.click(); return 'clicked'; }
                        return 'not-ready';
                      } catch (e) { return 'error'; }
                    })();
                    """)
                .ConfigureAwait(true);

            App.WriteStartupLog("LoginWindow: host sign-in click — " + (clicked ?? "?").Trim('"'));
            SetIdleStatus("Continue sign-in in the WebView…");
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[LoginWindow] Host sign-in failed: " + ex.Message);
            SetIdleStatus("Sign-in failed — try again.");
            MessageBox.Show(this, ex.Message, "Sign in — CAVE AI PRO", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            HostSignInButton.IsEnabled = true;
        }
    }

    private async void SwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        _cache.Clear();
        HideAccessDenied();
        SetIdleStatus("Sign in with Google on the page below…");
        await TrySignOutWebSessionAsync().ConfigureAwait(true);
        var core = AuthWebView?.CoreWebView2;
        if (core != null)
            _ = NavigateAuthEntryAsync(core);
    }

    private async Task NavigateAuthEntryAsync(CoreWebView2 core)
    {
        if (DesktopAuthFallback.HasUsableFirebaseConfig())
        {
            await DesktopAuthFallback.PrepareFallbackNavigationAsync(core).ConfigureAwait(true);
            SetIdleStatus("Sign in with Google on the bundled auth page…");
            Debug.WriteLine("[LoginWindow] Navigating to bundled auth: " + DesktopAuthFallback.FallbackUri);
            core.Navigate(DesktopAuthFallback.FallbackUri);
            return;
        }

        SetIdleStatus("Sign in with Google on the Cave Library auth page…");
        Debug.WriteLine("[LoginWindow] Bundled Firebase config missing — using live desktop auth URL.");
        core.Navigate(PublicLibraryCatalog.DesktopAuthUrl);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _cache.Clear();
        DialogResult = false;
        Close();
    }

    private void FallbackAuth_Click(object sender, RoutedEventArgs e)
    {
        var core = AuthWebView?.CoreWebView2;
        if (core == null)
            return;
        HideAccessDenied();
        SetIdleStatus("Bundled sign-in — use Google on this page…");
        _ = NavigateFallbackAsync(core);
    }

    private async Task NavigateFallbackAsync(CoreWebView2 core)
    {
        if (!DesktopAuthFallback.HasUsableFirebaseConfig())
        {
            MessageBox.Show(
                this,
                DesktopAuthFallback.MissingConfigUserMessage,
                "Sign in — CAVE AI PRO",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        await DesktopAuthFallback.PrepareFallbackNavigationAsync(core).ConfigureAwait(true);
        core.Navigate(DesktopAuthFallback.FallbackUri);
        await DesktopAuthFallback.PushFirebaseConfigToPageAsync(core).ConfigureAwait(true);
    }

    private static bool IsAllowedNavigation(string uri)
    {
        if (DesktopAuthFallback.IsFallbackUri(uri))
            return true;

        return PublicLibraryWebWindowNavigationPolicy.IsAllowed(uri);
    }

    private void PlayStore_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(AccountLinks.PlayStoreAppUrl) { UseShellExecute = true });
    }

    private void PublicLibrary_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo(PublicLibraryCatalog.WebMapUrl) { UseShellExecute = true });
    }
}
