using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CaveAiProForWindows.Tests;

[TestClass]
public sealed class IntegrityVerifierTests
{
    [TestMethod]
    public void VerifyZip_no_manifest_skips_with_reason()
    {
        var zip = Path.Combine(Path.GetTempPath(), "caveai_test_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var fs = File.Create(zip))
            using (var zipArchive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var e = zipArchive.CreateEntry("readme.txt");
                using var w = new StreamWriter(e.Open(), Encoding.UTF8);
                w.Write("x");
            }

            var r = IntegrityVerifier.VerifyZip(zip);
            Assert.IsNotNull(r.SkippedReason);
            StringAssert.Contains(r.SkippedReason, "integrity_manifest");
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch
            {
                /* best effort */
            }
        }
    }

    [TestMethod]
    public void VerifyZip_matching_sha256_reports_success()
    {
        const string entryName = "payload.bin";
        ReadOnlySpan<byte> payload = "hello-caveai"u8;
        var expectedHex = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        var manifest = new Dictionary<string, string> { [entryName] = expectedHex };
        var manifestJson = JsonSerializer.Serialize(new { files = manifest });

        var zip = Path.Combine(Path.GetTempPath(), "caveai_integ_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var fs = File.Create(zip))
            using (var zipArchive = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var m = zipArchive.CreateEntry("integrity_manifest.json");
                using (var mw = new StreamWriter(m.Open(), Encoding.UTF8))
                    mw.Write(manifestJson);

                var p = zipArchive.CreateEntry(entryName);
                using (var ps = p.Open())
                    ps.Write(payload);
            }

            var r = IntegrityVerifier.VerifyZip(zip);
            Assert.IsNull(r.SkippedReason);
            Assert.IsTrue(r.IsCompleteSuccess);
            Assert.AreEqual(1, r.FilesChecked);
            Assert.AreEqual(1, r.FilesMatched);
        }
        finally
        {
            try
            {
                File.Delete(zip);
            }
            catch
            {
                /* best effort */
            }
        }
    }
}
