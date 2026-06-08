using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaveAiProForWindows.Models;
using CaveAiProForWindows.Services;

namespace CaveAiProForWindows.Services.Security;

/// <summary>Packages project JSON (and optional assets) into an AES-encrypted <c>.caveai</c> bundle.</summary>
public static class CaveAiSecureBundleWriter
{
    public static void WriteBundle(
        CaveProjectDocument project,
        string outputPath,
        string password,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Output path required.", nameof(outputPath));
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Password required for encryption.", nameof(password));

        var zipBytes = BuildInnerZip(project, extraEntries);
        var encrypted = EncryptPayload(zipBytes, password);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllBytes(outputPath, encrypted);
    }

    private static byte[] BuildInnerZip(
        CaveProjectDocument project,
        IReadOnlyDictionary<string, byte[]>? extraEntries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var json = SurveyPortableZipExporter.SerializeSingleProjectArray(
                CaveProjectDocumentJsonDefaults.PrepareForSerialization(project));
            WriteEntry(zip, CaveAiSecureBundleFormat.PrimaryEntryName, new UTF8Encoding(true).GetBytes(json));

            var manifest = JsonSerializer.Serialize(new
            {
                format = "caveai-secure-v1",
                project = project.Name,
                createdUtc = DateTime.UtcNow,
            });
            WriteEntry(zip, CaveAiSecureBundleFormat.ManifestEntryName, Encoding.UTF8.GetBytes(manifest));

            if (extraEntries != null)
            {
                foreach (var (name, bytes) in extraEntries)
                    WriteEntry(zip, name, bytes);
            }
        }

        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string name, byte[] bytes)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    internal static byte[] EncryptPayload(byte[] plaintext, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(CaveAiSecureBundleFormat.SaltBytes);
        var nonce = RandomNumberGenerator.GetBytes(CaveAiSecureBundleFormat.NonceBytes);
        var key = DeriveKey(password, salt);

        var cipher = new byte[plaintext.Length];
        var tag = new byte[CaveAiSecureBundleFormat.TagBytes];

        using var aes = new AesGcm(key, CaveAiSecureBundleFormat.TagBytes);
        aes.Encrypt(nonce, plaintext, cipher, tag);

        using var outMs = new MemoryStream();
        outMs.Write(CaveAiSecureBundleFormat.Magic);
        outMs.WriteByte(CaveAiSecureBundleFormat.FormatVersion);
        outMs.Write(salt);
        outMs.Write(nonce);
        outMs.Write(tag);
        outMs.Write(cipher);
        return outMs.ToArray();
    }

    internal static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            CaveAiSecureBundleFormat.Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            CaveAiSecureBundleFormat.KeyBytes);
    }
}
