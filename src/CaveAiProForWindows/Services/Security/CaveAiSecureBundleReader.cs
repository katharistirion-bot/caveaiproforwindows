using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;

namespace CaveAiProForWindows.Services.Security;

/// <summary>Decrypts and extracts a <c>.caveai</c> secure bundle locally.</summary>
public static class CaveAiSecureBundleReader
{
    public sealed record DecryptedBundle(
        IReadOnlyList<CaveProjectDocument> Projects,
        IReadOnlyDictionary<string, byte[]> Entries);

    public static DecryptedBundle OpenBundle(string bundlePath, string password)
    {
        if (!File.Exists(bundlePath))
            throw new FileNotFoundException("Bundle not found.", bundlePath);
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password required.", nameof(password));

        var fileBytes = File.ReadAllBytes(bundlePath);
        var zipBytes = DecryptPayload(fileBytes, password);
        return ExtractInnerZip(zipBytes);
    }

    internal static byte[] DecryptPayload(byte[] fileBytes, string password)
    {
        var magicLen = CaveAiSecureBundleFormat.Magic.Length;
        if (fileBytes.Length < magicLen + 1 + CaveAiSecureBundleFormat.SaltBytes +
            CaveAiSecureBundleFormat.NonceBytes + CaveAiSecureBundleFormat.TagBytes + 1)
        {
            throw new InvalidDataException("Bundle file too short.");
        }

        for (var i = 0; i < magicLen; i++)
        {
            if (fileBytes[i] != CaveAiSecureBundleFormat.Magic[i])
                throw new InvalidDataException("Not a CAVE AI secure bundle (bad magic).");
        }

        var version = fileBytes[magicLen];
        if (version != CaveAiSecureBundleFormat.FormatVersion)
            throw new NotSupportedException($"Unsupported bundle version {version}.");

        var o = magicLen + 1;
        var salt = fileBytes.AsSpan(o, CaveAiSecureBundleFormat.SaltBytes).ToArray();
        o += CaveAiSecureBundleFormat.SaltBytes;
        var nonce = fileBytes.AsSpan(o, CaveAiSecureBundleFormat.NonceBytes).ToArray();
        o += CaveAiSecureBundleFormat.NonceBytes;
        var tag = fileBytes.AsSpan(o, CaveAiSecureBundleFormat.TagBytes).ToArray();
        o += CaveAiSecureBundleFormat.TagBytes;
        var cipher = fileBytes.AsSpan(o).ToArray();

        var key = CaveAiSecureBundleWriter.DeriveKey(password, salt);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, CaveAiSecureBundleFormat.TagBytes);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static DecryptedBundle ExtractInnerZip(byte[] zipBytes)
    {
        using var ms = new MemoryStream(zipBytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in zip.Entries)
        {
            if (string.IsNullOrEmpty(e.Name))
                continue;
            using var es = e.Open();
            using var buf = new MemoryStream();
            es.CopyTo(buf);
            entries[e.FullName.Replace('\\', '/')] = buf.ToArray();
        }

        if (!entries.TryGetValue(CaveAiSecureBundleFormat.PrimaryEntryName, out var jsonBytes))
            throw new InvalidDataException("Bundle missing data.json.");

        var jsonText = Encoding.UTF8.GetString(jsonBytes);
        var projects = JsonSerializer.Deserialize<List<CaveProjectDocument>>(jsonText)
            ?? throw new InvalidDataException("data.json parse failed.");

        return new DecryptedBundle(projects, entries);
    }
}
