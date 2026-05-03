using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CaveAiProForWindows.Services;

/// <summary>Matches Android backup <c>integrity_manifest.json</c> (SHA-256 per ZIP entry).</summary>
public static class IntegrityVerifier
{
    public static IntegrityReport VerifyZip(string zipPath)
    {
        var report = new IntegrityReport();
        using var fs = File.OpenRead(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);

        var integEntry = zip.GetEntry("integrity_manifest.json");
        if (integEntry == null)
        {
            report.SkippedReason = "No integrity_manifest.json in this ZIP.";
            return report;
        }

        string jsonText;
        using (var sr = new StreamReader(integEntry.Open(), Encoding.UTF8))
            jsonText = sr.ReadToEnd();

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        if (!root.TryGetProperty("files", out var filesObj) || filesObj.ValueKind != JsonValueKind.Object)
        {
            report.SkippedReason = "integrity_manifest.json has no \"files\" object.";
            return report;
        }

        foreach (var prop in filesObj.EnumerateObject())
        {
            var zipInternalPath = prop.Name.Replace('\\', '/');
            var expectedHex = prop.Value.GetString()?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(expectedHex)) continue;

            var entry = zip.GetEntry(zipInternalPath);
            if (entry == null)
            {
                report.IncrementChecked();
                report.IncrementMissing();
                report.Mismatches.Add($"Missing in ZIP: {zipInternalPath}");
                continue;
            }

            using var stream = entry.Open();
            var actualHex = Sha256Hex(stream);
            report.IncrementChecked();
            if (string.Equals(actualHex, expectedHex, StringComparison.OrdinalIgnoreCase))
                report.IncrementMatched();
            else
                report.Mismatches.Add($"SHA mismatch: {zipInternalPath}\n  expected: {expectedHex}\n  actual:   {actualHex}");
        }

        return report;
    }

    private static string Sha256Hex(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
