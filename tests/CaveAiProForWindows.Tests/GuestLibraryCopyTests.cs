using System.Net.Http;
using CaveAiProForWindows.Services.Auth;
using CaveAiProForWindows.Services.Localization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class GuestLibraryCopyTests
{
    [TestMethod]
    public void Canonical_strings_are_google_only()
    {
        StringAssert.Contains(GuestLibraryCopy.SignInHeadingPanel, "Google");
        StringAssert.Contains(GuestLibraryCopy.ToolbarHint, "Google");
        Assert.IsFalse(GuestLibraryCopy.LeadPanel.Contains("Facebook", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(GuestLibraryCopy.FaqAnswer.Contains("Facebook", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(GuestLibraryCopy.CatalogScale, "76k");
        StringAssert.Contains(GuestLibraryCopy.LeadPanel, "not a guest free library");
    }

    [TestMethod]
    public void App_strings_reuse_guest_copy_not_a_free_catalog()
    {
        Assert.AreEqual(GuestLibraryCopy.SignInHeadingPanel, AppStrings.LoginAccessDeniedPublicLibrary);
        StringAssert.Contains(AppStrings.PostSignInStep2Body, GuestLibraryCopy.CatalogScale);
        Assert.IsFalse(AppStrings.LoginAccessDeniedPublicLibrary.Contains("free", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(AppStrings.PostSignInStep2Body.Contains("60k", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(AppStrings.PostSignInStep2Body.Contains("64k", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(AppStrings.PostSignInStep2Body, "not a guest free library");
        StringAssert.Contains(GuestLibraryCopy.EmptyCatalogStatus, GuestLibraryCopy.LeadPanel);
        StringAssert.Contains(
            GuestLibraryCopy.CatalogLoadFailure(new HttpRequestException(GuestLibraryCopy.SignInHeadingPanel)),
            GuestLibraryCopy.LeadPanel);
    }
}
