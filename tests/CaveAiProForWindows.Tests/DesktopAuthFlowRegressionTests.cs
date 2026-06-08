using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

/// <summary>Guards against regressions in the WebView2 Google sign-in flow.</summary>
[TestClass]
public sealed class DesktopAuthFlowRegressionTests
{
    private static string RepoRoot =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    private static string BundledAuthHtmlPath =>
        Path.Combine(RepoRoot, "src", "CaveAiProForWindows", "Assets", "DesktopAuth", "auth.html");

    [TestMethod]
    public void Bundled_auth_html_follows_host_redirect_v2_invariants()
    {
        Assert.IsTrue(File.Exists(BundledAuthHtmlPath), "Missing auth.html at " + BundledAuthHtmlPath);
        var html = File.ReadAllText(BundledAuthHtmlPath);
        var errors = DesktopAuthFlowInvariants.ValidateBundledAuthHtml(html);
        Assert.AreEqual(0, errors.Count, string.Join(Environment.NewLine, errors));
    }

    [TestMethod]
    public void OAuth_callback_uri_is_detected_and_outbound_handler_is_not()
    {
        var callback = "https://caveaipro-5950e.firebaseapp.com/__/auth/handler"
                       + "?state=x&code=4%2F0abc&scope=email";
        var outbound = "https://caveaipro-5950e.firebaseapp.com/__/auth/handler"
                       + "?apiKey=k&authType=signInViaRedirect&redirectUrl=https%3A%2F%2Flocalhost%2Fauth.html";

        Assert.IsTrue(DesktopAuthFlowInvariants.IsOAuthCallbackUri(callback));
        Assert.IsFalse(DesktopAuthFlowInvariants.IsOAuthCallbackUri(outbound));
    }

    [TestMethod]
    public void Post_exchange_navigation_always_returns_to_bundled_auth_page()
    {
        StringAssert.Contains(DesktopAuthFlowInvariants.PostExchangeNavigationUri, "localhost");
        StringAssert.Contains(DesktopAuthFlowInvariants.PostExchangeNavigationUri, "auth.html");
    }

    [TestMethod]
    public void ValidateBundledAuthHtml_rejects_forbidden_signInWithRedirect_pattern()
    {
        var html = "<script>auth.signInWithRedirect(provider)</script>";
        var errors = DesktopAuthFlowInvariants.ValidateBundledAuthHtml(html);
        Assert.IsTrue(errors.Count > 0);
        Assert.IsTrue(errors.Any(e => e.Contains("signInWithRedirect", StringComparison.Ordinal)));
    }
}

[TestClass]
public sealed class DesktopAuthOAuthExchangeGateTests
{
    [TestMethod]
    public void TryBeginExchange_allows_first_callback_and_blocks_duplicate_code()
    {
        var gate = new DesktopAuthOAuthExchangeGate();
        Assert.IsTrue(gate.TryBeginExchange("code:abc"));
        gate.EndExchange();

        Assert.IsFalse(gate.TryBeginExchange("code:abc"));
    }

    [TestMethod]
    public void TryBeginExchange_blocks_concurrent_exchanges()
    {
        var gate = new DesktopAuthOAuthExchangeGate();
        Assert.IsTrue(gate.TryBeginExchange("code:first"));
        Assert.IsFalse(gate.TryBeginExchange("code:second"));
        gate.EndExchange();
        Assert.IsTrue(gate.TryBeginExchange("code:second"));
        gate.EndExchange();
    }
}
