using CaveAiProForWindows.Services.GenerativeMap;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class ReplicateByokTests
{
    [TestMethod]
    public void ReplicateTokenGate_IsAuthError_detects_unauthorized_messages()
    {
        Assert.IsTrue(ReplicateTokenGate.IsAuthError(new InvalidOperationException("Replicate create prediction failed (401): Unauthorized")));
        Assert.IsTrue(ReplicateTokenGate.IsAuthError(new InvalidOperationException("403 Forbidden")));
        Assert.IsFalse(ReplicateTokenGate.IsAuthError(new InvalidOperationException("Replicate prediction timed out.")));
    }

    [TestMethod]
    public void ReplicateApiTokenValidator_IsAuthFailure_recognizes_401_and_403()
    {
        Assert.IsTrue(ReplicateApiTokenValidator.IsAuthFailure(System.Net.HttpStatusCode.Unauthorized));
        Assert.IsTrue(ReplicateApiTokenValidator.IsAuthFailure(System.Net.HttpStatusCode.Forbidden));
        Assert.IsFalse(ReplicateApiTokenValidator.IsAuthFailure(System.Net.HttpStatusCode.BadRequest));
    }

    [TestMethod]
    public void ReplicateApiTokenValidator_rejects_empty_token()
    {
        Assert.IsFalse(ReplicateApiTokenValidator.ValidateAsync("").GetAwaiter().GetResult());
        Assert.IsFalse(ReplicateApiTokenValidator.ValidateAsync("   ").GetAwaiter().GetResult());
    }
}
