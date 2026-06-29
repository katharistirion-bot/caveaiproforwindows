using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Legal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class LegalTermsLaunchGateTests
{
    private string? _tempDir;
    private string? _prevEnv;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-legal-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _prevEnv = Environment.GetEnvironmentVariable("CAVEAI_LEGAL_TERMS_STORE_DIR");
        Environment.SetEnvironmentVariable("CAVEAI_LEGAL_TERMS_STORE_DIR", _tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("CAVEAI_LEGAL_TERMS_STORE_DIR", _prevEnv);
        if (_tempDir != null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [TestMethod]
    public void NeedsAcceptance_true_when_never_accepted()
    {
        Assert.IsTrue(LegalTermsLaunchGate.NeedsAcceptance);
    }

    [TestMethod]
    public void NeedsAcceptance_false_when_current_document_accepted()
    {
        LegalTermsAcceptanceStore.Save(true);
        Assert.IsFalse(LegalTermsLaunchGate.NeedsAcceptance);
    }

    [TestMethod]
    public void NeedsAcceptance_true_when_stored_version_mismatch()
    {
        var path = Path.Combine(_tempDir!, "legal_terms_acceptance.json");
        File.WriteAllText(
            path,
            """{"Accepted":true,"AcceptedDocumentVersion":"0.1","AcceptedAtUtc":"2026-01-01T00:00:00Z"}""");

        Assert.IsTrue(LegalTermsLaunchGate.NeedsAcceptance);
    }
}
