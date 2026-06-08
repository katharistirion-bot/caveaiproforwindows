using CaveAiProForWindows.Services.CloudPublish;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class DesktopAuthHandlerCallbackTests
{
    [TestMethod]
    public void TryParse_ExtractsOAuthCodeFromQuery()
    {
        var uri = "https://caveaipro-5950e.firebaseapp.com/__/auth/handler"
                  + "?state=abc&code=4%2F0AdkVLP_test&scope=email";

        Assert.IsTrue(DesktopAuthHandlerCallback.TryParse(uri, out var cb));
        Assert.IsNotNull(cb);
        Assert.IsTrue(cb!.PostBody.Contains("code=4%2F0AdkVLP_test", StringComparison.Ordinal));
        Assert.IsTrue(cb.PostBody.Contains("providerId=google.com", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryParse_ExtractsGoogleIdTokenFromFragment()
    {
        var uri = "https://caveaipro-5950e.firebaseapp.com/__/auth/handler"
                  + "#state=abc&id_token=eyJ.test.token&authuser=0";

        Assert.IsTrue(DesktopAuthHandlerCallback.TryParse(uri, out var cb));
        Assert.IsNotNull(cb);
        Assert.IsTrue(cb!.PostBody.StartsWith("id_token=", StringComparison.Ordinal));
        Assert.IsTrue(cb.PostBody.Contains("providerId=google.com", StringComparison.Ordinal));
    }

    [TestMethod]
    public void TryParse_RejectsUnrelatedUrls()
    {
        Assert.IsFalse(DesktopAuthHandlerCallback.TryParse("https://localhost/auth.html", out _));
        Assert.IsFalse(DesktopAuthHandlerCallback.TryParse(
            "https://caveaipro-5950e.firebaseapp.com/__/auth/handler?apiKey=x", out _));
    }
}
