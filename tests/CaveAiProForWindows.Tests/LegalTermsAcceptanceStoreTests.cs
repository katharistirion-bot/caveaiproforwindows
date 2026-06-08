using CaveAiProForWindows.Services;
using CaveAiProForWindows.Services.Legal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class LegalTermsAcceptanceStoreTests
{
    private string? _tempDir;
    private string? _prevEnv;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "caveai-legal-" + Guid.NewGuid().ToString("N"));
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
    public void Load_false_when_never_accepted()
    {
        Assert.IsFalse(LegalTermsAcceptanceStore.Load());
    }

    [TestMethod]
    public void Save_and_Load_roundtrip_current_document_version()
    {
        LegalTermsAcceptanceStore.Save(true);
        Assert.IsTrue(LegalTermsAcceptanceStore.Load());

        var state = LegalTermsAcceptanceStore.LoadState();
        Assert.IsTrue(state.IsAcceptedForCurrentDocument);
        Assert.AreEqual(LegalTexts.DocumentVersion, state.AcceptedDocumentVersion);
    }

    [TestMethod]
    public void Load_false_when_stored_version_mismatch()
    {
        var path = Path.Combine(_tempDir!, "legal_terms_acceptance.json");
        File.WriteAllText(
            path,
            """{"Accepted":true,"AcceptedDocumentVersion":"0.1","AcceptedAtUtc":"2026-01-01T00:00:00Z"}""");

        Assert.IsFalse(LegalTermsAcceptanceStore.Load());
        Assert.IsFalse(LegalTermsAcceptanceStore.LoadState().IsAcceptedForCurrentDocument);
    }
}
